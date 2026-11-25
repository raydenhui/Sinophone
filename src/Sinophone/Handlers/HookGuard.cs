using Action = System.Action;

namespace Sinophone.Handlers;

/// <summary>
/// Runs a hook callback so a throw cannot reach the game's frame.
/// </summary>
/// <remarks>
/// Every handler callback is called by the game from inside a frame. An exception there does not
/// stay inside the plugin: it unwinds into the game and can crash the client or leave a window
/// half-written. The rule is that a failed hook leaves the English text and logs, nothing more.
/// The rule lives here so it is tested once, rather than repeated in every handler with no test
/// of its own.
/// </remarks>
internal static class HookGuard
{
    /// <summary>
    /// Runs <paramref name="action"/> and swallows any exception, logging it at warning level.
    /// The message names the surface so a log line says which hook failed.
    /// </summary>
    public static void Run(PluginLog log, string surface, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            log.Warning(ex, "{Surface}: the hook failed; leaving the game text.", surface);
        }
    }

    /// <summary>
    /// Runs <paramref name="func"/> and returns its result, or <paramref name="fallback"/> when
    /// it throws. For a hook that produces a value, such as a translated line.
    /// </summary>
    public static T Run<T>(PluginLog log, string surface, Func<T> func, T fallback)
    {
        try
        {
            return func();
        }
        catch (Exception ex)
        {
            log.Warning(ex, "{Surface}: the hook failed; leaving the game text.", surface);
            return fallback;
        }
    }
}
