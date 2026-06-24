using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using RiftboundTcg.Server.Api.Data;
using RiftboundTcg.Server.Api.Models;
using RiftboundTcg.Server.Api.Realtime;
using riftbound_tcg.Engine.RulesEngine;
using riftbound_tcg.Core.Cards;
using riftbound_tcg.Core.DeckConstruction;

namespace RiftboundTcg.Server.Api.Services;

public sealed class OnlineGameService(GameDbContext db, IRulesEngine rulesEngine, IHubContext<MatchHub> hubContext)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] PlaceholderCardIds =
    [
        "ember-initiate",
        "glade-warden",
        "skyline-surge",
        "iron-vow",
        "ember-rune",
        "skybridge",
        "emberfield",
        "ember-legend",
        "ember-champion"
    ];

    public async Task EnsureCreatedAsync(CancellationToken cancellationToken)
    {
        await db.Database.EnsureCreatedAsync(cancellationToken);
        await MigrateSchemaAsync(cancellationToken);
        await CleanupPlaceholderCardsAsync(cancellationToken);

        if (!await db.Users.AnyAsync(cancellationToken))
        {
            var now = DateTimeOffset.UtcNow;
            db.Users.AddRange(
                new UserEntity { Id = "user-demo-001", Email = "demo1@riftbound.local", NormalizedEmail = "DEMO1@RIFTBOUND.LOCAL", DisplayName = "Demo Player One", PasswordHash = string.Empty, CreatedAt = now, UpdatedAt = now },
                new UserEntity { Id = "user-demo-002", Email = "demo2@riftbound.local", NormalizedEmail = "DEMO2@RIFTBOUND.LOCAL", DisplayName = "Demo Player Two", PasswordHash = string.Empty, CreatedAt = now, UpdatedAt = now });
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<CardDto>> ListCardsAsync(CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        return (await db.Cards.OrderBy(card => card.Name).ToArrayAsync(cancellationToken)).Select(ToDto).ToArray();
    }

    public async Task<CardDto?> GetCardAsync(string cardId, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        return await db.Cards.FindAsync([cardId], cancellationToken) is { } card ? ToDto(card) : null;
    }

    public async Task<CardUpsertResultDto> UpsertCardAsync(CardDto request, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var result = await UpsertCardCoreAsync(NormalizeCard(request), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<RiftCodexImportResultDto> ImportRiftCodexAsync(HttpClient httpClient, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var imported = 0;
        var updated = 0;
        var skipped = 0;
        var pagesProcessed = 0;
        var errors = new List<string>();
        const int size = 100;
        var page = 1;
        var totalPages = 1;

        while (page <= totalPages)
        {
            RiftCodexPage? response;
            try
            {
                response = await httpClient.GetFromJsonAsync<RiftCodexPage>($"https://api.riftcodex.com/cards?page={page}&size={size}", JsonOptions, cancellationToken);
            }
            catch (Exception ex)
            {
                errors.Add($"Page {page}: {ex.Message}");
                break;
            }

            if (response is null)
            {
                errors.Add($"Page {page}: Empty response.");
                break;
            }

            totalPages = Math.Max(1, response.Pages);
            foreach (var item in response.Items ?? [])
            {
                var mapped = MapRiftCodexCard(item);
                if (mapped is null)
                {
                    skipped += 1;
                    continue;
                }

                var upsert = await UpsertCardCoreAsync(mapped, cancellationToken);
                if (upsert.Created)
                {
                    imported += 1;
                }
                else
                {
                    updated += 1;
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            pagesProcessed += 1;
            page += 1;
        }

        return new RiftCodexImportResultDto(imported, updated, skipped, pagesProcessed, errors);
    }

    public async Task<bool?> DeleteCardAsync(string cardId, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var card = await db.Cards.FindAsync([cardId], cancellationToken);
        if (card is null)
        {
            return null;
        }

        var decks = await db.Decks
            .Where(deck => deck.DeletedAt == null)
            .ToArrayAsync(cancellationToken);
        var referencedByDeck = decks.Any(deck =>
            deck.LegendId == cardId ||
            deck.ChampionId == cardId ||
            Deserialize(deck.BattlefieldDeckIdsJson).Contains(cardId, StringComparer.OrdinalIgnoreCase) ||
            Deserialize(deck.RuneDeckIdsJson).Contains(cardId, StringComparer.OrdinalIgnoreCase) ||
            Deserialize(deck.MainDeckIdsJson).Contains(cardId, StringComparer.OrdinalIgnoreCase));
        if (referencedByDeck)
        {
            return false;
        }

        db.Cards.Remove(card);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<UserDto>> ListUsersAsync(CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        return await db.Users
            .OrderBy(user => user.DisplayName)
            .Select(user => AuthService.ToDto(user))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<UserDto?> GetUserAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        return await db.Users
            .Where(user => user.Id == userId)
            .Select(user => AuthService.ToDto(user))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<UserDto> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var email = $"{Guid.NewGuid():N}@legacy.riftbound.local";
        var user = new UserEntity
        {
            Id = $"user-{Guid.NewGuid():N}",
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? "Online Player" : request.DisplayName.Trim(),
            PasswordHash = string.Empty,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);
        return AuthService.ToDto(user);
    }

    public async Task<UserDto?> UpdateUserAsync(string userId, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var user = await db.Users.FindAsync([userId], cancellationToken);
        if (user is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(request.DisplayName))
        {
            user.DisplayName = request.DisplayName.Trim();
            user.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
        return AuthService.ToDto(user);
    }

    public async Task<UserDto?> AdminUpdateUserAsync(string currentAdminUserId, string userId, AdminUpdateUserRequest request, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var user = await db.Users.FindAsync([userId], cancellationToken);
        if (user is null)
        {
            return null;
        }

        var changingOwnAdminRole = user.Id == currentAdminUserId && request.IsAdmin == false && user.IsAdmin;
        var disablingSelf = user.Id == currentAdminUserId && request.IsDisabled == true && !user.IsDisabled;
        if (changingOwnAdminRole || disablingSelf)
        {
            throw new InvalidOperationException("Admins cannot disable or demote themselves.");
        }

        var wouldRemoveAdmin = user.IsAdmin && request.IsAdmin == false;
        var wouldDisableAdmin = user.IsAdmin && request.IsDisabled == true && !user.IsDisabled;
        if ((wouldRemoveAdmin || wouldDisableAdmin) && await CountEnabledAdminsAsync(cancellationToken) <= 1)
        {
            throw new InvalidOperationException("At least one enabled admin account must remain.");
        }

        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var email = request.Email.Trim();
            var normalized = email.ToUpperInvariant();
            if (await db.Users.AnyAsync(candidate => candidate.Id != user.Id && candidate.NormalizedEmail == normalized, cancellationToken))
            {
                throw new InvalidOperationException("Email is already registered.");
            }

            user.Email = email;
            user.NormalizedEmail = normalized;
        }

        if (!string.IsNullOrWhiteSpace(request.DisplayName))
        {
            user.DisplayName = request.DisplayName.Trim();
        }

        if (request.IsAdmin is not null)
        {
            user.IsAdmin = request.IsAdmin.Value;
        }

        if (request.IsDisabled is not null && user.IsDisabled != request.IsDisabled.Value)
        {
            user.IsDisabled = request.IsDisabled.Value;
            user.DisabledAt = user.IsDisabled ? DateTimeOffset.UtcNow : null;
        }

        user.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return AuthService.ToDto(user);
    }

    public async Task<IReadOnlyList<DeckDto>> ListDecksAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        return (await db.Decks
            .Where(deck => deck.DeletedAt == null && deck.OwnerUserId == userId)
            .OrderBy(deck => deck.Name)
            .ToArrayAsync(cancellationToken)).Select(ToDto).ToArray();
    }

    public async Task<IReadOnlyList<AdminDeckDto>> ListAdminDecksAsync(CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var decks = await db.Decks
            .Where(deck => deck.DeletedAt == null)
            .OrderBy(deck => deck.Name)
            .ToArrayAsync(cancellationToken);
        return await ToAdminDeckDtosAsync(decks, cancellationToken);
    }

    public async Task<IReadOnlyList<SharedDeckDto>> ListPublicDecksAsync(CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var decks = await db.Decks
            .Where(deck => deck.DeletedAt == null && deck.Visibility == "public")
            .OrderBy(deck => deck.Name)
            .ToArrayAsync(cancellationToken);
        return await ToSharedDtosAsync(decks, cancellationToken);
    }

    public async Task<IReadOnlyList<BrowseDeckDto>> BrowseDecksAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var decks = await db.Decks
            .Where(deck => deck.DeletedAt == null && (deck.Visibility == "public" || deck.OwnerUserId == userId))
            .OrderBy(deck => deck.Name)
            .ToArrayAsync(cancellationToken);
        return await ToBrowseDtosAsync(decks, userId, cancellationToken);
    }

    public async Task<IReadOnlyList<DeckDto>> ListActiveDecksAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var activeDeckIds = await db.UserActiveDecks
            .Where(activeDeck => activeDeck.UserId == userId)
            .Select(activeDeck => activeDeck.DeckId)
            .ToArrayAsync(cancellationToken);
        var decks = await db.Decks
            .Where(deck => deck.DeletedAt == null && activeDeckIds.Contains(deck.Id) && (deck.Visibility == "public" || deck.OwnerUserId == userId))
            .OrderBy(deck => deck.Name)
            .ToArrayAsync(cancellationToken);
        return decks.Select(ToDto).ToArray();
    }

    public async Task<DeckDto?> AddActiveDeckAsync(string userId, string deckId, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var deck = await db.Decks.FindAsync([deckId], cancellationToken);
        if (deck is null || deck.DeletedAt is not null || (deck.Visibility != "public" && deck.OwnerUserId != userId))
        {
            return null;
        }

        if (!await db.UserActiveDecks.AnyAsync(activeDeck => activeDeck.UserId == userId && activeDeck.DeckId == deckId, cancellationToken))
        {
            db.UserActiveDecks.Add(new UserActiveDeckEntity
            {
                UserId = userId,
                DeckId = deckId,
                AddedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        return ToDto(deck);
    }

    public async Task<bool> RemoveActiveDeckAsync(string userId, string deckId, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var activeDeck = await db.UserActiveDecks.FindAsync([userId, deckId], cancellationToken);
        if (activeDeck is null)
        {
            return false;
        }

        db.UserActiveDecks.Remove(activeDeck);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<DeckDto?> GetDeckAsync(string deckId, string userId, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var deck = await db.Decks.FindAsync([deckId], cancellationToken);
        return deck is null || deck.DeletedAt is not null || (deck.OwnerUserId != userId && deck.Visibility != "public") ? null : ToDto(deck);
    }

    public async Task<DeckDto> CreateDeckAsync(string userId, CreateDeckRequest request, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        await ValidateDeckAsync(request.LegendId, request.ChampionId, request.BattlefieldDeckIds, request.RuneDeckIds, request.MainDeckIds, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var deck = new DeckEntity
        {
            Id = string.IsNullOrWhiteSpace(request.Name) ? $"deck-{Guid.NewGuid():N}" : $"deck-{Guid.NewGuid():N}",
            OwnerUserId = userId,
            Name = string.IsNullOrWhiteSpace(request.Name) ? "Online deck" : request.Name.Trim(),
            Visibility = request.Visibility == "public" ? "public" : "private",
            Description = request.Description,
            TagsJson = Serialize(request.Tags ?? []),
            LegendId = request.LegendId,
            ChampionId = request.ChampionId,
            BattlefieldDeckIdsJson = Serialize(request.BattlefieldDeckIds),
            RuneDeckIdsJson = Serialize(request.RuneDeckIds),
            MainDeckIdsJson = Serialize(request.MainDeckIds),
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Decks.Add(deck);
        db.UserActiveDecks.Add(new UserActiveDeckEntity
        {
            UserId = userId,
            DeckId = deck.Id,
            AddedAt = now
        });
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(deck);
    }

    public async Task<DeckDto?> UpdateDeckAsync(string deckId, string userId, UpdateDeckRequest request, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var deck = await db.Decks.FindAsync([deckId], cancellationToken);
        if (deck is null || deck.DeletedAt is not null || deck.OwnerUserId != userId)
        {
            return null;
        }

        await ValidateDeckAsync(
            request.LegendId ?? deck.LegendId,
            request.ChampionId ?? deck.ChampionId,
            request.BattlefieldDeckIds ?? Deserialize(deck.BattlefieldDeckIdsJson),
            request.RuneDeckIds ?? Deserialize(deck.RuneDeckIdsJson),
            request.MainDeckIds ?? Deserialize(deck.MainDeckIdsJson),
            cancellationToken);

        deck.Name = request.Name ?? deck.Name;
        deck.Visibility = request.Visibility ?? deck.Visibility;
        deck.Description = request.Description ?? deck.Description;
        deck.TagsJson = request.Tags is null ? deck.TagsJson : Serialize(request.Tags);
        deck.LegendId = request.LegendId ?? deck.LegendId;
        deck.ChampionId = request.ChampionId ?? deck.ChampionId;
        deck.BattlefieldDeckIdsJson = request.BattlefieldDeckIds is null ? deck.BattlefieldDeckIdsJson : Serialize(request.BattlefieldDeckIds);
        deck.RuneDeckIdsJson = request.RuneDeckIds is null ? deck.RuneDeckIdsJson : Serialize(request.RuneDeckIds);
        deck.MainDeckIdsJson = request.MainDeckIds is null ? deck.MainDeckIdsJson : Serialize(request.MainDeckIds);
        deck.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(deck);
    }

    public async Task<bool> DeleteDeckAsync(string deckId, string userId, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var deck = await db.Decks.FindAsync([deckId], cancellationToken);
        if (deck is null || deck.OwnerUserId != userId || deck.DeletedAt is not null)
        {
            return false;
        }

        deck.DeletedAt = DateTimeOffset.UtcNow;
        deck.UpdatedAt = deck.DeletedAt.Value;
        await db.UserActiveDecks
            .Where(activeDeck => activeDeck.DeckId == deckId)
            .ExecuteDeleteAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<AdminDeckDto?> AdminUpdateDeckAsync(string deckId, AdminUpdateDeckRequest request, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var deck = await db.Decks.FindAsync([deckId], cancellationToken);
        if (deck is null || deck.DeletedAt is not null)
        {
            return null;
        }

        if (request.Visibility is null)
        {
            throw new InvalidOperationException("Visibility is required.");
        }

        var visibility = request.Visibility.Trim().ToLowerInvariant();
        if (visibility is not "public" and not "private")
        {
            throw new InvalidOperationException("Visibility must be public or private.");
        }

        if (deck.Visibility != visibility)
        {
            var now = DateTimeOffset.UtcNow;
            IReadOnlyList<string> affectedLobbyIds = Array.Empty<string>();
            IReadOnlyList<MatchmakingTicketEntity> cancelledTickets = Array.Empty<MatchmakingTicketEntity>();
            using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            deck.Visibility = visibility;
            deck.UpdatedAt = now;
            if (visibility == "private")
            {
                var cleanup = await CleanupDeckReferencesAsync(deck.Id, deck.OwnerUserId, now, cancellationToken);
                affectedLobbyIds = cleanup.LobbyIds;
                cancelledTickets = cleanup.CancelledTickets;
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await BroadcastCancelledTicketsAsync(cancelledTickets, cancellationToken);
            await BroadcastUpdatedLobbiesAsync(affectedLobbyIds, "lobby.loadoutUpdated", cancellationToken);
        }

        return (await ToAdminDeckDtosAsync([deck], cancellationToken)).Single();
    }

    public async Task<bool?> AdminDeleteDeckAsync(string deckId, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var deck = await db.Decks.FindAsync([deckId], cancellationToken);
        if (deck is null)
        {
            return null;
        }

        if (deck.DeletedAt is not null)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        CleanupResult cleanup;
        using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            deck.DeletedAt = now;
            deck.UpdatedAt = now;
            cleanup = await CleanupDeckReferencesAsync(deck.Id, null, now, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        await BroadcastCancelledTicketsAsync(cleanup.CancelledTickets, cancellationToken);
        await BroadcastUpdatedLobbiesAsync(cleanup.LobbyIds, "lobby.loadoutUpdated", cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<MatchSummaryDto>> ListMatchesAsync(CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var matches = await db.Matches.OrderByDescending(match => match.CreatedAt).ToArrayAsync(cancellationToken);
        var players = await db.MatchPlayers.ToArrayAsync(cancellationToken);
        var avatarImageHashes = await LoadAvatarImageHashesAsync(players, cancellationToken);
        return matches.Select(match => ToSummary(match, players.Where(player => player.MatchId == match.Id), avatarImageHashes)).ToArray();
    }

    public async Task<MatchSnapshotDto?> GetMatchAsync(string matchId, string userId, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var match = await db.Matches.FindAsync([matchId], cancellationToken);
        if (match is null)
        {
            return null;
        }

        var players = await db.MatchPlayers.Where(player => player.MatchId == matchId).OrderBy(player => player.PlayerId).ToArrayAsync(cancellationToken);
        var viewerPlayerId = players.FirstOrDefault(player => player.UserId == userId)?.PlayerId;
        if (viewerPlayerId is null)
        {
            return null;
        }

        var snapshot = await db.MatchSnapshots.Where(candidate => candidate.MatchId == matchId).OrderByDescending(candidate => candidate.SequenceNumber).FirstOrDefaultAsync(cancellationToken);
        var avatarImageHashes = await LoadAvatarImageHashesAsync(players, cancellationToken);
        return ToSnapshot(match, players, avatarImageHashes, snapshot?.StateJson ?? "{}", snapshot?.SequenceNumber ?? match.SequenceNumber, viewerPlayerId);
    }

    public async Task<MatchSnapshotDto> CreateMatchAsync(string userId, CreateMatchRequest request, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var mode = ModeSpec.For(request.Mode);
        if (request.Players.Count != mode.PlayerCount)
        {
            throw new InvalidOperationException($"{mode.Label} requires exactly {mode.PlayerCount} players.");
        }

        var decks = new List<DeckEntity>();
        foreach (var player in request.Players)
        {
            var deck = await db.Decks.FindAsync([player.DeckId], cancellationToken)
                ?? throw new InvalidOperationException($"Deck '{player.DeckId}' was not found.");
            if (deck.OwnerUserId != player.UserId || (player.UserId == userId && deck.OwnerUserId != userId))
            {
                throw new InvalidOperationException($"Deck '{player.DeckId}' is not owned by its seated player.");
            }
            decks.Add(deck);
        }

        return await CreateMatchFromDecksAsync(request.Mode, request.Players, decks, request.BattlefieldIds ?? [], request.FirstPlayerId ?? 0, userId, cancellationToken);
    }

    public async Task<LobbyDto> CreateLobbyAsync(string hostUserId, CreateLobbyRequest request, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var host = await db.Users.FindAsync([hostUserId], cancellationToken)
            ?? throw new InvalidOperationException("Host user was not found.");
        var allowedModes = NormalizeAllowedModes(request.AllowedModes);
        var selectedMode = NormalizeMode(request.SelectedMode);
        if (!allowedModes.Contains(selectedMode, StringComparer.OrdinalIgnoreCase))
        {
            selectedMode = allowedModes[0];
        }

        var mode = ModeSpec.For(selectedMode);
        var now = DateTimeOffset.UtcNow;
        var lobby = new LobbyEntity
        {
            Id = $"lobby-{Guid.NewGuid():N}",
            HostUserId = hostUserId,
            Name = string.IsNullOrWhiteSpace(request.Name) ? $"{host.DisplayName}'s lobby" : request.Name.Trim(),
            Status = "open",
            AllowedModesJson = Serialize(allowedModes),
            SelectedMode = selectedMode,
            MaxPlayers = mode.PlayerCount,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Lobbies.Add(lobby);
        db.LobbyPlayers.Add(new LobbyPlayerEntity
        {
            LobbyId = lobby.Id,
            UserId = hostUserId,
            SeatIndex = 0,
            DisplayName = host.DisplayName,
            TeamId = TeamForSeat(selectedMode, 0),
            JoinedAt = now,
            UpdatedAt = now
        });

        if (request.IncludeReadyDummy == true)
        {
            if (!host.IsAdmin)
            {
                throw new InvalidOperationException("Only admins can add a ready dummy player.");
            }

            var dummyUser = CreateDummyUser(now);
            var dummyDeck = await CreateRandomLegalDummyDeckAsync(dummyUser.Id, mode, now, cancellationToken);
            var dummyBattlefieldId = Deserialize(dummyDeck.BattlefieldDeckIdsJson)[0];
            db.Users.Add(dummyUser);
            db.Decks.Add(dummyDeck);
            db.UserActiveDecks.Add(new UserActiveDeckEntity
            {
                UserId = dummyUser.Id,
                DeckId = dummyDeck.Id,
                AddedAt = now
            });
            db.LobbyPlayers.Add(new LobbyPlayerEntity
            {
                LobbyId = lobby.Id,
                UserId = dummyUser.Id,
                SeatIndex = 1,
                DisplayName = dummyUser.DisplayName,
                DeckId = dummyDeck.Id,
                SelectedBattlefieldIdsJson = Serialize(new[] { dummyBattlefieldId }),
                TeamId = TeamForSeat(selectedMode, 1),
                IsReady = true,
                JoinedAt = now,
                UpdatedAt = now
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        return await GetLobbyRequiredAsync(lobby.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<LobbyDto>> ListOpenLobbiesAsync(CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var lobbies = await db.Lobbies
            .Where(lobby => lobby.Status == "open")
            .OrderByDescending(lobby => lobby.UpdatedAt)
            .ToArrayAsync(cancellationToken);
        var result = new List<LobbyDto>();
        foreach (var lobby in lobbies)
        {
            result.Add(await ToLobbyDtoAsync(lobby, cancellationToken, includePrivateLoadouts: false));
        }

        return result;
    }

    private async Task<DeckEntity> CreateRandomLegalDummyDeckAsync(string ownerUserId, ModeSpec mode, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var cards = await db.Cards.ToArrayAsync(cancellationToken);
        var legends = cards.Where(card => EffectiveKind(card) == "legend").ToArray();
        var champions = cards.Where(card => EffectiveKind(card) == "champion").ToArray();
        var battlefields = cards.Where(card => EffectiveKind(card) == "battlefield").ToArray();
        var runes = cards.Where(card => EffectiveKind(card) == "rune").ToArray();
        var mainDeckCards = cards.Where(card => EffectiveKind(card) is not "legend" and not "battlefield" and not "rune" and not "token").ToArray();
        var definitions = cards.ToDictionary(card => card.Id, ToCardDefinition, StringComparer.OrdinalIgnoreCase);

        if (legends.Length == 0 || champions.Length == 0 || battlefields.Length == 0 || runes.Length == 0 || mainDeckCards.Length == 0)
        {
            throw new InvalidOperationException("Card catalog does not have enough legal cards to create a random dummy deck.");
        }

        for (var attempt = 0; attempt < 200; attempt++)
        {
            var legendId = PickRandom(legends).Id;
            var championId = PickRandom(champions).Id;
            var battlefieldIds = PickRandomDistinctByName(battlefields, 3, definitions).Select(card => card.Id).ToArray();
            var runeIds = PickRandomMany(runes, 12).Select(card => card.Id).ToArray();
            var mainDeckIds = PickRandomMainDeckIds(mainDeckCards.Where(card => card.Id != championId).ToArray(), 40, definitions);
            var validation = DeckConstructionRules.Validate(new DeckConstructionRequest(legendId, championId, battlefieldIds, runeIds, mainDeckIds), definitions);
            if (!validation.IsValid)
            {
                continue;
            }

            return new DeckEntity
            {
                Id = $"deck-dummy-{Guid.NewGuid():N}",
                OwnerUserId = ownerUserId,
                Name = $"Dummy Deck {now:HHmmss}",
                Visibility = "private",
                Description = "Generated automatically for an admin-created dummy lobby seat.",
                TagsJson = Serialize(new[] { "dummy", "generated" }),
                LegendId = legendId,
                ChampionId = championId,
                BattlefieldDeckIdsJson = Serialize(battlefieldIds),
                RuneDeckIdsJson = Serialize(runeIds),
                MainDeckIdsJson = Serialize(mainDeckIds),
                CreatedAt = now,
                UpdatedAt = now
            };
        }

        throw new InvalidOperationException("Card catalog does not have enough legal cards to create a random dummy deck.");
    }

    private static string[] PickRandomMainDeckIds(IReadOnlyList<CardEntity> cards, int count, IReadOnlyDictionary<string, CardDefinition> definitions)
    {
        return cards
            .GroupBy(card => definitions.TryGetValue(card.Id, out var definition) ? definition.Name : card.Name, StringComparer.OrdinalIgnoreCase)
            .OrderBy(_ => Random.Shared.Next())
            .SelectMany(group => Enumerable.Range(0, 3).Select(_ => PickRandom(group.ToArray()).Id))
            .Take(count)
            .ToArray();
    }

    private static IReadOnlyList<CardEntity> PickRandomDistinctByName(IReadOnlyList<CardEntity> cards, int count, IReadOnlyDictionary<string, CardDefinition> definitions)
    {
        return cards
            .GroupBy(card => definitions.TryGetValue(card.Id, out var definition) ? definition.Name : card.Name, StringComparer.OrdinalIgnoreCase)
            .OrderBy(_ => Random.Shared.Next())
            .Take(count)
            .Select(group => PickRandom(group.ToArray()))
            .ToArray();
    }

    private static UserEntity CreateDummyUser(DateTimeOffset now)
    {
        var id = $"user-dummy-{Guid.NewGuid():N}";
        return new UserEntity
        {
            Id = id,
            Email = $"{id}@riftbound.local",
            NormalizedEmail = $"{id.ToUpperInvariant()}@RIFTBOUND.LOCAL",
            DisplayName = $"Dummy {now:HHmmss}",
            PasswordHash = string.Empty,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static CardEntity PickRandom(IReadOnlyList<CardEntity> cards)
    {
        return cards[Random.Shared.Next(cards.Count)];
    }

    private static IReadOnlyList<CardEntity> PickRandomMany(IReadOnlyList<CardEntity> cards, int count)
    {
        return Enumerable.Range(0, count).Select(_ => PickRandom(cards)).ToArray();
    }

    private static IReadOnlyList<CardEntity> PickRandomDistinct(IReadOnlyList<CardEntity> cards, int count)
    {
        return cards.OrderBy(_ => Random.Shared.Next()).Take(Math.Min(count, cards.Count)).ToArray();
    }

    public async Task<LobbyDto?> GetLobbyAsync(string lobbyId, string userId, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var lobby = await db.Lobbies.FindAsync([lobbyId], cancellationToken);
        if (lobby is null)
        {
            return null;
        }

        var isMember = await db.LobbyPlayers.AnyAsync(player => player.LobbyId == lobbyId && player.UserId == userId, cancellationToken);
        return await ToLobbyDtoAsync(lobby, cancellationToken, includePrivateLoadouts: isMember);
    }

    public async Task<LobbyDto?> JoinLobbyAsync(string lobbyId, string userId, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var lobby = await db.Lobbies.FindAsync([lobbyId], cancellationToken);
        if (lobby is null)
        {
            return null;
        }

        if (lobby.Status != "open")
        {
            throw new InvalidOperationException("Lobby is not open.");
        }

        var user = await db.Users.FindAsync([userId], cancellationToken)
            ?? throw new InvalidOperationException("User was not found.");
        var players = await db.LobbyPlayers.Where(player => player.LobbyId == lobbyId).OrderBy(player => player.SeatIndex).ToArrayAsync(cancellationToken);
        var existing = players.FirstOrDefault(player => player.UserId == userId);
        if (existing is not null)
        {
            return await ToLobbyDtoAsync(lobby, cancellationToken);
        }

        if (players.Length >= ModeSpec.For(lobby.SelectedMode).PlayerCount)
        {
            throw new InvalidOperationException("Lobby is full.");
        }

        var seat = Enumerable.Range(0, ModeSpec.For(lobby.SelectedMode).PlayerCount).First(index => players.All(player => player.SeatIndex != index));
        var now = DateTimeOffset.UtcNow;
        db.LobbyPlayers.Add(new LobbyPlayerEntity
        {
            LobbyId = lobbyId,
            UserId = userId,
            SeatIndex = seat,
            DisplayName = user.DisplayName,
            TeamId = TeamForSeat(lobby.SelectedMode, seat),
            JoinedAt = now,
            UpdatedAt = now
        });
        lobby.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        var dto = await ToLobbyDtoAsync(lobby, cancellationToken);
        await BroadcastLobbyAsync(dto, "lobby.playerJoined", cancellationToken);
        return dto;
    }

    public async Task<LobbyDto?> LeaveLobbyAsync(string lobbyId, string userId, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var lobby = await db.Lobbies.FindAsync([lobbyId], cancellationToken);
        if (lobby is null)
        {
            return null;
        }

        var player = await db.LobbyPlayers.FindAsync([lobbyId, userId], cancellationToken);
        if (player is null)
        {
            return await ToLobbyDtoAsync(lobby, cancellationToken);
        }

        if (lobby.HostUserId == userId)
        {
            lobby.Status = "cancelled";
            lobby.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            var cancelled = await ToLobbyDtoAsync(lobby, cancellationToken);
            await BroadcastLobbyAsync(cancelled, "lobby.cancelled", cancellationToken);
            return cancelled;
        }

        db.LobbyPlayers.Remove(player);
        lobby.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        var dto = await ToLobbyDtoAsync(lobby, cancellationToken);
        await BroadcastLobbyAsync(dto, "lobby.playerLeft", cancellationToken);
        return dto;
    }

    public async Task<LobbyDto?> UpdateLobbySettingsAsync(string lobbyId, string userId, UpdateLobbySettingsRequest request, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var lobby = await db.Lobbies.FindAsync([lobbyId], cancellationToken);
        if (lobby is null)
        {
            return null;
        }

        if (lobby.HostUserId != userId)
        {
            throw new InvalidOperationException("Only the lobby host can update settings.");
        }

        if (lobby.Status != "open")
        {
            throw new InvalidOperationException("Lobby settings can only be changed while open.");
        }

        if (await db.LobbyPlayers.AnyAsync(player => player.LobbyId == lobbyId && player.IsReady, cancellationToken))
        {
            throw new InvalidOperationException("Lobby settings cannot be changed while players are ready.");
        }

        var allowedModes = request.AllowedModes is null ? Deserialize(lobby.AllowedModesJson) : NormalizeAllowedModes(request.AllowedModes);
        var selectedMode = request.SelectedMode is null ? lobby.SelectedMode : NormalizeMode(request.SelectedMode);
        if (!allowedModes.Contains(selectedMode, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Selected mode must be allowed by this lobby.");
        }

        var mode = ModeSpec.For(selectedMode);
        var playerCount = await db.LobbyPlayers.CountAsync(player => player.LobbyId == lobbyId, cancellationToken);
        if (playerCount > mode.PlayerCount)
        {
            throw new InvalidOperationException("Selected mode has fewer seats than the current lobby player count.");
        }

        lobby.Name = string.IsNullOrWhiteSpace(request.Name) ? lobby.Name : request.Name.Trim();
        lobby.AllowedModesJson = Serialize(allowedModes);
        lobby.SelectedMode = selectedMode;
        lobby.MaxPlayers = mode.PlayerCount;
        lobby.UpdatedAt = DateTimeOffset.UtcNow;

        var players = await db.LobbyPlayers.Where(player => player.LobbyId == lobbyId).ToArrayAsync(cancellationToken);
        foreach (var player in players)
        {
            player.TeamId = TeamForSeat(selectedMode, player.SeatIndex);
            player.UpdatedAt = lobby.UpdatedAt;
        }

        await db.SaveChangesAsync(cancellationToken);
        var dto = await ToLobbyDtoAsync(lobby, cancellationToken);
        await BroadcastLobbyAsync(dto, "lobby.updated", cancellationToken);
        return dto;
    }

    public async Task<LobbyDto?> UpdateLobbyLoadoutAsync(string lobbyId, string userId, UpdateLobbyLoadoutRequest request, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var lobby = await db.Lobbies.FindAsync([lobbyId], cancellationToken);
        if (lobby is null)
        {
            return null;
        }

        var player = await db.LobbyPlayers.FindAsync([lobbyId, userId], cancellationToken);
        if (player is null)
        {
            throw new InvalidOperationException("User is not in this lobby.");
        }

        var deck = await GetActiveDeckForUserAsync(userId, request.DeckId, cancellationToken);

        var selectedBattlefields = request.SelectedBattlefieldIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (selectedBattlefields.Length != 1)
        {
            throw new InvalidOperationException("Select exactly one battlefield from the selected deck.");
        }

        var deckBattlefields = Deserialize(deck.BattlefieldDeckIdsJson);
        if (selectedBattlefields.Any(id => !deckBattlefields.Contains(id, StringComparer.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Selected battlefield must be in the selected deck.");
        }

        var otherPlayers = Array.Empty<LobbyPlayerEntity>();
        if (player.TeamId is not null)
        {
            otherPlayers = await db.LobbyPlayers
                .Where(candidate =>
                    candidate.LobbyId == lobbyId &&
                    candidate.UserId != userId &&
                    candidate.TeamId == player.TeamId)
                .ToArrayAsync(cancellationToken);
        }
        var now = DateTimeOffset.UtcNow;
        foreach (var otherPlayer in otherPlayers)
        {
            var previousSelectedBattlefields = Deserialize(otherPlayer.SelectedBattlefieldIdsJson);
            var otherSelectedBattlefields = previousSelectedBattlefields
                .Where(id => !selectedBattlefields.Contains(id, StringComparer.OrdinalIgnoreCase))
                .ToArray();
            if (otherSelectedBattlefields.Length == previousSelectedBattlefields.Length)
            {
                continue;
            }

            otherPlayer.SelectedBattlefieldIdsJson = Serialize(otherSelectedBattlefields);
            otherPlayer.IsReady = false;
            otherPlayer.UpdatedAt = now;
        }

        player.DeckId = deck.Id;
        player.SelectedBattlefieldIdsJson = Serialize(selectedBattlefields);
        player.IsReady = false;
        player.UpdatedAt = now;
        lobby.UpdatedAt = player.UpdatedAt;
        await db.SaveChangesAsync(cancellationToken);
        var dto = await ToLobbyDtoAsync(lobby, cancellationToken);
        await BroadcastLobbyAsync(dto, "lobby.loadoutUpdated", cancellationToken);
        return dto;
    }

    public async Task<LobbyDto?> SetLobbyReadyAsync(string lobbyId, string userId, bool isReady, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var lobby = await db.Lobbies.FindAsync([lobbyId], cancellationToken);
        if (lobby is null)
        {
            return null;
        }

        var player = await db.LobbyPlayers.FindAsync([lobbyId, userId], cancellationToken);
        if (player is null)
        {
            throw new InvalidOperationException("User is not in this lobby.");
        }

        if (isReady)
        {
            await ValidateLobbyPlayerLoadoutAsync(player, cancellationToken);
        }

        player.IsReady = isReady;
        player.UpdatedAt = DateTimeOffset.UtcNow;
        lobby.UpdatedAt = player.UpdatedAt;
        await db.SaveChangesAsync(cancellationToken);
        var dto = await ToLobbyDtoAsync(lobby, cancellationToken);
        await BroadcastLobbyAsync(dto, "lobby.readyChanged", cancellationToken);
        return dto;
    }

    public async Task<LobbyDto?> StartLobbyAsync(string lobbyId, string userId, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var lobby = await db.Lobbies.FindAsync([lobbyId], cancellationToken);
        if (lobby is null)
        {
            return null;
        }

        if (lobby.HostUserId != userId)
        {
            throw new InvalidOperationException("Only the lobby host can start the lobby.");
        }

        if (lobby.Status != "open")
        {
            throw new InvalidOperationException("Lobby is not open.");
        }

        var players = await db.LobbyPlayers.Where(player => player.LobbyId == lobbyId).OrderBy(player => player.SeatIndex).ToArrayAsync(cancellationToken);
        var mode = ModeSpec.For(lobby.SelectedMode);
        if (players.Length != mode.PlayerCount || players.Any(player => !player.IsReady))
        {
            throw new InvalidOperationException("Lobby requires all seats filled and ready before starting.");
        }

        foreach (var player in players)
        {
            await ValidateLobbyPlayerLoadoutAsync(player, cancellationToken);
        }

        var decks = new List<DeckEntity>();
        foreach (var player in players)
        {
            decks.Add(await db.Decks.FindAsync([player.DeckId!], cancellationToken)
                ?? throw new InvalidOperationException($"Deck '{player.DeckId}' was not found."));
        }

        var battlefieldIds = SelectLobbyBattlefields(lobby.SelectedMode, players);
        if (battlefieldIds.Length != mode.BattlefieldCount)
        {
            throw new InvalidOperationException($"{mode.Label} requires {mode.BattlefieldCount} selected battlefields.");
        }

        lobby.Status = "starting";
        lobby.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        var matchPlayers = players.Select(player => new CreateMatchPlayerRequest(player.UserId, player.DeckId!, player.TeamId)).ToArray();
        var match = await CreateMatchFromDecksAsync(lobby.SelectedMode, matchPlayers, decks, battlefieldIds, 0, lobby.HostUserId, cancellationToken);
        lobby.Status = "matched";
        lobby.MatchId = match.Id;
        lobby.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        var dto = await ToLobbyDtoAsync(lobby, cancellationToken);
        await BroadcastLobbyAsync(dto, "lobby.matched", cancellationToken);
        return dto;
    }

    public async Task<bool> IsUserInLobbyAsync(string lobbyId, string userId, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        return await db.LobbyPlayers.AnyAsync(player => player.LobbyId == lobbyId && player.UserId == userId, cancellationToken);
    }

    public async Task<IReadOnlyList<LegalActionDto>?> GetLegalActionsAsync(string matchId, int playerId, CancellationToken cancellationToken)
    {
        var engineState = await LoadEngineStateAsync(matchId, cancellationToken);
        return engineState is null ? null : rulesEngine.GetLegalActions(engineState, playerId).Select(ToDto).ToArray();
    }

    public async Task<IReadOnlyList<MatchEventDto>?> GetMatchEventsAsync(string matchId, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        if (!await db.Matches.AnyAsync(match => match.Id == matchId, cancellationToken))
        {
            return null;
        }

        return (await db.MatchEvents.Where(matchEvent => matchEvent.MatchId == matchId).OrderBy(matchEvent => matchEvent.SequenceNumber).ToArrayAsync(cancellationToken))
            .Select(ToDto)
            .ToArray();
    }

    public async Task<SubmitActionResponseDto?> SubmitActionAsync(string matchId, SubmitMatchActionRequest request, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var match = await db.Matches.FindAsync([matchId], cancellationToken);
        if (match is null)
        {
            return null;
        }

        var engineState = await LoadEngineStateAsync(matchId, cancellationToken) ?? throw new InvalidOperationException("Match snapshot is missing.");
        var result = rulesEngine.ApplyAction(engineState, new EngineGameAction(request.PlayerId, request.Type, request.Payload), request.ExpectedSequenceNumber);
        var lastEventSequence = await db.MatchEvents
            .Where(matchEvent => matchEvent.MatchId == matchId)
            .MaxAsync(matchEvent => (int?)matchEvent.SequenceNumber, cancellationToken) ?? match.SequenceNumber;
        var nextSequence = result.Accepted ? Math.Max(result.State.SequenceNumber, lastEventSequence + 1) : lastEventSequence + 1;
        var now = DateTimeOffset.UtcNow;
        var actionPayload = Serialize(request.Payload ?? new Dictionary<string, object?>());
        var resultPayload = BuildResultPayload(result);

        var entity = new MatchEventEntity
        {
            Id = $"event-{Guid.NewGuid():N}",
            MatchId = matchId,
            SequenceNumber = nextSequence,
            PlayerId = request.PlayerId,
            ActionType = result.Accepted ? request.Type : "action.rejected",
            ActionPayloadJson = actionPayload,
            ResultPayloadJson = resultPayload,
            StateAfterJson = result.Accepted ? result.State.State.ToJsonString(JsonOptions) : null,
            CreatedAt = now
        };

        db.MatchEvents.Add(entity);
        if (result.Accepted)
        {
            match.SequenceNumber = result.State.SequenceNumber;
            match.Status = result.State.Stage == "game-over" ? "completed" : result.State.Stage;
            match.UpdatedAt = now;
            match.CompletedAt = result.State.Stage == "game-over" ? now : match.CompletedAt;
            match.WinnerPlayerId = result.State.State["winner"]?.GetValue<int?>();
            match.WinningTeamId = result.State.State["winningTeamId"]?.GetValue<int?>();
            if (match.CompletedAt == now)
            {
                await UpdateCompletedMatchStatsAsync(matchId, match.WinnerPlayerId, cancellationToken);
            }
            db.MatchSnapshots.Add(new MatchSnapshotEntity
            {
                Id = $"snapshot-{Guid.NewGuid():N}",
                MatchId = matchId,
                SequenceNumber = match.SequenceNumber,
                StateJson = result.State.State.ToJsonString(JsonOptions),
                CreatedAt = now
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        return new SubmitActionResponseDto(result.Accepted, ToDto(entity), PlayerViewRedactor.Redact(result.State.State, request.PlayerId), match.SequenceNumber, result.LegalActions.Select(ToDto).ToArray());
    }

    private async Task UpdateCompletedMatchStatsAsync(string matchId, int? winnerPlayerId, CancellationToken cancellationToken)
    {
        var players = await db.MatchPlayers.Where(player => player.MatchId == matchId).ToArrayAsync(cancellationToken);
        foreach (var player in players)
        {
            var user = await db.Users.FindAsync([player.UserId], cancellationToken);
            if (user is null)
            {
                continue;
            }

            user.GamesPlayed += 1;
            if (winnerPlayerId is not null && player.PlayerId == winnerPlayerId)
            {
                user.Wins += 1;
            }
            else if (winnerPlayerId is not null)
            {
                user.Losses += 1;
            }
            user.LastPlayedAt = DateTimeOffset.UtcNow;
            user.UpdatedAt = user.LastPlayedAt.Value;
        }
    }

    public async Task<MatchmakingTicketDto> JoinQueueAsync(string userId, JoinMatchmakingRequest request, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        if (request.Mode != "duel-1v1")
        {
            throw new InvalidOperationException("Only duel-1v1 matchmaking is supported.");
        }

        await GetActiveDeckForUserAsync(userId, request.DeckId, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var ticket = await db.MatchmakingTickets.FirstOrDefaultAsync(candidate => candidate.UserId == userId && candidate.Mode == request.Mode && candidate.Status == "queued", cancellationToken);
        if (ticket is null)
        {
            ticket = new MatchmakingTicketEntity
            {
                Id = $"ticket-{Guid.NewGuid():N}",
                UserId = userId,
                DeckId = request.DeckId,
                Mode = request.Mode,
                Status = "queued",
                CreatedAt = now,
                UpdatedAt = now
            };
            db.MatchmakingTickets.Add(ticket);
        }
        else
        {
            ticket.DeckId = request.DeckId;
            ticket.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        await TryPairTicketAsync(ticket, cancellationToken);
        return ToDto(ticket);
    }

    public async Task<IReadOnlyList<QueueEntryDto>> ListQueueAsync(CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        return await db.MatchmakingTickets
            .Where(ticket => ticket.Status == "queued")
            .OrderBy(ticket => ticket.CreatedAt)
            .Select(ticket => new QueueEntryDto(ticket.UserId, ticket.DeckId, ticket.Mode, ticket.CreatedAt))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<MatchmakingTicketDto?> GetTicketAsync(string ticketId, string userId, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var ticket = await db.MatchmakingTickets.FindAsync([ticketId], cancellationToken);
        return ticket is null || ticket.UserId != userId ? null : ToDto(ticket);
    }

    public async Task<MatchmakingTicketDto?> CancelTicketAsync(string ticketId, string userId, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var ticket = await db.MatchmakingTickets.FindAsync([ticketId], cancellationToken);
        if (ticket is null || ticket.UserId != userId)
        {
            return null;
        }

        ticket.Status = "cancelled";
        ticket.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(ticket);
    }

    public async Task LeaveQueueAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        await db.MatchmakingTickets
            .Where(ticket => ticket.UserId == userId && ticket.Status == "queued")
            .ExecuteUpdateAsync(updates => updates.SetProperty(ticket => ticket.Status, "cancelled"), cancellationToken);
    }

    public async Task<IReadOnlyList<MatchSummaryDto>> ListMatchesForUserAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var matchIds = await db.MatchPlayers.Where(player => player.UserId == userId).Select(player => player.MatchId).ToArrayAsync(cancellationToken);
        var matches = await db.Matches.Where(match => matchIds.Contains(match.Id)).OrderByDescending(match => match.CreatedAt).ToArrayAsync(cancellationToken);
        var players = await db.MatchPlayers.Where(player => matchIds.Contains(player.MatchId)).ToArrayAsync(cancellationToken);
        var avatarImageHashes = await LoadAvatarImageHashesAsync(players, cancellationToken);
        return matches.Select(match => ToSummary(match, players.Where(player => player.MatchId == match.Id), avatarImageHashes)).ToArray();
    }

    public async Task<bool> IsUserSeatedAsync(string matchId, string userId, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        return await db.MatchPlayers.AnyAsync(player => player.MatchId == matchId && player.UserId == userId, cancellationToken);
    }

    public async Task<bool> UserOwnsPlayerSeatAsync(string matchId, string userId, int playerId, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        return await db.MatchPlayers.AnyAsync(player => player.MatchId == matchId && player.UserId == userId && player.PlayerId == playerId, cancellationToken);
    }

    public async Task<IReadOnlyList<MatchPlayerDto>> ListMatchPlayersAsync(string matchId, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var players = await db.MatchPlayers.Where(player => player.MatchId == matchId).OrderBy(player => player.PlayerId).ToArrayAsync(cancellationToken);
        var avatarImageHashes = await LoadAvatarImageHashesAsync(players, cancellationToken);
        return players.Select(player => ToDto(player, avatarImageHashes)).ToArray();
    }

    private async Task TryPairTicketAsync(MatchmakingTicketEntity ticket, CancellationToken cancellationToken)
    {
        var opponent = await db.MatchmakingTickets
            .Where(candidate => candidate.Id != ticket.Id && candidate.Mode == ticket.Mode && candidate.Status == "queued" && candidate.UserId != ticket.UserId)
            .OrderBy(candidate => candidate.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (opponent is null)
        {
            return;
        }

        var firstDeck = await db.Decks.FindAsync([ticket.DeckId], cancellationToken);
        var secondDeck = await db.Decks.FindAsync([opponent.DeckId], cancellationToken);
        if (firstDeck is null || secondDeck is null)
        {
            return;
        }

        var players = new[]
        {
            new CreateMatchPlayerRequest(ticket.UserId, ticket.DeckId, 0),
            new CreateMatchPlayerRequest(opponent.UserId, opponent.DeckId, 1)
        };
        var match = await CreateMatchFromDecksAsync(ticket.Mode, players, [firstDeck, secondDeck], [], 0, ticket.UserId, cancellationToken);
        ticket.Status = "matched";
        ticket.MatchId = match.Id;
        ticket.UpdatedAt = DateTimeOffset.UtcNow;
        opponent.Status = "matched";
        opponent.MatchId = match.Id;
        opponent.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await hubContext.Clients.Group(MatchHub.TicketGroupName(ticket.Id)).SendAsync("matchmaking.ticketUpdated", ToDto(ticket), cancellationToken);
        await hubContext.Clients.Group(MatchHub.TicketGroupName(opponent.Id)).SendAsync("matchmaking.ticketUpdated", ToDto(opponent), cancellationToken);
    }

    private async Task<MatchSnapshotDto> CreateMatchFromDecksAsync(string mode, IReadOnlyList<CreateMatchPlayerRequest> players, IReadOnlyList<DeckEntity> decks, IReadOnlyList<string> battlefieldIds, int firstPlayerId, string viewerUserId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var matchId = $"match-{Guid.NewGuid():N}";
        var users = await db.Users.ToDictionaryAsync(user => user.Id, cancellationToken);
        var seats = players.Select((player, index) => new EngineSeatConfig(index, player.UserId, users.TryGetValue(player.UserId, out var user) ? user.DisplayName : player.UserId, player.TeamId)).ToArray();
        var engineDecks = decks.Select(deck => new EnginePlayerDeck(deck.Id, deck.LegendId, deck.ChampionId, Deserialize(deck.BattlefieldDeckIdsJson), Deserialize(deck.RuneDeckIdsJson), Deserialize(deck.MainDeckIdsJson))).ToArray();
        var selectedBattlefields = battlefieldIds.Count > 0 ? battlefieldIds : engineDecks.SelectMany(deck => deck.BattlefieldDeckIds).Take(ModeSpec.For(mode).BattlefieldCount).ToArray();
        var catalog = await BuildCardCatalogAsync(engineDecks, cancellationToken);
        var engineState = rulesEngine.CreateInitialState(new EngineMatchConfig(matchId, mode, seats, selectedBattlefields, firstPlayerId), engineDecks, StableSeed(matchId), catalog);

        var match = new MatchEntity
        {
            Id = matchId,
            Mode = mode,
            Status = engineState.Stage,
            SequenceNumber = 0,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Matches.Add(match);
        foreach (var seat in seats)
        {
            db.MatchPlayers.Add(new MatchPlayerEntity
            {
                MatchId = matchId,
                PlayerId = seat.PlayerId,
                UserId = seat.UserId,
                DisplayName = seat.DisplayName,
                DeckId = players[seat.PlayerId].DeckId,
                TeamId = seat.TeamId
            });
        }

        db.MatchEvents.Add(new MatchEventEntity
        {
            Id = $"event-{Guid.NewGuid():N}",
            MatchId = matchId,
            SequenceNumber = 0,
            ActionType = "match.created",
            ActionPayloadJson = "{}",
            ResultPayloadJson = "{}",
            StateAfterJson = engineState.State.ToJsonString(JsonOptions),
            CreatedAt = now
        });
        db.MatchSnapshots.Add(new MatchSnapshotEntity
        {
            Id = $"snapshot-{Guid.NewGuid():N}",
            MatchId = matchId,
            SequenceNumber = 0,
            StateJson = engineState.State.ToJsonString(JsonOptions),
            CreatedAt = now
        });
        await db.SaveChangesAsync(cancellationToken);
        return ToSnapshot(
            match,
            db.MatchPlayers.Local.Where(player => player.MatchId == matchId).ToArray(),
            users.ToDictionary(item => item.Key, item => item.Value.AvatarImageHash),
            engineState.State.ToJsonString(JsonOptions),
            0,
            seats.First(seat => seat.UserId == viewerUserId).PlayerId);
    }

    private async Task<EngineMatchState?> LoadEngineStateAsync(string matchId, CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        var match = await db.Matches.FindAsync([matchId], cancellationToken);
        if (match is null)
        {
            return null;
        }

        var players = await db.MatchPlayers.Where(player => player.MatchId == matchId).OrderBy(player => player.PlayerId).ToArrayAsync(cancellationToken);
        var snapshot = await db.MatchSnapshots.Where(candidate => candidate.MatchId == matchId).OrderByDescending(candidate => candidate.SequenceNumber).FirstOrDefaultAsync(cancellationToken);
        if (snapshot is null)
        {
            return null;
        }

        return new EngineMatchState(
            match.Id,
            match.Mode,
            match.Status,
            snapshot.SequenceNumber,
            JsonNode.Parse(snapshot.StateJson)!.AsObject(),
            players.Select(player => new EnginePlayerState(player.PlayerId, player.UserId, 0, false)).ToArray());
    }

    private async Task CleanupPlaceholderCardsAsync(CancellationToken cancellationToken)
    {
        await db.Cards
            .Where(card => PlaceholderCardIds.Contains(card.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }

    private async Task<LobbyDto> GetLobbyRequiredAsync(string lobbyId, CancellationToken cancellationToken)
    {
        var lobby = await db.Lobbies.FindAsync([lobbyId], cancellationToken)
            ?? throw new InvalidOperationException("Lobby was not found.");
        return await ToLobbyDtoAsync(lobby, cancellationToken);
    }

    private async Task<LobbyDto> ToLobbyDtoAsync(LobbyEntity lobby, CancellationToken cancellationToken, bool includePrivateLoadouts = true)
    {
        var players = await db.LobbyPlayers
            .Where(player => player.LobbyId == lobby.Id)
            .OrderBy(player => player.SeatIndex)
            .ToArrayAsync(cancellationToken);
        var mode = ModeSpec.For(lobby.SelectedMode);
        var playerDtos = players.Select(player => ToDto(player, includePrivateLoadouts)).ToArray();
        return new LobbyDto(
            lobby.Id,
            lobby.HostUserId,
            lobby.Name,
            lobby.Status,
            Deserialize(lobby.AllowedModesJson),
            lobby.SelectedMode,
            mode.PlayerCount,
            mode.BattlefieldCount,
            playerDtos,
            CanStartLobby(lobby, players, mode),
            lobby.MatchId,
            lobby.CreatedAt,
            lobby.UpdatedAt);
    }

    private static LobbyPlayerDto ToDto(LobbyPlayerEntity player, bool includePrivateLoadout)
    {
        return new LobbyPlayerDto(
            player.UserId,
            player.SeatIndex,
            player.DisplayName,
            includePrivateLoadout ? player.DeckId : null,
            includePrivateLoadout ? Deserialize(player.SelectedBattlefieldIdsJson) : [],
            player.TeamId,
            player.IsReady);
    }

    private static bool CanStartLobby(LobbyEntity lobby, IReadOnlyList<LobbyPlayerEntity> players, ModeSpec mode)
    {
        return lobby.Status == "open"
            && Deserialize(lobby.AllowedModesJson).Contains(lobby.SelectedMode, StringComparer.OrdinalIgnoreCase)
            && players.Count == mode.PlayerCount
            && players.All(player => player.IsReady && !string.IsNullOrWhiteSpace(player.DeckId))
            && SelectLobbyBattlefields(lobby.SelectedMode, players).Length == mode.BattlefieldCount;
    }

    private async Task ValidateLobbyPlayerLoadoutAsync(LobbyPlayerEntity player, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(player.DeckId))
        {
            throw new InvalidOperationException("Select a deck before readying.");
        }

        var deck = await GetActiveDeckForUserAsync(player.UserId, player.DeckId, cancellationToken);

        var selected = Deserialize(player.SelectedBattlefieldIdsJson);
        if (selected.Length != 1)
        {
            throw new InvalidOperationException("Select exactly one battlefield before readying.");
        }

        var deckBattlefields = Deserialize(deck.BattlefieldDeckIdsJson);
        if (selected.Any(id => !deckBattlefields.Contains(id, StringComparer.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Selected battlefield must be in the selected deck.");
        }
    }

    private static string[] SelectLobbyBattlefields(string mode, IReadOnlyList<LobbyPlayerEntity> players)
    {
        var modeSpec = ModeSpec.For(mode);
        var ordered = players.OrderBy(player => player.SeatIndex).ToArray();
        if (mode is "ffa-4" or "teams-2v2")
        {
            ordered = ordered.Where(player => player.SeatIndex != 0).ToArray();
        }

        return ordered
            .SelectMany(player => Deserialize(player.SelectedBattlefieldIdsJson))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(modeSpec.BattlefieldCount)
            .ToArray();
    }

    private async Task BroadcastLobbyAsync(LobbyDto lobby, string eventName, CancellationToken cancellationToken)
    {
        await hubContext.Clients.Group(MatchHub.LobbyGroupName(lobby.Id)).SendAsync(eventName, lobby, cancellationToken);
        await hubContext.Clients.Group(MatchHub.LobbyGroupName(lobby.Id)).SendAsync("lobby.updated", lobby, cancellationToken);
    }

    private static string[] NormalizeAllowedModes(IReadOnlyList<string>? modes)
    {
        var normalized = (modes is { Count: > 0 } ? modes : ["duel-1v1"])
            .Select(NormalizeMode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return normalized.Length == 0 ? ["duel-1v1"] : normalized;
    }

    private static string NormalizeMode(string? mode)
    {
        return (mode ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "duel-1v1" => "duel-1v1",
            "ffa-3" => "ffa-3",
            "ffa-4" => "ffa-4",
            "teams-2v2" => "teams-2v2",
            _ => "duel-1v1"
        };
    }

    private static int? TeamForSeat(string mode, int seatIndex)
    {
        return mode == "teams-2v2" ? seatIndex % 2 : null;
    }

    private async Task<int> CountEnabledAdminsAsync(CancellationToken cancellationToken)
    {
        return await db.Users.CountAsync(user => user.IsAdmin && !user.IsDisabled, cancellationToken);
    }

    private async Task<IReadOnlyDictionary<string, CardDefinition>> BuildCardCatalogAsync(IReadOnlyList<EnginePlayerDeck> engineDecks, CancellationToken cancellationToken)
    {
        var cardIds = engineDecks
            .SelectMany(deck => deck.MainDeckIds.Concat(deck.RuneDeckIds).Append(deck.ChampionId).Append(deck.LegendId))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct()
            .ToArray();

        var entities = await db.Cards.Where(card => cardIds.Contains(card.Id)).ToListAsync(cancellationToken);
        return entities.ToDictionary(entity => entity.Id, ToCardDefinition);
    }

    private static CardDefinition ToCardDefinition(CardEntity card)
    {
        var kind = Enum.TryParse<CardKind>(EffectiveKind(card), true, out var parsedKind) ? parsedKind : CardKind.Unit;
        var domain = Enum.TryParse<Domain>(card.Domain, true, out var parsedDomain) ? parsedDomain : Domain.Fury;
        var domains = Deserialize(card.DomainsJson)
            .Select(value => Enum.TryParse<Domain>(value, true, out var parsed) ? parsed : (Domain?)null)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();
        var effectType = Enum.TryParse<CardEffectType>(card.EffectType, true, out var parsedEffect) ? parsedEffect : CardEffectType.Rally;
        return new CardDefinition(
            card.Id,
            card.Name,
            kind,
            Deserialize(card.TagsJson),
            domain,
            domains.Length > 0 ? domains : [domain],
            card.Cost,
            card.Might,
            card.Text,
            card.Image,
            card.CardType,
            card.Supertype,
            new CardEffectDefinition(effectType, card.EffectAmount));
    }

    private static CardDto ToDto(CardEntity card)
    {
        var tags = Deserialize(card.TagsJson);
        return new CardDto(card.Id, card.Name, EffectiveKind(card), tags.Length > 0 ? tags : InferTagsFromName(card.Name), card.Domain, Deserialize(card.DomainsJson), card.Cost, card.Might, card.Text, card.Image, card.CardType, card.Supertype, new CardEffectDto(card.EffectType, card.EffectAmount));
    }

    private async Task<CardUpsertResultDto> UpsertCardCoreAsync(CardDto card, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var entity = await db.Cards.FindAsync([card.Id], cancellationToken);
        var created = entity is null;
        if (entity is null)
        {
            entity = new CardEntity
            {
                Id = card.Id,
                CreatedAt = now
            };
            db.Cards.Add(entity);
        }

        entity.Name = card.Name;
        entity.Kind = card.Kind;
        entity.TagsJson = Serialize(card.Tags);
        entity.Domain = card.Domain;
        entity.DomainsJson = Serialize(card.Domains);
        entity.Cost = card.Cost;
        entity.Might = card.Might;
        entity.Text = card.Text;
        entity.Image = card.Image;
        entity.CardType = card.CardType;
        entity.Supertype = card.Supertype;
        entity.EffectType = card.Effect.Type;
        entity.EffectAmount = card.Effect.Amount;
        entity.UpdatedAt = now;
        return new CardUpsertResultDto(ToDto(entity), created);
    }

    private static CardDto NormalizeCard(CardDto card)
    {
        var kind = NormalizeKind(card.Kind, card.Supertype, card.CardType);
        var domain = NormalizeDomain(card.Domain);
        var domains = card.Domains.Count > 0 ? card.Domains.Select(NormalizeDomain).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() : [domain];
        var tags = card.Tags.Where(tag => !string.IsNullOrWhiteSpace(tag)).Select(tag => tag.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return card with
        {
            Id = card.Id.Trim(),
            Name = string.IsNullOrWhiteSpace(card.Name) ? card.Id.Trim() : card.Name.Trim(),
            Kind = kind,
            Tags = tags.Length > 0 ? tags : InferTagsFromName(card.Name),
            Domain = domain,
            Domains = domains,
            Cost = Math.Max(0, card.Cost),
            Might = Math.Max(0, card.Might),
            Text = card.Text.Trim(),
            Image = string.IsNullOrWhiteSpace(card.Image) ? "*" : card.Image.Trim(),
            CardType = string.IsNullOrWhiteSpace(card.CardType) ? kind : card.CardType.Trim(),
            Effect = card.Effect with { Type = string.IsNullOrWhiteSpace(card.Effect.Type) ? "rally" : card.Effect.Type.Trim(), Amount = Math.Max(0, card.Effect.Amount) }
        };
    }

    private static CardDto? MapRiftCodexCard(RiftCodexCard item)
    {
        var id = !string.IsNullOrWhiteSpace(item.RiftboundId) ? item.RiftboundId : item.Id;
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(item.Name))
        {
            return null;
        }

        var kind = NormalizeKind(item.Classification?.Type, item.Classification?.Supertype);
        var domains = item.Classification?.Domain?.Select(NormalizeDomain).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
        var domain = domains.FirstOrDefault() ?? "Fury";
        var richText = string.IsNullOrWhiteSpace(item.Text?.Rich) ? string.Empty : Regex.Replace(item.Text.Rich, "<.*?>", string.Empty);
        var plainText = !string.IsNullOrWhiteSpace(item.Text?.Plain) ? item.Text.Plain : richText;
        var tags = item.Tags?.Where(tag => !string.IsNullOrWhiteSpace(tag)).Select(tag => tag.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
        return new CardDto(
            id.Trim(),
            item.Name.Trim(),
            kind,
            tags.Length > 0 ? tags : InferTagsFromName(item.Name),
            domain,
            domains.Length > 0 ? domains : [domain],
            Math.Max(0, item.Attributes?.Energy ?? 0),
            Math.Max(0, item.Attributes?.Might ?? item.Attributes?.Power ?? 0),
            plainText ?? string.Empty,
            string.IsNullOrWhiteSpace(item.Media?.ImageUrl) ? "*" : item.Media.ImageUrl,
            string.IsNullOrWhiteSpace(item.Classification?.Type) ? kind : item.Classification.Type,
            SupertypeWithSignature(item.Classification?.Supertype, item.Metadata?.Signature == true),
            new CardEffectDto("rally", 0));
    }

    private static string NormalizeKind(string? kind, string? supertype = null, string? cardType = null)
    {
        var normalizedKind = (kind ?? string.Empty).Trim().ToLowerInvariant();
        var normalizedSupertype = (supertype ?? string.Empty).Trim().ToLowerInvariant();
        var normalizedCardType = (cardType ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedSupertype.Contains("token")) return "token";
        if (normalizedKind.Contains("token") || normalizedCardType.Contains("token")) return "token";
        if (normalizedKind.Contains("rune") || normalizedCardType.Contains("rune")) return "rune";
        if (normalizedKind.Contains("battlefield") || normalizedCardType.Contains("battlefield")) return "battlefield";
        if (normalizedKind.Contains("legend") || normalizedCardType.Contains("legend") || normalizedSupertype.Contains("legend")) return "legend";
        if (normalizedKind.Contains("champion") || normalizedCardType.Contains("champion") || normalizedSupertype.Contains("champion")) return "champion";
        if (normalizedKind.Contains("spell") || normalizedCardType.Contains("spell")) return "spell";
        if (normalizedKind.Contains("gear") || normalizedKind.Contains("equipment") || normalizedKind.Contains("attachment") || normalizedCardType.Contains("gear") || normalizedCardType.Contains("equipment") || normalizedCardType.Contains("attachment")) return "gear";
        return "unit";
    }

    private static string EffectiveKind(CardEntity card)
    {
        return NormalizeKind(card.Kind, card.Supertype, card.CardType);
    }

    private static string? SupertypeWithSignature(string? supertype, bool signature)
    {
        var trimmed = supertype?.Trim();
        if (!signature)
        {
            return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
        }

        if (!string.IsNullOrWhiteSpace(trimmed) && trimmed.Contains("signature", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        return string.IsNullOrWhiteSpace(trimmed) ? "Signature" : $"{trimmed} Signature";
    }

    private static string[] InferTagsFromName(string name)
    {
        var tag = name.Split(" - ", StringSplitOptions.None)[0]?.Trim();
        return string.IsNullOrWhiteSpace(tag) ? [] : [tag];
    }

    private static string NormalizeDomain(string? domain)
    {
        return (domain ?? string.Empty).Trim() switch
        {
            "Calm" => "Calm",
            "Mind" => "Mind",
            "Body" => "Body",
            "Chaos" => "Chaos",
            "Order" => "Order",
            _ => "Fury"
        };
    }

    private static DeckDto ToDto(DeckEntity deck)
    {
        return new DeckDto(deck.Id, deck.Name, deck.OwnerUserId, deck.Visibility, deck.Description, Deserialize(deck.TagsJson), deck.LegendId, deck.ChampionId, Deserialize(deck.BattlefieldDeckIdsJson), Deserialize(deck.RuneDeckIdsJson), Deserialize(deck.MainDeckIdsJson), deck.CreatedAt, deck.UpdatedAt);
    }

    private async Task<IReadOnlyList<AdminDeckDto>> ToAdminDeckDtosAsync(IReadOnlyList<DeckEntity> decks, CancellationToken cancellationToken)
    {
        var users = await db.Users.ToDictionaryAsync(user => user.Id, cancellationToken);
        var cards = await db.Cards.ToDictionaryAsync(card => card.Id, cancellationToken);
        var deckIds = decks.Select(deck => deck.Id).ToArray();
        var activeCounts = await db.UserActiveDecks
            .Where(activeDeck => deckIds.Contains(activeDeck.DeckId))
            .GroupBy(activeDeck => activeDeck.DeckId)
            .Select(group => new { DeckId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(group => group.DeckId, group => group.Count, cancellationToken);
        var queuedCounts = await db.MatchmakingTickets
            .Where(ticket => ticket.Status == "queued" && deckIds.Contains(ticket.DeckId))
            .GroupBy(ticket => ticket.DeckId)
            .Select(group => new { DeckId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(group => group.DeckId, group => group.Count, cancellationToken);
        var lobbyCounts = await db.LobbyPlayers
            .Where(player => player.DeckId != null && deckIds.Contains(player.DeckId!))
            .GroupBy(player => player.DeckId!)
            .Select(group => new { DeckId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(group => group.DeckId, group => group.Count, cancellationToken);

        return decks.Select(deck =>
        {
            var battlefieldIds = Deserialize(deck.BattlefieldDeckIdsJson);
            var runeIds = Deserialize(deck.RuneDeckIdsJson);
            var mainIds = Deserialize(deck.MainDeckIdsJson);
            var allCardIds = new[] { deck.LegendId, deck.ChampionId }.Concat(battlefieldIds).Concat(runeIds).Concat(mainIds);
            var deckCards = allCardIds.Select(id => cards.TryGetValue(id, out var card) ? card : null).Where(card => card is not null).Cast<CardEntity>().ToArray();
            var tags = deckCards.SelectMany(card => Deserialize(card.TagsJson)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(tag => tag).ToArray();
            var domains = deckCards.SelectMany(card => Deserialize(card.DomainsJson).Length > 0 ? Deserialize(card.DomainsJson) : [card.Domain]).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(domain => domain).ToArray();
            var legendName = cards.TryGetValue(deck.LegendId, out var legend) ? legend.Name : string.Empty;
            var championName = cards.TryGetValue(deck.ChampionId, out var champion) ? champion.Name : string.Empty;
            var ownerDisplayName = users.TryGetValue(deck.OwnerUserId, out var owner) ? owner.DisplayName : deck.OwnerUserId;
            return new AdminDeckDto(
                deck.Id,
                deck.Name,
                deck.OwnerUserId,
                ownerDisplayName,
                deck.Visibility,
                deck.LegendId,
                deck.ChampionId,
                legendName,
                championName,
                new DeckCardCountsDto(mainIds.Length, runeIds.Length, battlefieldIds.Length),
                tags,
                domains,
                deck.Description,
                deck.CreatedAt,
                deck.UpdatedAt,
                activeCounts.GetValueOrDefault(deck.Id),
                queuedCounts.GetValueOrDefault(deck.Id),
                lobbyCounts.GetValueOrDefault(deck.Id));
        }).ToArray();
    }

    private async Task<IReadOnlyList<SharedDeckDto>> ToSharedDtosAsync(IReadOnlyList<DeckEntity> decks, CancellationToken cancellationToken)
    {
        var users = await db.Users.ToDictionaryAsync(user => user.Id, cancellationToken);
        var cards = await db.Cards.ToDictionaryAsync(card => card.Id, cancellationToken);
        return decks.Select(deck =>
        {
            var battlefieldIds = Deserialize(deck.BattlefieldDeckIdsJson);
            var runeIds = Deserialize(deck.RuneDeckIdsJson);
            var mainIds = Deserialize(deck.MainDeckIdsJson);
            var allCardIds = new[] { deck.LegendId, deck.ChampionId }.Concat(battlefieldIds).Concat(runeIds).Concat(mainIds);
            var deckCards = allCardIds.Select(id => cards.TryGetValue(id, out var card) ? card : null).Where(card => card is not null).Cast<CardEntity>().ToArray();
            var tags = deckCards.SelectMany(card => Deserialize(card.TagsJson)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(tag => tag).ToArray();
            var domains = deckCards.SelectMany(card => Deserialize(card.DomainsJson).Length > 0 ? Deserialize(card.DomainsJson) : [card.Domain]).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(domain => domain).ToArray();
            var legendName = cards.TryGetValue(deck.LegendId, out var legend) ? legend.Name : string.Empty;
            var championName = cards.TryGetValue(deck.ChampionId, out var champion) ? champion.Name : string.Empty;
            var author = users.TryGetValue(deck.OwnerUserId, out var owner) ? owner.DisplayName : deck.OwnerUserId;
            return new SharedDeckDto(deck.Id, deck.Name, deck.OwnerUserId, deck.Visibility, author, deck.LegendId, deck.ChampionId, battlefieldIds, runeIds, mainIds, tags, domains, legendName, championName, new DeckCardCountsDto(mainIds.Length, runeIds.Length, battlefieldIds.Length), deck.Description, deck.UpdatedAt);
        }).ToArray();
    }

    private async Task<IReadOnlyList<BrowseDeckDto>> ToBrowseDtosAsync(IReadOnlyList<DeckEntity> decks, string userId, CancellationToken cancellationToken)
    {
        var users = await db.Users.ToDictionaryAsync(user => user.Id, cancellationToken);
        var cards = await db.Cards.ToDictionaryAsync(card => card.Id, cancellationToken);
        var deckIds = decks.Select(deck => deck.Id).ToArray();
        var activeDeckIds = await db.UserActiveDecks
            .Where(activeDeck => activeDeck.UserId == userId && deckIds.Contains(activeDeck.DeckId))
            .Select(activeDeck => activeDeck.DeckId)
            .ToArrayAsync(cancellationToken);
        var activeDeckSet = activeDeckIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return decks.Select(deck =>
        {
            var battlefieldIds = Deserialize(deck.BattlefieldDeckIdsJson);
            var runeIds = Deserialize(deck.RuneDeckIdsJson);
            var mainIds = Deserialize(deck.MainDeckIdsJson);
            var allCardIds = new[] { deck.LegendId, deck.ChampionId }.Concat(battlefieldIds).Concat(runeIds).Concat(mainIds);
            var deckCards = allCardIds.Select(id => cards.TryGetValue(id, out var card) ? card : null).Where(card => card is not null).Cast<CardEntity>().ToArray();
            var tags = deckCards.SelectMany(card => Deserialize(card.TagsJson)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(tag => tag).ToArray();
            var domains = deckCards.SelectMany(card => Deserialize(card.DomainsJson).Length > 0 ? Deserialize(card.DomainsJson) : [card.Domain]).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(domain => domain).ToArray();
            var legendName = cards.TryGetValue(deck.LegendId, out var legend) ? legend.Name : string.Empty;
            var championName = cards.TryGetValue(deck.ChampionId, out var champion) ? champion.Name : string.Empty;
            var author = users.TryGetValue(deck.OwnerUserId, out var owner) ? owner.DisplayName : deck.OwnerUserId;
            return new BrowseDeckDto(deck.Id, deck.Name, deck.OwnerUserId, deck.Visibility, author, deck.LegendId, deck.ChampionId, battlefieldIds, runeIds, mainIds, tags, domains, legendName, championName, new DeckCardCountsDto(mainIds.Length, runeIds.Length, battlefieldIds.Length), deck.Description, deck.UpdatedAt, activeDeckSet.Contains(deck.Id));
        }).ToArray();
    }

    private async Task<DeckEntity> GetActiveDeckForUserAsync(string userId, string deckId, CancellationToken cancellationToken)
    {
        var deck = await db.Decks.FindAsync([deckId], cancellationToken)
            ?? throw new InvalidOperationException("Selected deck was not found.");
        if (deck.DeletedAt is not null || (deck.Visibility != "public" && deck.OwnerUserId != userId))
        {
            throw new InvalidOperationException("Selected deck is not available to the current user.");
        }

        var isActive = await db.UserActiveDecks.AnyAsync(activeDeck => activeDeck.UserId == userId && activeDeck.DeckId == deckId, cancellationToken);
        if (!isActive)
        {
            throw new InvalidOperationException("Selected deck is not in the active deck list.");
        }

        return deck;
    }

    private async Task<CleanupResult> CleanupDeckReferencesAsync(string deckId, string? preserveOwnerUserId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var activeDecks = await db.UserActiveDecks
            .Where(activeDeck => activeDeck.DeckId == deckId && (preserveOwnerUserId == null || activeDeck.UserId != preserveOwnerUserId))
            .ToArrayAsync(cancellationToken);
        if (activeDecks.Length > 0)
        {
            db.UserActiveDecks.RemoveRange(activeDecks);
        }

        var tickets = await db.MatchmakingTickets
            .Where(ticket => ticket.DeckId == deckId && ticket.Status == "queued" && (preserveOwnerUserId == null || ticket.UserId != preserveOwnerUserId))
            .ToArrayAsync(cancellationToken);
        foreach (var ticket in tickets)
        {
            ticket.Status = "cancelled";
            ticket.UpdatedAt = now;
        }

        var lobbyPlayers = await db.LobbyPlayers
            .Where(player => player.DeckId == deckId && (preserveOwnerUserId == null || player.UserId != preserveOwnerUserId))
            .ToArrayAsync(cancellationToken);
        var lobbyIds = lobbyPlayers.Select(player => player.LobbyId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var player in lobbyPlayers)
        {
            player.DeckId = null;
            player.SelectedBattlefieldIdsJson = Serialize(Array.Empty<string>());
            player.IsReady = false;
            player.UpdatedAt = now;
        }

        if (lobbyIds.Length > 0)
        {
            var lobbies = await db.Lobbies.Where(lobby => lobbyIds.Contains(lobby.Id)).ToArrayAsync(cancellationToken);
            foreach (var lobby in lobbies)
            {
                lobby.UpdatedAt = now;
            }
        }

        return new CleanupResult(lobbyIds, tickets);
    }

    private async Task BroadcastCancelledTicketsAsync(IReadOnlyList<MatchmakingTicketEntity> tickets, CancellationToken cancellationToken)
    {
        foreach (var ticket in tickets)
        {
            await hubContext.Clients.Group(MatchHub.TicketGroupName(ticket.Id)).SendAsync("matchmaking.ticketUpdated", ToDto(ticket), cancellationToken);
        }
    }

    private async Task BroadcastUpdatedLobbiesAsync(IReadOnlyList<string> lobbyIds, string eventName, CancellationToken cancellationToken)
    {
        foreach (var lobbyId in lobbyIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var lobby = await db.Lobbies.FindAsync([lobbyId], cancellationToken);
            if (lobby is null)
            {
                continue;
            }

            await BroadcastLobbyAsync(await ToLobbyDtoAsync(lobby, cancellationToken), eventName, cancellationToken);
        }
    }

    private sealed record CleanupResult(
        IReadOnlyList<string> LobbyIds,
        IReadOnlyList<MatchmakingTicketEntity> CancelledTickets);

    private async Task<IReadOnlyDictionary<string, string?>> LoadAvatarImageHashesAsync(IEnumerable<MatchPlayerEntity> players, CancellationToken cancellationToken)
    {
        var userIds = players.Select(player => player.UserId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return await db.Users
            .Where(user => userIds.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.AvatarImageHash, cancellationToken);
    }

    private static MatchSummaryDto ToSummary(MatchEntity match, IEnumerable<MatchPlayerEntity> players, IReadOnlyDictionary<string, string?> avatarImageHashes)
    {
        return new MatchSummaryDto(match.Id, match.Mode, match.Status, players.OrderBy(player => player.PlayerId).Select(player => ToDto(player, avatarImageHashes)).ToArray(), match.CreatedAt, match.UpdatedAt, match.CompletedAt, match.WinnerPlayerId, match.WinningTeamId);
    }

    private static MatchSnapshotDto ToSnapshot(MatchEntity match, IEnumerable<MatchPlayerEntity> players, IReadOnlyDictionary<string, string?> avatarImageHashes, string stateJson, int sequenceNumber, int? viewerPlayerId = null)
    {
        var state = JsonNode.Parse(stateJson)!.AsObject();
        var view = viewerPlayerId is null ? state : PlayerViewRedactor.Redact(state, viewerPlayerId.Value);
        return new MatchSnapshotDto(match.Id, match.Mode, match.Status, players.OrderBy(player => player.PlayerId).Select(player => ToDto(player, avatarImageHashes)).ToArray(), match.CreatedAt, match.UpdatedAt, match.CompletedAt, match.WinnerPlayerId, match.WinningTeamId, view, sequenceNumber);
    }

    private static MatchPlayerDto ToDto(MatchPlayerEntity player, IReadOnlyDictionary<string, string?> avatarImageHashes)
    {
        avatarImageHashes.TryGetValue(player.UserId, out var avatarImageHash);
        return new MatchPlayerDto(player.PlayerId, player.UserId, player.DisplayName, avatarImageHash, player.DeckId, player.TeamId);
    }

    private static LegalActionDto ToDto(EngineLegalAction action)
    {
        return new LegalActionDto(action.Id, action.Type, action.PlayerId, action.Label, null, [], action.PayloadSchema?.Deserialize<IReadOnlyDictionary<string, object?>>(JsonOptions));
    }

    private static MatchEventDto ToDto(MatchEventEntity matchEvent)
    {
        return new MatchEventDto(
            matchEvent.Id,
            matchEvent.MatchId,
            matchEvent.SequenceNumber,
            matchEvent.PlayerId,
            matchEvent.ActionType,
            JsonNode.Parse(matchEvent.ActionPayloadJson) ?? new JsonObject(),
            JsonNode.Parse(matchEvent.ResultPayloadJson) ?? new JsonObject(),
            matchEvent.CreatedAt);
    }

    private static string BuildResultPayload(EngineActionResult result)
    {
        var payload = JsonSerializer.SerializeToNode(new { result.ResultMessage, result.Status }, JsonOptions)!.AsObject();
        if (result.ResultPayload is not null)
        {
            foreach (var item in result.ResultPayload)
            {
                payload[item.Key] = item.Value?.DeepClone();
            }
        }

        return payload.ToJsonString(JsonOptions);
    }

    private static MatchmakingTicketDto ToDto(MatchmakingTicketEntity ticket)
    {
        return new MatchmakingTicketDto(ticket.Id, ticket.UserId, ticket.DeckId, ticket.Mode, ticket.Status, ticket.CreatedAt, ticket.MatchId);
    }

    private async Task MigrateSchemaAsync(CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE users ADD COLUMN IF NOT EXISTS "Email" text NOT NULL DEFAULT '';
            ALTER TABLE users ADD COLUMN IF NOT EXISTS "NormalizedEmail" text NOT NULL DEFAULT '';
            ALTER TABLE users ADD COLUMN IF NOT EXISTS "PasswordHash" text NOT NULL DEFAULT '';
            ALTER TABLE users ADD COLUMN IF NOT EXISTS "IsAdmin" boolean NOT NULL DEFAULT false;
            ALTER TABLE users ADD COLUMN IF NOT EXISTS "IsDisabled" boolean NOT NULL DEFAULT false;
            ALTER TABLE users ADD COLUMN IF NOT EXISTS "UpdatedAt" timestamptz NOT NULL DEFAULT now();
            ALTER TABLE users ADD COLUMN IF NOT EXISTS "DisabledAt" timestamptz NULL;
            ALTER TABLE users ADD COLUMN IF NOT EXISTS "LastLoginAt" timestamptz NULL;
            ALTER TABLE users ADD COLUMN IF NOT EXISTS "AvatarImageHash" text NULL;
            ALTER TABLE users ADD COLUMN IF NOT EXISTS "GamesPlayed" integer NOT NULL DEFAULT 0;
            ALTER TABLE users ADD COLUMN IF NOT EXISTS "Wins" integer NOT NULL DEFAULT 0;
            ALTER TABLE users ADD COLUMN IF NOT EXISTS "Losses" integer NOT NULL DEFAULT 0;
            ALTER TABLE users ADD COLUMN IF NOT EXISTS "PointsScored" integer NOT NULL DEFAULT 0;
            ALTER TABLE users ADD COLUMN IF NOT EXISTS "LastPlayedAt" timestamptz NULL;
            UPDATE users SET "Email" = lower("Id") || '@legacy.riftbound.local' WHERE "Email" = '';
            UPDATE users SET "NormalizedEmail" = upper("Email") WHERE "NormalizedEmail" = '';

            ALTER TABLE decks ADD COLUMN IF NOT EXISTS "Description" text NULL;
            ALTER TABLE decks ADD COLUMN IF NOT EXISTS "TagsJson" jsonb NOT NULL DEFAULT '[]'::jsonb;
            ALTER TABLE decks ADD COLUMN IF NOT EXISTS "DeletedAt" timestamptz NULL;

            CREATE TABLE IF NOT EXISTS cards (
                "Id" text PRIMARY KEY,
                "Name" text NOT NULL,
                "Kind" text NOT NULL,
                "TagsJson" jsonb NOT NULL DEFAULT '[]'::jsonb,
                "Domain" text NOT NULL,
                "DomainsJson" jsonb NOT NULL DEFAULT '[]'::jsonb,
                "Cost" integer NOT NULL,
                "Might" integer NOT NULL,
                "Text" text NOT NULL,
                "Image" text NOT NULL,
                "CardType" text NOT NULL,
                "Supertype" text NULL,
                "EffectType" text NOT NULL,
                "EffectAmount" integer NOT NULL,
                "CreatedAt" timestamptz NOT NULL,
                "UpdatedAt" timestamptz NOT NULL
            );
            CREATE INDEX IF NOT EXISTS "IX_cards_Kind" ON cards("Kind");
            CREATE INDEX IF NOT EXISTS "IX_cards_Domain" ON cards("Domain");

            CREATE TABLE IF NOT EXISTS refresh_tokens (
                "Id" text PRIMARY KEY,
                "UserId" text NOT NULL,
                "TokenHash" text NOT NULL,
                "ExpiresAt" timestamptz NOT NULL,
                "RevokedAt" timestamptz NULL,
                "CreatedAt" timestamptz NOT NULL
            );
            CREATE TABLE IF NOT EXISTS profile_images (
                "Hash" text PRIMARY KEY,
                "ContentType" text NOT NULL,
                "Bytes" bytea NOT NULL,
                "Length" integer NOT NULL,
                "CreatedAt" timestamptz NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_users_NormalizedEmail" ON users("NormalizedEmail");
            CREATE INDEX IF NOT EXISTS "IX_refresh_tokens_UserId" ON refresh_tokens("UserId");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_refresh_tokens_TokenHash" ON refresh_tokens("TokenHash");
            CREATE INDEX IF NOT EXISTS "IX_decks_DeletedAt" ON decks("DeletedAt");

            CREATE TABLE IF NOT EXISTS user_active_decks (
                "UserId" text NOT NULL,
                "DeckId" text NOT NULL,
                "AddedAt" timestamptz NOT NULL,
                PRIMARY KEY ("UserId", "DeckId")
            );
            CREATE INDEX IF NOT EXISTS "IX_user_active_decks_DeckId" ON user_active_decks("DeckId");

            CREATE TABLE IF NOT EXISTS lobbies (
                "Id" text PRIMARY KEY,
                "HostUserId" text NOT NULL,
                "Name" text NOT NULL,
                "Status" text NOT NULL,
                "AllowedModesJson" jsonb NOT NULL DEFAULT '[]'::jsonb,
                "SelectedMode" text NOT NULL,
                "MaxPlayers" integer NOT NULL,
                "CreatedAt" timestamptz NOT NULL,
                "UpdatedAt" timestamptz NOT NULL,
                "MatchId" text NULL
            );
            CREATE INDEX IF NOT EXISTS "IX_lobbies_Status" ON lobbies("Status");
            CREATE INDEX IF NOT EXISTS "IX_lobbies_HostUserId" ON lobbies("HostUserId");

            CREATE TABLE IF NOT EXISTS lobby_players (
                "LobbyId" text NOT NULL,
                "UserId" text NOT NULL,
                "SeatIndex" integer NOT NULL,
                "DisplayName" text NOT NULL,
                "DeckId" text NULL,
                "SelectedBattlefieldIdsJson" jsonb NOT NULL DEFAULT '[]'::jsonb,
                "TeamId" integer NULL,
                "IsReady" boolean NOT NULL DEFAULT false,
                "JoinedAt" timestamptz NOT NULL,
                "UpdatedAt" timestamptz NOT NULL,
                PRIMARY KEY ("LobbyId", "UserId")
            );
            CREATE INDEX IF NOT EXISTS "IX_lobby_players_LobbyId" ON lobby_players("LobbyId");
            CREATE INDEX IF NOT EXISTS "IX_lobby_players_UserId" ON lobby_players("UserId");
            """, cancellationToken);
    }

    private async Task ValidateDeckAsync(string legendId, string championId, IReadOnlyList<string> battlefieldDeckIds, IReadOnlyList<string> runeDeckIds, IReadOnlyList<string> mainDeckIds, CancellationToken cancellationToken)
    {
        var allIds = new[] { legendId, championId }.Concat(battlefieldDeckIds).Concat(runeDeckIds).Concat(mainDeckIds).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var entities = await db.Cards.Where(card => allIds.Contains(card.Id)).ToListAsync(cancellationToken);
        var cards = entities.ToDictionary(card => card.Id, ToCardDefinition, StringComparer.OrdinalIgnoreCase);
        var result = DeckConstructionRules.Validate(
            new DeckConstructionRequest(legendId, championId, battlefieldDeckIds, runeDeckIds, mainDeckIds),
            cards);
        if (!result.IsValid)
        {
            throw new InvalidOperationException(string.Join(" ", result.Errors));
        }
    }

    private static string Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }

    private static string[] Deserialize(string json)
    {
        return JsonSerializer.Deserialize<string[]>(json, JsonOptions) ?? [];
    }

    private static int StableSeed(string value)
    {
        var hash = 17;
        foreach (var character in value)
        {
            hash = unchecked(hash * 31 + character);
        }

        return Math.Abs(hash);
    }

    private sealed record ModeSpec(string Id, string Label, int PlayerCount, int BattlefieldCount)
    {
        public static ModeSpec For(string mode)
        {
            return NormalizeMode(mode) switch
            {
                "ffa-3" => new ModeSpec("ffa-3", "FFA3 Skirmish", 3, 3),
                "ffa-4" => new ModeSpec("ffa-4", "FFA4 War", 4, 3),
                "teams-2v2" => new ModeSpec("teams-2v2", "2v2 Magma Chamber", 4, 3),
                _ => new ModeSpec("duel-1v1", "1v1 Duel", 2, 2)
            };
        }
    }

    private sealed record RiftCodexPage(
        RiftCodexCard[]? Items,
        int Total,
        int Page,
        int Size,
        int Pages);

    private sealed record RiftCodexCard(
        string Id,
        string Name,
        [property: JsonPropertyName("riftbound_id")] string? RiftboundId,
        [property: JsonPropertyName("tcgplayer_id")] string? TcgplayerId,
        [property: JsonPropertyName("collector_number")] int? CollectorNumber,
        RiftCodexAttributes? Attributes,
        RiftCodexClassification? Classification,
        RiftCodexText? Text,
        RiftCodexSet? Set,
        RiftCodexMedia? Media,
        string[]? Tags,
        string? Orientation,
        RiftCodexMetadata? Metadata);

    private sealed record RiftCodexAttributes(int? Energy, int? Might, int? Power);

    private sealed record RiftCodexClassification(string? Type, string? Supertype, string? Rarity, string[]? Domain);

    private sealed record RiftCodexText(string? Rich, string? Plain, string? Flavour);

    private sealed record RiftCodexSet([property: JsonPropertyName("set_id")] string? SetId, string? Label);

    private sealed record RiftCodexMedia([property: JsonPropertyName("image_url")] string? ImageUrl, string? Artist, [property: JsonPropertyName("accessibility_text")] string? AccessibilityText);

    private sealed record RiftCodexMetadata([property: JsonPropertyName("clean_name")] string? CleanName, [property: JsonPropertyName("updated_on")] string? UpdatedOn, [property: JsonPropertyName("alternate_art")] bool? AlternateArt, bool? Overnumbered, bool? Signature);
}
