using System.Text.Json.Nodes;

namespace riftbound_tcg.Engine.RulesEngine;

public enum InformationPrivacy
{
    Public,
    Private,
    Secret
}

public static class InformationModel
{
    public static InformationPrivacy PrivacyForZone(string zone) =>
        zone switch
        {
            "deck" or "rune-deck" => InformationPrivacy.Secret,
            "hand" or "battlefield-hidden" => InformationPrivacy.Private,
            _ => InformationPrivacy.Public
        };

    public static bool IdentityVisibleTo(
        JsonObject state,
        JsonObject card,
        int? viewerPlayerId,
        string zone,
        int? zoneOwnerPlayerId,
        int zoneIndex)
    {
        if (IsPublic(card))
        {
            return true;
        }

        if (IsActivelyRevealed(state, card, zone, zoneOwnerPlayerId, zoneIndex, viewerPlayerId))
        {
            return true;
        }

        if (viewerPlayerId is null)
        {
            return Privacy(card, zone) == InformationPrivacy.Public && !IsFacedown(card);
        }

        if (IsExplicitlyVisibleTo(card, viewerPlayerId.Value))
        {
            return true;
        }

        if (IsFacedown(card))
        {
            return FacedownIdentityKnownTo(card, viewerPlayerId.Value);
        }

        return Privacy(card, zone) switch
        {
            InformationPrivacy.Public => true,
            InformationPrivacy.Private => PrivateInformationHolder(card, zoneOwnerPlayerId) == viewerPlayerId.Value,
            InformationPrivacy.Secret => false,
            _ => false
        };
    }

    public static void MarkHiddenFacedown(JsonObject card, int controllerPlayerId, int turnNumber, string battlefieldId)
    {
        card["ownerId"] = card["ownerId"]?.DeepClone() ?? controllerPlayerId;
        card["controllerId"] = controllerPlayerId;
        card["hiddenAtBattlefieldId"] = battlefieldId;
        card["hiddenTurnNumber"] = turnNumber;
        card["facedown"] = true;
        card["isFaceDown"] = true;
        card["rulesTextActive"] = false;

        var information = EnsureInformation(card);
        information["privacy"] = "private";
        information["controllerId"] = controllerPlayerId;
        information["ownerVisible"] = true;
        AddPlayerId(information, "visibleToPlayerIds", controllerPlayerId);
        AddPlayerId(information, "faceDownIdentityKnownToPlayerIds", controllerPlayerId);
        AppendHistory(card, "hidden", controllerPlayerId, turnNumber);
    }

    public static void MarkFacedown(JsonObject card, int controllerPlayerId, int turnNumber)
    {
        card["isFaceDown"] = true;
        card["facedown"] = true;
        card["rulesTextActive"] = false;

        var information = EnsureInformation(card);
        information["privacy"] = "private";
        information["controllerId"] = controllerPlayerId;
        information["ownerVisible"] = true;
        AddPlayerId(information, "visibleToPlayerIds", controllerPlayerId);
        AddPlayerId(information, "faceDownIdentityKnownToPlayerIds", controllerPlayerId);
        AppendHistory(card, "facedown", controllerPlayerId, turnNumber);
    }

    public static void MarkFaceup(JsonObject card, int turnNumber)
    {
        card["isFaceDown"] = false;
        card["facedown"] = false;
        card["rulesTextActive"] = true;

        var information = EnsureInformation(card);
        information["privacy"] = "public";
        AppendHistory(card, "faceup", null, turnNumber);
    }

    private static InformationPrivacy Privacy(JsonObject card, string zone)
    {
        var privacy = card["information"]?["privacy"]?.GetValue<string>()
            ?? card["privacy"]?.GetValue<string>();
        return privacy?.ToLowerInvariant() switch
        {
            "public" => InformationPrivacy.Public,
            "private" => InformationPrivacy.Private,
            "secret" => InformationPrivacy.Secret,
            _ => PrivacyForZone(zone)
        };
    }

    private static bool IsPublic(JsonObject card) =>
        ReadBool(card, "public")
        || ReadBool(card, "revealed")
        || string.Equals(card["information"]?["privacy"]?.GetValue<string>(), "public", StringComparison.OrdinalIgnoreCase);

    private static bool IsFacedown(JsonObject card) =>
        ReadBool(card, "faceDown")
        || ReadBool(card, "facedown")
        || ReadBool(card, "isFaceDown")
        || ReadBool(card, "hidden");

    private static bool IsExplicitlyVisibleTo(JsonObject card, int viewerPlayerId) =>
        ContainsPlayerId(card["revealedToPlayerIds"], viewerPlayerId)
        || ContainsPlayerId(card["revealedTo"], viewerPlayerId)
        || ContainsPlayerId(card["visibleToPlayerIds"], viewerPlayerId)
        || ContainsPlayerId(card["visibleTo"], viewerPlayerId)
        || ContainsPlayerId(card["knownToPlayerIds"], viewerPlayerId)
        || ContainsPlayerId(card["information"]?["revealedToPlayerIds"], viewerPlayerId)
        || ContainsPlayerId(card["information"]?["visibleToPlayerIds"], viewerPlayerId)
        || ContainsPlayerId(card["information"]?["knownToPlayerIds"], viewerPlayerId);

    private static bool FacedownIdentityKnownTo(JsonObject card, int viewerPlayerId)
    {
        var controller = card["controllerId"]?.GetValue<int?>()
            ?? card["information"]?["controllerId"]?.GetValue<int?>()
            ?? card["ownerId"]?.GetValue<int?>()
            ?? card["owner"]?.GetValue<int?>()
            ?? card["ownerPlayerId"]?.GetValue<int?>();
        return controller == viewerPlayerId
            || ContainsPlayerId(card["faceDownIdentityKnownToPlayerIds"], viewerPlayerId)
            || ContainsPlayerId(card["facedownIdentityKnownToPlayerIds"], viewerPlayerId)
            || ContainsPlayerId(card["information"]?["faceDownIdentityKnownToPlayerIds"], viewerPlayerId)
            || ContainsPlayerId(card["information"]?["facedownIdentityKnownToPlayerIds"], viewerPlayerId)
            || ContainsPlayerId(card["information"]?["visibleToPlayerIds"], viewerPlayerId);
    }

    private static int? PrivateInformationHolder(JsonObject card, int? zoneOwnerPlayerId) =>
        card["controllerId"]?.GetValue<int?>()
        ?? card["information"]?["controllerId"]?.GetValue<int?>()
        ?? card["ownerId"]?.GetValue<int?>()
        ?? card["owner"]?.GetValue<int?>()
        ?? card["ownerPlayerId"]?.GetValue<int?>()
        ?? zoneOwnerPlayerId;

    private static bool IsActivelyRevealed(
        JsonObject state,
        JsonObject card,
        string zone,
        int? zoneOwnerPlayerId,
        int zoneIndex,
        int? viewerPlayerId)
    {
        if (state["revealedCards"] is not JsonArray revealedCards)
        {
            return false;
        }

        return revealedCards
            .Select(node => node?.AsObject())
            .Any(entry => entry is not null
                && RevealedEntryVisibleTo(entry, viewerPlayerId)
                && RevealedEntryMatches(entry, card, zone, zoneOwnerPlayerId, zoneIndex));
    }

    private static bool RevealedEntryVisibleTo(JsonObject entry, int? viewerPlayerId)
    {
        if (viewerPlayerId is null)
        {
            return entry["revealedToPlayerIds"] is null && entry["visibleToPlayerIds"] is null;
        }

        return (entry["revealedToPlayerIds"] is null && entry["visibleToPlayerIds"] is null)
            || ContainsPlayerId(entry["revealedToPlayerIds"], viewerPlayerId.Value)
            || ContainsPlayerId(entry["visibleToPlayerIds"], viewerPlayerId.Value);
    }

    private static bool RevealedEntryMatches(JsonObject entry, JsonObject card, string zone, int? zoneOwnerPlayerId, int zoneIndex)
    {
        var entryZone = entry["zone"]?.GetValue<string>();
        var entryIndex = entry["index"]?.GetValue<int?>();
        var entryPlayerId = entry["playerId"]?.GetValue<int?>();
        if (string.Equals(entryZone, zone, StringComparison.OrdinalIgnoreCase)
            && entryIndex == zoneIndex
            && (entryPlayerId is null || entryPlayerId == zoneOwnerPlayerId))
        {
            return true;
        }

        var revealedCard = entry["card"] as JsonObject;
        return SameIdentity(revealedCard, card);
    }

    private static bool SameIdentity(JsonObject? left, JsonObject right)
    {
        if (left is null)
        {
            return false;
        }

        foreach (var key in new[] { "uid", "id" })
        {
            var leftValue = left[key]?.GetValue<string>();
            var rightValue = right[key]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(leftValue) && leftValue == rightValue)
            {
                return true;
            }
        }

        return false;
    }

    private static JsonObject EnsureInformation(JsonObject card)
    {
        if (card["information"] is JsonObject information)
        {
            return information;
        }

        information = new JsonObject();
        card["information"] = information;
        return information;
    }

    private static void AppendHistory(JsonObject card, string action, int? playerId, int turnNumber)
    {
        var information = EnsureInformation(card);
        var history = information["visibilityHistory"] as JsonArray ?? new JsonArray();
        information["visibilityHistory"] = history;
        history.Add(new JsonObject
        {
            ["action"] = action,
            ["playerId"] = playerId,
            ["turnNumber"] = turnNumber
        });
    }

    private static void AddPlayerId(JsonObject obj, string key, int playerId)
    {
        var array = obj[key] as JsonArray ?? new JsonArray();
        obj[key] = array;
        if (!array.Any(node => MatchesPlayerId(node, playerId)))
        {
            array.Add(playerId);
        }
    }

    private static bool ContainsPlayerId(JsonNode? node, int viewerPlayerId) =>
        node is JsonArray array && array.Any(item => MatchesPlayerId(item, viewerPlayerId));

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

    private static bool ReadBool(JsonObject obj, string key) =>
        obj[key]?.GetValue<bool?>() == true;
}
