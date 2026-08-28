namespace SabrTrader.Pipeline.Indicators.Builtin;

/// <summary>
/// Canonical alert-sound filenames for the built-in indicators.
///
/// <para>These are bare filenames (no path) — the playback sink resolves
/// them against the user's sound library
/// (<c>Documents\SabrTrader\sounds</c>) and then the app-install
/// <c>sounds</c> folder, both of which are seeded with the factory wavs.
/// A filename that resolves to nothing makes the sound channel fall back
/// to a generic system sound, so every default declared here MUST name a
/// file the platform actually ships.</para>
///
/// <para>The originals these indicators were ported from
/// defaulted to <c>Alert2.wav</c> — a stock sound that does
/// NOT exist in the SabrTrader library. Pointing every built-in default at
/// this one constant keeps the choice in a single place and guarantees it
/// stays a shipped file.</para>
/// </summary>
internal static class BuiltinAlertSounds
{
    /// <summary>
    /// Default alert sound for built-in indicators: a neutral, non-directional
    /// chime. Must match a wav shipped in the install/user sound library
    /// (see <c>SabrTrader.Desktop\Services\Alerts\SoundsFolder</c>).
    /// </summary>
    public const string Default = "alert-ding.wav";
}
