using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Serilog;
using X3DCcdInspector.Core;
using X3DCcdInspector.Models;

namespace X3DCcdInspector.ViewModels;

public class GameLibraryItemViewModel : ViewModelBase
{
    private ImageSource? _artworkImage;
    private string _ccdPreference;
    private string _fallbackCcdPin;
    private readonly Action<GameLibraryItemViewModel, string, bool>? _onPreferenceChanged;

    public string DisplayName { get; }
    public string ExeName { get; }
    public string Source { get; }
    public string SourceBadge { get; }
    public SolidColorBrush SourceColor { get; }
    public int? SteamAppId { get; }
    public string InitialLetter { get; }
    public bool IsDriverAvailable { get; }

    public ImageSource? ArtworkImage
    {
        get => _artworkImage;
        set => SetProperty(ref _artworkImage, value);
    }

    public bool HasArtwork => _artworkImage != null;

    /// <summary>
    /// CCD Preference via AMD driver profile (only active when driver available).
    /// </summary>
    public string CcdPreference
    {
        get => _ccdPreference;
        set
        {
            if (SetProperty(ref _ccdPreference, value))
                _onPreferenceChanged?.Invoke(this, value, true);
        }
    }

    /// <summary>
    /// Fallback affinity pin (only active when driver NOT available).
    /// Values: "None", "VCache", "Frequency"
    /// </summary>
    public string FallbackCcdPin
    {
        get => _fallbackCcdPin;
        set
        {
            if (SetProperty(ref _fallbackCcdPin, value))
                _onPreferenceChanged?.Invoke(this, value, false);
        }
    }

    public GameLibraryItemViewModel(string displayName, string exeName, string source, int? steamAppId = null,
        string? artworkPath = null, string ccdPreference = "Auto", string fallbackCcdPin = "None",
        bool isDriverAvailable = false,
        Action<GameLibraryItemViewModel, string, bool>? onPreferenceChanged = null)
    {
        DisplayName = displayName;
        ExeName = exeName;
        Source = source;
        SteamAppId = steamAppId;
        _ccdPreference = ccdPreference;
        _fallbackCcdPin = fallbackCcdPin;
        IsDriverAvailable = isDriverAvailable;
        _onPreferenceChanged = onPreferenceChanged;
        InitialLetter = displayName.Length > 0 ? char.ToUpperInvariant(displayName[0]).ToString() : "?";

        SourceBadge = source switch
        {
            "steam" => "Steam",
            "epic" => "Epic",
            "gog" => "GOG",
            _ => source
        };

        SourceColor = source switch
        {
            "steam" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x17, 0x1A, 0x21)),
            "epic" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2A, 0x2A, 0x2A)),
            "gog" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x43, 0x1D, 0x6B)),
            _ => new SolidColorBrush(Colors.Gray)
        };

        if (artworkPath != null && File.Exists(artworkPath))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(artworkPath, UriKind.Absolute);
                bmp.DecodePixelWidth = 45;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                _artworkImage = bmp;
            }
            catch { }
        }
    }

    private static SolidColorBrush FindBrush(string key)
    {
        return Application.Current?.TryFindResource(key) as SolidColorBrush
            ?? new SolidColorBrush(Colors.Gray);
    }
}

public class GameLibraryViewModel : ViewModelBase
{
    private readonly GameDatabase _gameDb;
    private readonly HashSet<string> _excludedProcesses;
    private string _totalCountText = "";

    /// <summary>
    /// Fired when a game's CCD preference is changed (driver-based).
    /// Args: (exeName, displayName, newPreference)
    /// </summary>
    public event Action<string, string, string>? PreferenceChanged;

    /// <summary>
    /// Fired when a game's fallback affinity pin is changed (no driver).
    /// Args: (exeName, displayName, newFallbackPin)
    /// </summary>
    public event Action<string, string, string>? FallbackPinChanged;

    public ObservableCollection<GameLibraryItemViewModel> Games { get; } = [];

    public string TotalCountText
    {
        get => _totalCountText;
        set => SetProperty(ref _totalCountText, value);
    }

    public GameLibraryViewModel(GameDatabase gameDb, IEnumerable<string>? excludedProcesses = null)
    {
        _gameDb = gameDb;
        _excludedProcesses = new HashSet<string>(excludedProcesses ?? [], StringComparer.OrdinalIgnoreCase);
        Refresh();
    }

    public void Refresh()
    {
        Games.Clear();

        var seenExes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var steamCount = 0;
        var epicCount = 0;
        var gogCount = 0;
        var driverAvailable = VCacheDriverManager.IsDriverAvailable;

        foreach (var game in _gameDb.GetAllGames().OrderBy(g => g.DisplayName))
        {
            // Skip excluded processes — these aren't games
            if (_excludedProcesses.Contains(game.ProcessName))
                continue;

            // Deduplicate by exe name only
            if (!seenExes.Add(game.ProcessName))
                continue;

            Games.Add(new GameLibraryItemViewModel(
                game.DisplayName, game.ProcessName, game.Source,
                game.SteamAppId, game.ArtworkPath, game.CcdPreference,
                game.FallbackCcdPin, driverAvailable, OnItemPreferenceChanged));

            switch (game.Source)
            {
                case "steam": steamCount++; break;
                case "epic": epicCount++; break;
                case "gog": gogCount++; break;
            }
        }

        var total = Games.Count;
        var parts = new List<string>();
        if (steamCount > 0) parts.Add($"{steamCount} Steam");
        if (epicCount > 0) parts.Add($"{epicCount} Epic");
        if (gogCount > 0) parts.Add($"{gogCount} GOG");

        TotalCountText = total > 0
            ? $"{total} games ({string.Join(", ", parts)})"
            : "No games found yet. Scan your libraries from Settings \u2192 Detection.";
    }

    private void OnItemPreferenceChanged(GameLibraryItemViewModel item, string newValue, bool isDriverBased)
    {
        if (isDriverBased)
        {
            // Driver-based CCD preference (Phase 4)
            _gameDb.UpdateCcdPreference(item.ExeName, newValue);

            var profileName = VCacheDriverManager.SanitizeProfileName(item.DisplayName);
            if (newValue == "Auto")
                VCacheDriverManager.RemoveAppProfile(profileName);
            else
            {
                var type = newValue == "VCache" ? 1 : 0;
                VCacheDriverManager.SetAppProfile(profileName, item.ExeName, type);
            }

            PreferenceChanged?.Invoke(item.ExeName, item.DisplayName, newValue);
        }
        else
        {
            // Fallback affinity pin (Phase 5)
            _gameDb.UpdateFallbackPin(item.ExeName, newValue);
            FallbackPinChanged?.Invoke(item.ExeName, item.DisplayName, newValue);
        }
    }
}
