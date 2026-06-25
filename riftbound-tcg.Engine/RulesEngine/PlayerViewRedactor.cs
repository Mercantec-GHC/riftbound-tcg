using System.Text.Json.Nodes;

namespace riftbound_tcg.Engine.RulesEngine;

public static class PlayerViewRedactor
{
    public static JsonObject Redact(JsonObject state, int viewerPlayerId)
    {
        return Redact(state, (int?)viewerPlayerId);
    }

    public static JsonObject RedactForSpectator(JsonObject state)
    {
        return Redact(state, null);
    }

    private static JsonObject Redact(JsonObject state, int? viewerPlayerId)
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
                player["deck"] = RedactHiddenZone(view, player["deck"] as JsonArray, viewerPlayerId, playerId, "deck");
                player["runeDeck"] = RedactHiddenZone(view, player["runeDeck"] as JsonArray, viewerPlayerId, playerId, "rune-deck");
                player["hand"] = RedactHiddenZone(view, player["hand"] as JsonArray, viewerPlayerId, playerId, "hand");

                RedactCardArray(view, player["runes"]?["ready"] as JsonArray, viewerPlayerId, "ready-rune", playerId);
                RedactCardArray(view, player["runes"]?["exhausted"] as JsonArray, viewerPlayerId, "exhausted-rune", playerId);
                RedactCardArray(view, player["trash"] as JsonArray, viewerPlayerId, "trash", playerId);
                RedactCardArray(view, player["banished"] as JsonArray, viewerPlayerId, "banished", playerId);
                RedactCardArray(view, player["base"] as JsonArray, viewerPlayerId, "base", playerId);
                RedactCardArray(view, player["baseGear"] as JsonArray, viewerPlayerId, "base-gear", playerId);
                RedactCardProperty(view, player, "champion", viewerPlayerId, "champion", playerId);
                RedactCardProperty(view, player, "legend", viewerPlayerId, "legend", playerId);
            }
        }

        if (view["battlefields"] is JsonArray battlefields)
        {
            foreach (var battlefield in battlefields.OfType<JsonObject>())
            {
                RedactCardArray(view, battlefield["units"] as JsonArray, viewerPlayerId, "battlefield-unit", null);
                RedactCardArray(view, battlefield["hiddenCards"] as JsonArray, viewerPlayerId, "battlefield-hidden", null);
            }
        }

        if (view["effectStack"] is JsonArray effectStack)
        {
            foreach (var item in effectStack.OfType<JsonObject>())
            {
                RedactStackItem(view, item, viewerPlayerId);
            }
        }

        RedactRevealedCards(view, viewerPlayerId);
        RedactPendingVision(view, viewerPlayerId);

        if (viewerPlayerId is null
            || view["selectedCard"] is JsonObject selectedCard
            && (selectedCard["player"]?.GetValue<int?>() ?? selectedCard["playerId"]?.GetValue<int?>()) != viewerPlayerId)
        {
            view["selectedCard"] = null;
        }

        view["viewerPlayerId"] = viewerPlayerId;
        return view;
    }

    private static JsonArray RedactHiddenZone(JsonObject state, JsonArray? source, int? viewerPlayerId, int ownerPlayerId, string zone)
    {
        var count = source?.Count ?? 0;
        var redacted = new JsonArray();
        for (var index = 0; index < count; index++)
        {
            if (source?[index] is JsonObject card
                && InformationModel.IdentityVisibleTo(state, card, viewerPlayerId, zone, ownerPlayerId, index))
            {
                redacted.Add(card.DeepClone());
                continue;
            }

            redacted.Add(HiddenCard(ownerPlayerId, zone, index, source?[index] as JsonObject));
        }

        return redacted;
    }

    private static void RedactCardArray(JsonObject state, JsonArray? cards, int? viewerPlayerId, string zone, int? ownerPlayerId)
    {
        if (cards is null)
        {
            return;
        }

        for (var index = 0; index < cards.Count; index++)
        {
            if (cards[index] is JsonObject card
                && !InformationModel.IdentityVisibleTo(state, card, viewerPlayerId, zone, OwnerId(card, ownerPlayerId), index))
            {
                cards[index] = HiddenCard(OwnerId(card, ownerPlayerId), zone, index, card);
            }
        }
    }

    private static void RedactCardProperty(JsonObject state, JsonObject parent, string propertyName, int? viewerPlayerId, string zone, int ownerPlayerId)
    {
        if (parent[propertyName] is JsonObject card
            && !InformationModel.IdentityVisibleTo(state, card, viewerPlayerId, zone, ownerPlayerId, 0))
        {
            parent[propertyName] = HiddenCard(OwnerId(card, ownerPlayerId), zone, 0, card);
        }
    }

    private static void RedactStackItem(JsonObject state, JsonObject item, int? viewerPlayerId)
    {
        if (InformationModel.IdentityVisibleTo(state, item, viewerPlayerId, "stack", OwnerId(item, null), 0))
        {
            return;
        }

        item["cardId"] = $"hidden-stack-{item["playerId"]?.GetValue<int?>() ?? -1}";
        item["cardName"] = "Hidden card";
    }

    private static void RedactRevealedCards(JsonObject state, int? viewerPlayerId)
    {
        if (state["revealedCards"] is not JsonArray revealedCards)
        {
            return;
        }

        for (var index = 0; index < revealedCards.Count; index++)
        {
            if (revealedCards[index] is not JsonObject entry
                || entry["card"] is not JsonObject card)
            {
                continue;
            }

            var zone = entry["zone"]?.GetValue<string>() ?? "revealed";
            var playerId = entry["playerId"]?.GetValue<int?>();
            if (!InformationModel.IdentityVisibleTo(state, card, viewerPlayerId, zone, playerId, entry["index"]?.GetValue<int?>() ?? index))
            {
                var clone = entry.DeepClone().AsObject();
                clone["card"] = HiddenCard(OwnerId(card, playerId), zone, index, card);
                revealedCards[index] = clone;
            }
        }
    }

    private static void RedactPendingVision(JsonObject state, int? viewerPlayerId)
    {
        if (state["pendingVision"] is not JsonObject pending
            || pending["card"] is not JsonObject card)
        {
            return;
        }

        var playerId = pending["playerId"]?.GetValue<int?>();
        if (viewerPlayerId != playerId)
        {
            pending["card"] = HiddenCard(OwnerId(card, playerId), "vision", 0, card);
        }
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
        Preserve(source, hidden, "controllerId");
        Preserve(source, hidden, "hiddenAtBattlefieldId");
        Preserve(source, hidden, "hiddenTurnNumber");
        Preserve(source, hidden, "revealedToPlayerIds");
        Preserve(source, hidden, "revealedTo");
        Preserve(source, hidden, "information");
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
