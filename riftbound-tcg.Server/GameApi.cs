using riftbound_tcg.Core.Cards;
using riftbound_tcg.Core.Effects;
using riftbound_tcg.Core.GameState;
using riftbound_tcg.Engine.EffectResolver;
using riftbound_tcg.Engine.RulesEngine;

namespace RiftboundTcg.Server;

public static class GameApi
{
    public static IEndpointRouteBuilder MapGameEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/game")
            .WithTags("Game Rules");

        // Returns the spell subtype and whether the card has an on-play effect trigger.
        api.MapPost("/classify-card", (CardDefinition card) =>
        {
            var subtype = SpellClassifier.GetSpellSubtype(card);
            var hasOnPlayEffect = SpellClassifier.HasOnPlayEffect(card);
            return Results.Ok(new CardClassification(subtype, hasOnPlayEffect));
        })
        .WithName("ClassifyCard")
        .WithSummary("Classify a card's spell subtype and on-play trigger.");

        // Validates whether a card can be legally added to the chain right now.
        api.MapPost("/validate-chain-play", (ValidateChainPlayRequest req) =>
        {
            var result = ChainRules.ValidateChainPlay(req.Card, req.PlayerId, req.TurnPlayerId, req.ChainWindow);
            return result.IsValid ? Results.Ok(result) : Results.BadRequest(result);
        })
        .WithName("ValidateChainPlay")
        .WithSummary("Validate whether a card can be played into the current chain window.");

        // Resolves the top item of the effect stack and returns the updated state fragments.
        api.MapPost("/resolve-stack-top", (ResolveStackTopRequest req) =>
        {
            var resolution = EffectResolver.ResolveTop(req.EffectStack, req.Players, req.Battlefields);
            if (resolution is null)
                return Results.BadRequest(new { error = "Effect stack is empty." });
            return Results.Ok(resolution);
        })
        .WithName("ResolveStackTop")
        .WithSummary("Resolve the top item of the effect stack (LIFO). Returns updated players and battlefields.");

        // Records a player passing their chain window priority.
        // Returns null for UpdatedChainWindow when all players have passed (stack should resolve).
        api.MapPost("/pass-chain-window", (PassChainWindowRequest req) =>
        {
            var updated = ChainRules.Pass(req.ChainWindow, req.PlayerId, req.TurnOrder);
            return Results.Ok(new PassChainWindowResponse(AllPassed: updated is null, UpdatedChainWindow: updated));
        })
        .WithName("PassChainWindow")
        .WithSummary("Player passes chain window priority. Returns whether all players have passed.");

        return app;
    }
}

// --- Request / Response DTOs ---

public sealed record CardClassification(SpellSubtype SpellSubtype, bool HasOnPlayEffect);

public sealed record ValidateChainPlayRequest(
    CardDefinition Card,
    int PlayerId,
    int TurnPlayerId,
    ChainWindow? ChainWindow);

public sealed record ResolveStackTopRequest(
    IReadOnlyList<StackItem> EffectStack,
    IReadOnlyList<PlayerState> Players,
    IReadOnlyList<BattlefieldState> Battlefields);

public sealed record PassChainWindowRequest(
    ChainWindow ChainWindow,
    int PlayerId,
    IReadOnlyList<int> TurnOrder);

public sealed record PassChainWindowResponse(bool AllPassed, ChainWindow? UpdatedChainWindow);
