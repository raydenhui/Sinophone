namespace Sinophone.Diagnostics;

/// <summary>
/// Says whether this assembly was built with the testing tab's engine code.
/// </summary>
/// <remarks>
/// The switch is <c>SINOPHONE_TESTING</c>, which a Debug build sets and a release build does not.
/// The tests read this constant to check that the engine code really is absent from a release
/// assembly, rather than only hidden at runtime. The harness core is always compiled, so this
/// type is always available.
/// </remarks>
public static class HarnessBuild
{
    /// <summary>True when the engine half of the testing tab is compiled into this assembly.</summary>
    public const bool IsTesting =
#if SINOPHONE_TESTING
        true;
#else
        false;
#endif
}
