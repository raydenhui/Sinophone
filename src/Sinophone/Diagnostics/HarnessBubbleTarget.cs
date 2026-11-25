namespace Sinophone.Diagnostics;

/// <summary>
/// A game object a bubble can be shown above. Plain data, so the testing tab can list targets and
/// remember a pick while the code that starts a bubble stays behind the build switch.
/// </summary>
/// <param name="Name">Name shown in the picker.</param>
/// <param name="Address">Address of the game object, or zero when it cannot be driven.</param>
/// <param name="Kind">What the object is, for the picker label.</param>
public readonly record struct HarnessBubbleTarget(string Name, nint Address, string Kind)
{
    /// <summary>True when the target has a character struct a bubble can be started on.</summary>
    public bool IsUsable => Address != 0;
}
