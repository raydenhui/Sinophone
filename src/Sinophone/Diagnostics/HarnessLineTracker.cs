namespace Sinophone.Diagnostics;

/// <summary>
/// Follows one line while a surface draws it: did the translation appear, and did the game
/// put the English back afterwards?
/// </summary>
/// <remarks>
/// This is the part of a surface driver that can get the answer wrong, so it is kept apart
/// from the engine code and tested on its own. The rule the Talk window taught us: a
/// translation that appears and then goes back to the English line is a revert, and a revert is
/// a failure even when the final text looks fine.
/// </remarks>
public sealed class HarnessLineTracker
{
    private string? _source;
    private bool _sawTranslation;
    private int _reverts;

    /// <summary>Times the surface went back to the source line after showing a translation.</summary>
    public int Reverts => _reverts;

    /// <summary>True once a text other than the source line was drawn.</summary>
    public bool SawTranslation => _sawTranslation;

    /// <summary>Starts following a new line. <paramref name="source"/> is what the surface was given.</summary>
    public void Reset(string? source)
    {
        _source = source;
        _sawTranslation = false;
        _reverts = 0;
    }

    /// <summary>
    /// Records one look at the drawn text. A text that differs from the source is a translation;
    /// the source line after a translation is a revert.
    /// </summary>
    public void Observe(string? drawn)
    {
        if (string.IsNullOrEmpty(drawn))
            return;

        if (!string.Equals(drawn, _source, StringComparison.Ordinal))
        {
            _sawTranslation = true;
            return;
        }

        if (_sawTranslation)
            _reverts++;
    }

    /// <summary>True when the drawn text is a translation of the source line.</summary>
    public bool IsTranslated(string? drawn)
        => !string.IsNullOrEmpty(drawn) && !string.Equals(drawn, _source, StringComparison.Ordinal);
}
