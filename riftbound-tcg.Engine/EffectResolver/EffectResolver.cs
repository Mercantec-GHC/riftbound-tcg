using riftbound_tcg.Core.Cards;
using riftbound_tcg.Core.Effects;
using riftbound_tcg.Core.GameState;

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

        var item = stack[0];
        var remaining = stack.Skip(1).ToList();

        var (updatedPlayers, updatedBattlefields) = ApplyEffect(item, players, battlefields);

        return new EffectResolution(item, remaining, updatedPlayers, updatedBattlefields);
    }

    private static (List<PlayerState> Players, List<BattlefieldState> Battlefields) ApplyEffect(
        StackItem item,
        IReadOnlyList<PlayerState> players,
        IReadOnlyList<BattlefieldState> battlefields)
    {
        return item.Effect.Type switch
        {
            CardEffectType.Draw => (ApplyDraw(item, players), battlefields.ToList()),
            CardEffectType.Buff => ApplyUnitMod(item, players, battlefields, (u, amt) => u with { AttachedMight = u.AttachedMight + amt }),
            CardEffectType.Rally => ApplyUnitMod(item, players, battlefields, (u, _) => u with { Exhausted = false }),
            CardEffectType.Damage => ApplyDamage(item, players, battlefields),
            _ => (players.ToList(), battlefields.ToList()),
        };
    }

    private static List<PlayerState> ApplyDraw(StackItem item, IReadOnlyList<PlayerState> players)
    {
        return players.Select(p =>
        {
            if (p.Id != item.PlayerId) return p;
            var drawn = p.DeckCardIds.Take(item.Effect.Amount).ToList();
            return p with
            {
                DeckCardIds = p.DeckCardIds.Skip(item.Effect.Amount).ToList(),
                HandCardIds = p.HandCardIds.Concat(drawn).ToList(),
            };
        }).ToList();
    }

    private static (List<PlayerState>, List<BattlefieldState>) ApplyUnitMod(
        StackItem item,
        IReadOnlyList<PlayerState> players,
        IReadOnlyList<BattlefieldState> battlefields,
        Func<UnitState, int, UnitState> modify)
    {
        if (item.TargetUnitId is null)
            return (players.ToList(), battlefields.ToList());

        var updatedPlayers = players.Select(p => p with
        {
            Base = p.Base.Select(u => u.Uid == item.TargetUnitId ? modify(u, item.Effect.Amount) : u).ToList(),
        }).ToList();

        var updatedFields = battlefields.Select(b => b with
        {
            Units = b.Units.Select(u => u.Uid == item.TargetUnitId ? modify(u, item.Effect.Amount) : u).ToList(),
        }).ToList();

        return (updatedPlayers, updatedFields);
    }

    private static (List<PlayerState>, List<BattlefieldState>) ApplyDamage(
        StackItem item,
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
            Base = p.Base.Select(u => u.Uid == targetUid ? u with { Damage = u.Damage + item.Effect.Amount } : u).ToList(),
        }).ToList();

        var updatedFields = battlefields.Select(b => b with
        {
            Units = b.Units.Select(u => u.Uid == targetUid ? u with { Damage = u.Damage + item.Effect.Amount } : u).ToList(),
        }).ToList();

        return (updatedPlayers, updatedFields);
    }
}

public sealed record EffectResolution(
    StackItem ResolvedItem,
    IReadOnlyList<StackItem> RemainingStack,
    IReadOnlyList<PlayerState> UpdatedPlayers,
    IReadOnlyList<BattlefieldState> UpdatedBattlefields);
