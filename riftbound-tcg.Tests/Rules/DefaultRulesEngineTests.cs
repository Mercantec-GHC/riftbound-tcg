using riftbound_tcg.Engine.RulesEngine;

namespace riftbound_tcg.Tests.Rules;

public class DefaultRulesEngineTests
{
    [Test]
    public void legal_actions_are_returned_for_a_seated_player()
    {
        var engine = new DefaultRulesEngine();
        var state = CreateState();

        var actions = engine.GetLegalActions(state, 0);

        Assert.That(actions.Select(action => action.Type), Contains.Item("advance-phase"));
        Assert.That(actions.Select(action => action.Type), Contains.Item("confirm-mulligan"));
        Assert.That(actions.Select(action => action.Type), Contains.Item("concede"));
    }

    [Test]
    public void unsupported_action_is_rejected()
    {
        var engine = new DefaultRulesEngine();
        var state = CreateState();

        var result = engine.ApplyAction(state, new EngineGameAction("user-demo-001", "play-card", new { }));

        Assert.That(result.Accepted, Is.False);
        Assert.That(result.Status, Is.EqualTo("rejected"));
        Assert.That(result.State.SequenceNumber, Is.EqualTo(state.SequenceNumber));
    }

    [Test]
    public void supported_action_advances_sequence()
    {
        var engine = new DefaultRulesEngine();
        var state = CreateState();

        var result = engine.ApplyAction(state, new EngineGameAction("user-demo-001", "advance-phase", new { }));

        Assert.That(result.Accepted, Is.True);
        Assert.That(result.State.SequenceNumber, Is.EqualTo(2));
    }

    private static EngineMatchState CreateState()
    {
        return new EngineMatchState(
            "match-demo-001",
            "mulligan",
            1,
            [new EnginePlayerState(0, "user-demo-001", 0, true)]);
    }
}
