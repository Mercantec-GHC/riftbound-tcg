using System.Text.Json.Nodes;

namespace riftbound_tcg.Engine.RulesEngine;

public static class PlayerViewRedactor
{
    public static JsonObject Redact(JsonObject state, int viewerPlayerId)
    {
        var view = state.DeepClone().AsObject();
        if (view["players"] is JsonArray players)
        {
            for (var playerIndex = 0; playerIndex < players.Count; playerIndex++)
            {
                if (players[playerIndex] is not JsonObject player)
                {
                    continue;
                }

                var playerId = player["id"]?.GetValue<int>() ?? -1;
                var isViewer = playerId == viewerPlayerId;
                player["deck"] = HiddenCards(player["deck"] as JsonArray, playerId, "deck");
                player["runeDeck"] = HiddenCards(player["runeDeck"] as JsonArray, playerId, "rune-deck");
                if (!isViewer)
                {
                    player["hand"] = HiddenCards(player["hand"] as JsonArray, playerId, "hand");
                }
                else
                {
                    RedactCardArray(player["hand"] as JsonArray, viewerPlayerId, "hand", playerId);
                }

                RedactCardArray(player["runes"]?["ready"] as JsonArray, viewerPlayerId, "ready-rune", playerId);
                RedactCardArray(player["runes"]?["exhausted"] as JsonArray, viewerPlayerId, "exhausted-rune", playerId);
                RedactCardArray(player["trash"] as JsonArray, viewerPlayerId, "trash", playerId);
                RedactCardArray(player["base"] as JsonArray, viewerPlayerId, "base", playerId);
                RedactCardArray(player["baseGear"] as JsonArray, viewerPlayerId, "base-gear", playerId);
                RedactCardProperty(player, "champion", viewerPlayerId, "champion", playerId);
                RedactCardProperty(player, "legend", viewerPlayerId, "legend", playerId);
            }
        }

        if (view["battlefields"] is JsonArray battlefields)
        {
            foreach (var battlefield in battlefields.OfType<JsonObject>())
            {
                RedactCardArray(battlefield["units"] as JsonArray, viewerPlayerId, "battlefield-unit", null);
            }
        }

        if (view["effectStack"] is JsonArray effectStack)
        {
            foreach (var item in effectStack.OfType<JsonObject>())
            {
                RedactStackItem(item, viewerPlayerId);
            }
        }

        if (view["selectedCard"] is JsonObject selectedCard
            && (selectedCard["player"]?.GetValue<int?>() ?? selectedCard["playerId"]?.GetValue<int?>()) != viewerPlayerId)
        {
            view["selectedCard"] = null;
        }

        view["viewerPlayerId"] = viewerPlayerId;
        return view;
    }

    private static JsonArray HiddenCards(JsonArray? source, int ownerPlayerId, string zone)
    {
        var count = source?.Count ?? 0;
        var redacted = new JsonArray();
        for (var index = 0; index < count; index++)
        {
            redacted.Add(HiddenCard(ownerPlayerId, zone, index));
        }

        return redacted;
    }

    private static void RedactCardArray(JsonArray? cards, int viewerPlayerId, string zone, int? ownerPlayerId)
    {
        if (cards is null)
        {
            return;
        }

        for (var index = 0; index < cards.Count; index++)
        {
            if (cards[index] is JsonObject card && ShouldHideFacedown(card, viewerPlayerId))
            {
                cards[index] = HiddenCard(OwnerId(card, ownerPlayerId), zone, index, card);
            }
        }
    }

    private static void RedactCardProperty(JsonObject parent, string propertyName, int viewerPlayerId, string zone, int ownerPlayerId)
    {
        if (parent[propertyName] is JsonObject card && ShouldHideFacedown(card, viewerPlayerId))
        {
            parent[propertyName] = HiddenCard(OwnerId(card, ownerPlayerId), zone, 0, card);
        }
    }

    private static void RedactStackItem(JsonObject item, int viewerPlayerId)
    {
        if (!ShouldHideFacedown(item, viewerPlayerId))
        {
            return;
        }

        item["cardId"] = $"hidden-stack-{item["playerId"]?.GetValue<int?>() ?? -1}";
        item["cardName"] = "Hidden card";
    }

    private static bool ShouldHideFacedown(JsonObject card, int viewerPlayerId)
    {
        var faceDown = ReadBool(card, "faceDown")
            || ReadBool(card, "facedown")
            || ReadBool(card, "isFaceDown")
            || ReadBool(card, "hidden");
        if (!faceDown || ReadBool(card, "public") || ReadBool(card, "revealed"))
        {
            return false;
        }

        var ownerId = OwnerId(card, null);
        if (ownerId == viewerPlayerId)
        {
            return false;
        }

        return !RevealedTo(card, viewerPlayerId);
    }

    private static bool RevealedTo(JsonObject card, int viewerPlayerId)
    {
        return ContainsPlayerId(card["revealedToPlayerIds"], viewerPlayerId)
            || ContainsPlayerId(card["revealedTo"], viewerPlayerId)
            || ContainsPlayerId(card["visibleToPlayerIds"], viewerPlayerId)
            || ContainsPlayerId(card["visibleTo"], viewerPlayerId);
    }

    private static bool ContainsPlayerId(JsonNode? node, int viewerPlayerId)
    {
        return node is JsonArray array
            && array.Any(item => MatchesPlayerId(item, viewerPlayerId));
    }

    private static bool MatchesPlayerId(JsonNode? node, int viewerPlayerId)
    {
        if (node is null)
        {
            return false;
        }

        try
        {
            return node.GetValue<int>() == viewerPlayerId;
        }
        catch (InvalidOperationException)
        {
            return node.GetValue<string>() == viewerPlayerId.ToString();
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool ReadBool(JsonObject obj, string key)
    {
        return obj[key]?.GetValue<bool?>() == true;
    }

    private static int OwnerId(JsonObject card, int? fallback)
    {
        return card["ownerId"]?.GetValue<int?>()
            ?? card["owner"]?.GetValue<int?>()
            ?? card["ownerPlayerId"]?.GetValue<int?>()
            ?? fallback
            ?? -1;
    }

    private static JsonObject HiddenCard(int ownerPlayerId, string zone, int index, JsonObject? source = null)
    {
        var hidden = new JsonObject
        {
            ["id"] = $"hidden-{zone}-{ownerPlayerId}-{index}",
            ["catalogId"] = null,
            ["name"] = "Hidden card",
            ["kind"] = "token",
            ["tags"] = new JsonArray(),
            ["domain"] = "Hidden",
            ["domains"] = new JsonArray(),
            ["cost"] = 0,
            ["might"] = 0,
            ["text"] = string.Empty,
            ["image"] = "*",
            ["cardType"] = "Hidden",
            ["supertype"] = null,
            ["effect"] = new JsonObject { ["type"] = "rally", ["amount"] = 0 },
            ["hidden"] = true,
            ["ownerId"] = ownerPlayerId
        };

        Preserve(source, hidden, "uid");
        Preserve(source, hidden, "owner");
        Preserve(source, hidden, "ownerId");
        Preserve(source, hidden, "ownerPlayerId");
        Preserve(source, hidden, "location");
        Preserve(source, hidden, "exhausted");
        Preserve(source, hidden, "damage");
        Preserve(source, hidden, "attachedMight");
        Preserve(source, hidden, "attacker");
        Preserve(source, hidden, "defender");
        Preserve(source, hidden, "attachedUnitId");
        Preserve(source, hidden, "faceDown");
        Preserve(source, hidden, "facedown");
        Preserve(source, hidden, "isFaceDown");
        Preserve(source, hidden, "revealedToPlayerIds");
        Preserve(source, hidden, "revealedTo");
        return hidden;
    }

    private static void Preserve(JsonObject? source, JsonObject target, string propertyName)
    {
        if (source?[propertyName] is { } value)
        {
            target[propertyName] = value.DeepClone();
        }
    }
}
