using riftbound_tcg.Core.Cards;
using riftbound_tcg.Core.Effects;
using riftbound_tcg.Core.GameState;

namespace riftbound_tcg.Engine.EffectResolver;

public static class EffectResolver
{
    // Resolves the top item on the stack and returns updated players and battlefields.
    // Returns null if there is nothing to resolve.
    public static EffectResolution? ResolveTop(
        IReadOnlyList<StackItem> stack,
        IReadOnlyList<PlayerState> players,
        IReadOnlyList<BattlefieldState> battlefields)
    {
        if (stack.Count == 0) return null;

        var item = stack[0];
        var remaining = stack.Skip(1).ToList();

        var updatedPlayers = ApplyEffect(item, players, battlefields, out var updatedBattlefields);

        return new EffectResolution(item, remaining, updatedPlayers, updatedBattlefields ?? battlefields.ToList());
    }

    private static List<PlayerState> ApplyEffect(
        StackItem item,
        IReadOnlyList<PlayerState> players,
        IReadOnlyList<BattlefieldState> battlefields,
        out List<BattlefieldState>? updatedBattlefields)
    {
        updatedBattlefields = null;

        return item.Effect.Type switch
        {
            EffectType.Draw => ApplyDraw(item, players),
            EffectType.Buff => ApplyBuff(item, players, battlefields),
            EffectType.Rally => ApplyRally(item, players, battlefields),
            EffectType.Damage => ApplyDamage(item, players, battlefields, out updatedBattlefields),
            _ => players.ToList(),
        };
    }

    private static List<PlayerState> ApplyDraw(StackItem item, IReadOnlyList<PlayerState> players)
    {
        return players.Select(p =>
        {
            if (p.Id != item.PlayerId) return p;
            var drawn = p.Deck.Take(item.Effect.Amount).ToList();
            return p with
            {
                Deck = p.Deck.Skip(item.Effect.Amount).ToList(),
                Hand = p.Hand.Concat(drawn).ToList(),
            };
        }).ToList();
    }

    private static List<PlayerState> ApplyBuff(
        StackItem item,
        IReadOnlyList<PlayerState> players,
        IReadOnlyList<BattlefieldState> battlefields)
    {
        if (item.TargetUnitId is null) return players.ToList();

        return players.Select(p => p with
        {
            Base = p.Base.Select(u => u.Uid == item.TargetUnitId
                ? u with { AttachedMight = u.AttachedMight + item.Effect.Amount }
                : u).ToList(),
        }).ToList();
    }

    private static List<PlayerState> ApplyRally(
        StackItem item,
        IReadOnlyList<PlayerState> players,
        IReadOnlyList<BattlefieldState> battlefields)
    {
        if (item.TargetUnitId is null) return players.ToList();

        return players.Select(p => p with
        {
            Base = p.Base.Select(u => u.Uid == item.TargetUnitId
                ? u with { Exhausted = false }
                : u).ToList(),
        }).ToList();
    }

    private static List<PlayerState> ApplyDamage(
        StackItem item,
        IReadOnlyList<PlayerState> players,
        IReadOnlyList<BattlefieldState> battlefields,
        out List<BattlefieldState>? updatedBattlefields)
    {
        updatedBattlefields = null;

        // Find target: first enemy unit in the target lane, or the named target unit.
        UnitInstance? target = null;
        if (item.TargetLaneId is not null)
        {
            var lane = battlefields.FirstOrDefault(b => b.Id == item.TargetLaneId);
            target = lane?.Units.FirstOrDefault(u => u.Owner != item.PlayerId);
        }
        else if (item.TargetUnitId is not null)
        {
            target = battlefields
                .SelectMany(b => b.Units)
                .Concat(players.SelectMany(p => p.Base))
                .FirstOrDefault(u => u.Uid == item.TargetUnitId && u.Owner != item.PlayerId);
        }

        if (target is null) return players.ToList();

        // Apply damage to unit wherever it lives (base or battlefield).
        updatedBattlefields = battlefields.Select(b => b with
        {
            Units = b.Units.Select(u => u.Uid == target.Uid
                ? u with { Damage = u.Damage + item.Effect.Amount }
                : u).ToList(),
        }).ToList();

        return players.Select(p => p with
        {
            Base = p.Base.Select(u => u.Uid == target.Uid
                ? u with { Damage = u.Damage + item.Effect.Amount }
                : u).ToList(),
        }).ToList();
    }
}

public sealed record EffectResolution(
    StackItem ResolvedItem,
    IReadOnlyList<StackItem> RemainingStack,
    IReadOnlyList<PlayerState> UpdatedPlayers,
    IReadOnlyList<BattlefieldState> UpdatedBattlefields);
