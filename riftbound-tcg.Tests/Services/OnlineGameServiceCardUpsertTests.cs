using System.Reflection;
using RiftboundTcg.Server.Api.Services;

namespace riftbound_tcg.Tests.Services;

public sealed class OnlineGameServiceCardUpsertTests
{
    [Test]
    public void normalize_effect_amount_preserves_negative_buff_amounts()
    {
        var amount = InvokeNormalizeEffectAmount("buff", -4);

        Assert.That(amount, Is.EqualTo(-4));
    }

    [Test]
    public void normalize_effect_amount_clamps_negative_non_buff_amounts()
    {
        var amount = InvokeNormalizeEffectAmount("damage", -4);

        Assert.That(amount, Is.EqualTo(0));
    }

    private static int InvokeNormalizeEffectAmount(string effectType, int amount)
    {
        var method = typeof(OnlineGameService).GetMethod("NormalizeEffectAmount", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(method, Is.Not.Null);
        var result = method!.Invoke(null, [effectType, amount]);
        Assert.That(result, Is.InstanceOf<int>());
        return (int)result!;
    }
}
