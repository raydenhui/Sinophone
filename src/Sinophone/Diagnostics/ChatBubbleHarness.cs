#if SINOPHONE_TESTING
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

// Lumina also has a sheet called Character; the harness only wants the game struct.
using GameCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;

namespace Sinophone.Diagnostics;

/// <summary>
/// Shows a chat bubble above a picked character and reads the result back.
/// </summary>
/// <remarks>
/// A yell bubble is started through <see cref="NpcYellBalloon.OpenBalloon"/>, which
/// <see cref="Handlers.ChatBubbleHandler"/> already detours. Calling the member function runs
/// that detour first, so an injected English line goes through the real lookup and the game
/// sizes the bubble for the Chinese. Nothing here writes a node.
/// <para>
/// This drives the yell balloon only. A default balloon (<c>Character.Balloon</c>) has no start
/// function and its text node is filled by the game itself, so it is not covered until an
/// experiment shows it can be started.
/// </para>
/// </remarks>
public static unsafe class ChatBubbleHarness
{
    /// <summary>
    /// Starts a bubble with <paramref name="text"/> above <paramref name="target"/>. A zero
    /// duration holds the bubble open until <see cref="Close"/> is called, which is what a
    /// screenshot needs.
    /// </summary>
    /// <returns>False with a reason when the target cannot be driven.</returns>
    public static bool Open(HarnessBubbleTarget target, string text, out string reason, float duration = 0f)
    {
        if (!target.IsUsable)
        {
            reason = "The picked object has no character address.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            reason = "The case has no text.";
            return false;
        }

        try
        {
            var character = (GameCharacter*)target.Address;
            character->YellBalloon.OpenBalloon(
                text,
                duration,
                softOpen: true,
                openDelay: 0f,
                printToLog: false,
                reducedCloseChecks: false,
                ignoreRangeCheck: true,
                parentBone: 0);

            reason = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            reason = $"Could not start a bubble: {ex.Message}";
            return false;
        }
    }

    /// <summary>Closes the bubble on <paramref name="target"/>.</summary>
    public static void Close(HarnessBubbleTarget target)
    {
        if (!target.IsUsable)
            return;

        try
        {
            ((GameCharacter*)target.Address)->YellBalloon.CloseBalloon();
        }
        catch
        {
            // A target that left the zone is not a case failure.
        }
    }

    /// <summary>
    /// The text the bubble currently holds, and the state it is in. Empty when the bubble is
    /// inactive, which is what the game reports before it opens.
    /// </summary>
    public static (string Text, NpcYellBalloonState State) Read(HarnessBubbleTarget target)
    {
        if (!target.IsUsable)
            return (string.Empty, NpcYellBalloonState.Inactive);

        try
        {
            var balloon = &((GameCharacter*)target.Address)->YellBalloon;
            return balloon->State == NpcYellBalloonState.Inactive
                ? (string.Empty, balloon->State)
                : (balloon->Text.ToString(), balloon->State);
        }
        catch
        {
            return (string.Empty, NpcYellBalloonState.Inactive);
        }
    }

    /// <summary>
    /// The objects in the zone a bubble can be shown above, nearest first. The player is
    /// included, because the struct exists on the player character too and testing a bubble
    /// without walking to an NPC is quicker.
    /// </summary>
    public static List<HarnessBubbleTarget> VisibleTargets(IObjectTable objectTable, int limit = 40)
    {
        var targets = new List<HarnessBubbleTarget>(limit);

        var player = objectTable.LocalPlayer;
        if (player is not null)
            targets.Add(new HarnessBubbleTarget(player.Name.TextValue, player.Address, "player"));

        foreach (var obj in objectTable)
        {
            if (targets.Count >= limit)
                break;

            if (obj is null || obj.Address == 0)
                continue;

            var name = obj.Name.TextValue;
            if (name.Length == 0)
                continue;

            if (player is not null && obj.Address == player.Address)
                continue;

            targets.Add(new HarnessBubbleTarget(name, obj.Address, obj.ObjectKind.ToString()));
        }

        return targets;
    }
}
#endif
