namespace X3DCcdOptimizer.Data;

/// <summary>
/// Curated list of common background apps for autocomplete in the Process Rules tab.
/// Used only for the manual-entry TextBox when apps are not currently running.
/// </summary>
public static class BackgroundAppSuggestions
{
    public static readonly IReadOnlyList<(string DisplayName, string Exe)> Apps =
    [
        // Browsers
        ("Google Chrome", "chrome.exe"),
        ("Mozilla Firefox", "firefox.exe"),
        ("Microsoft Edge", "msedge.exe"),
        ("Brave Browser", "brave.exe"),

        // Communication
        ("Discord", "discord.exe"),
        ("Slack", "slack.exe"),
        ("Microsoft Teams", "ms-teams.exe"),

        // Streaming / Media
        ("OBS Studio", "obs64.exe"),
        ("Spotify", "spotify.exe"),

        // Game Launchers
        ("Steam", "steam.exe"),
        ("Epic Games Launcher", "EpicGamesLauncher.exe"),
        ("GOG Galaxy", "GalaxyClient.exe"),
        ("Battle.net", "Battle.net.exe"),

        // Utilities
        ("Wallpaper Engine", "webwallpaper32.exe"),
        ("Logitech G Hub", "lghub.exe"),
        ("Razer Synapse", "RazerCentralService.exe"),
        ("MSI Afterburner", "MSIAfterburner.exe"),
        ("HWiNFO", "HWiNFO64.exe"),
    ];
}
