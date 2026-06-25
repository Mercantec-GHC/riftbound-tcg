using riftbound_tcg.Core.Cards;
using riftbound_tcg.Core.Effects;
using riftbound_tcg.Core.GameState;
using riftbound_tcg.Engine.RulesEngine;

namespace riftbound_tcg.Engine.EffectResolver;

public static class EffectResolver
{
    // Resolves the top item on the stack and returns updated players and battlefields.
    // Returns null if the stack is empty.
    public static EffectResolution? ResolveTop(
        IReadOnlyList<StackItem> stack,
        IReadOnlyList<PlayerState> players,
        IReadOnlyList<BattlefieldState> battlefields)
    {
        if (stack.Count == 0) return null;

        var item = ChainRules.Finalize(stack[0]);
        var remaining = stack.Skip(1).ToList();

        var (updatedPlayers, updatedBattlefields) = ApplyEffect(item, players, battlefields);

        return new EffectResolution(item, remaining, updatedPlayers, updatedBattlefields);
    }

    private static (List<PlayerState> Players, List<BattlefieldState> Battlefields) ApplyEffect(
        StackItem item,
        IReadOnlyList<PlayerState> players,
        IReadOnlyList<BattlefieldState> battlefields)
    {
        if (item.Effect.Steps.Count == 0)
        {
            return ApplyStep(item, item.Effect.Type, item.Effect.Amount, players, battlefields);
        }

        var currentPlayers = players.ToList();
        var currentBattlefields = battlefields.ToList();
        foreach (var step in item.Effect.Steps)
        {
            (currentPlayers, currentBattlefields) = ApplyStep(item, step.Type, step.Amount, currentPlayers, currentBattlefields);
        }

        return (currentPlayers, currentBattlefields);
    }

    private static (List<PlayerState>, List<BattlefieldState>) ApplyStep(
        StackItem item,
        CardEffectType type,
        int amount,
        IReadOnlyList<PlayerState> players,
        IReadOnlyList<BattlefieldState> battlefields)
    {
        return type switch
        {
            CardEffectType.Draw => (ApplyDraw(item, amount, players), battlefields.ToList()),
            CardEffectType.Buff => ApplyUnitMod(item, players, battlefields, (u, amt) => u with { AttachedMight = u.AttachedMight + amt }, amount),
            CardEffectType.Rally => ApplyUnitMod(item, players, battlefields, (u, _) => u with { Exhausted = false }, amount),
            CardEffectType.Stun => ApplyUnitMod(item, players, battlefields, (u, _) => u with { Exhausted = true }, amount),
            CardEffectType.Damage => ApplyDamage(item, amount, players, battlefields),
            CardEffectType.Kill => ApplyKill(item, players, battlefields, banish: false),
            CardEffectType.Banish => ApplyKill(item, players, battlefields, banish: true),
            _ => (players.ToList(), battlefields.ToList()),
        };
    }

    private static List<PlayerState> ApplyDraw(StackItem item, int amount, IReadOnlyList<PlayerState> players)
    {
        return players.Select(p =>
        {
            if (p.Id != item.PlayerId) return p;
            var drawn = p.DeckCardIds.Take(amount).ToList();
            return p with
            {
                DeckCardIds = p.DeckCardIds.Skip(amount).ToList(),
                HandCardIds = p.HandCardIds.Concat(drawn).ToList(),
            };
        }).ToList();
    }

    private static (List<PlayerState>, List<BattlefieldState>) ApplyUnitMod(
        StackItem item,
        IReadOnlyList<PlayerState> players,
        IReadOnlyList<BattlefieldState> battlefields,
        Func<UnitState, int, UnitState> modify,
        int amount)
    {
        if (item.TargetUnitId is null)
            return (players.ToList(), battlefields.ToList());

        var updatedPlayers = players.Select(p => p with
        {
            Base = p.Base.Select(u => u.Uid == item.TargetUnitId ? modify(u, amount) : u).ToList(),
        }).ToList();

        var updatedFields = battlefields.Select(b => b with
        {
            Units = b.Units.Select(u => u.Uid == item.TargetUnitId ? modify(u, amount) : u).ToList(),
        }).ToList();

        return (updatedPlayers, updatedFields);
    }

    private static (List<PlayerState>, List<BattlefieldState>) ApplyDamage(
        StackItem item,
        int amount,
        IReadOnlyList<PlayerState> players,
        IReadOnlyList<BattlefieldState> battlefields)
    {
        // Find the first enemy unit in the target lane, or the explicitly named unit.
        string? targetUid = null;
        if (item.TargetLaneId is not null)
        {
            targetUid = battlefields
                .FirstOrDefault(b => b.Id == item.TargetLaneId)
                ?.Units.FirstOrDefault(u => u.OwnerPlayerId != item.PlayerId)
                ?.Uid;
        }
        else if (item.TargetUnitId is not null)
        {
            var allUnits = players.SelectMany(p => p.Base)
                .Concat(battlefields.SelectMany(b => b.Units));
            targetUid = allUnits.FirstOrDefault(u => u.Uid == item.TargetUnitId && u.OwnerPlayerId != item.PlayerId)?.Uid;
        }

        if (targetUid is null)
            return (players.ToList(), battlefields.ToList());

        var updatedPlayers = players.Select(p => p with
        {
            Base = p.Base.Select(u => u.Uid == targetUid ? u with { Damage = u.Damage + amount } : u).ToList(),
        }).ToList();

        var updatedFields = battlefields.Select(b => b with
        {
            Units = b.Units.Select(u => u.Uid == targetUid ? u with { Damage = u.Damage + amount } : u).ToList(),
        }).ToList();

        return (updatedPlayers, updatedFields);
    }

    // Kill moves the targeted unit to its owner's trash; banish removes it from the game instead.
    private static (List<PlayerState>, List<BattlefieldState>) ApplyKill(
        StackItem item,
        IReadOnlyList<PlayerState> players,
        IReadOnlyList<BattlefieldState> battlefields,
        bool banish)
    {
        if (item.TargetUnitId is null)
            return (players.ToList(), battlefields.ToList());

        var targetUid = item.TargetUnitId;
        var removedFromBase = players.SelectMany(p => p.Base).FirstOrDefault(u => u.Uid == targetUid);
        var removedFromField = battlefields.SelectMany(b => b.Units).FirstOrDefault(u => u.Uid == targetUid);
        var removed = removedFromBase ?? removedFromField;
        if (removed is null)
            return (players.ToList(), battlefields.ToList());

        var updatedPlayers = players.Select(p => p with
        {
            Base = p.Base.Where(u => u.Uid != targetUid).ToList(),
            TrashCardIds = !banish && p.Id == removed.OwnerPlayerId ? p.TrashCardIds.Append(removed.CardId).ToList() : p.TrashCardIds,
            BanishedCardIds = banish && p.Id == removed.OwnerPlayerId ? (p.BanishedCardIds ?? []).Append(removed.CardId).ToList() : p.BanishedCardIds,
        }).ToList();

        var updatedFields = battlefields.Select(b => b with
        {
            Units = b.Units.Where(u => u.Uid != targetUid).ToList(),
        }).ToList();

        return (updatedPlayers, updatedFields);
    }
}

public sealed record EffectResolution(
    StackItem ResolvedItem,
    IReadOnlyList<StackItem> RemainingStack,
    IReadOnlyList<PlayerState> UpdatedPlayers,
    IReadOnlyList<BattlefieldState> UpdatedBattlefields);
