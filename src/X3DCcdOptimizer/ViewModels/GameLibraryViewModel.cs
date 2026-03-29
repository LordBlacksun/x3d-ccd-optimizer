using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Serilog;
using X3DCcdOptimizer.Core;
using X3DCcdOptimizer.Models;

namespace X3DCcdOptimizer.ViewModels;

public class GameLibraryItemViewModel : ViewModelBase
{
    private ImageSource? _artworkImage;

    public string DisplayName { get; }
    public string ExeName { get; }
    public string Source { get; }
    public string SourceBadge { get; }
    public SolidColorBrush SourceColor { get; }
    public int? SteamAppId { get; }

    public ImageSource? ArtworkImage
    {
        get => _artworkImage;
        set => SetProperty(ref _artworkImage, value);
    }

    public bool HasArtwork => _artworkImage != null;

    public GameLibraryItemViewModel(string displayName, string exeName, string source, int? steamAppId = null,
        string? artworkPath = null)
    {
        DisplayName = displayName;
        ExeName = exeName;
        Source = source;
        SteamAppId = steamAppId;

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
    private string _totalCountText = "";

    public ObservableCollection<GameLibraryItemViewModel> Games { get; } = [];

    public string TotalCountText
    {
        get => _totalCountText;
        set => SetProperty(ref _totalCountText, value);
    }

    public GameLibraryViewModel(GameDatabase gameDb)
    {
        _gameDb = gameDb;
        Refresh();
    }

    public void Refresh()
    {
        Games.Clear();

        var seenExes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var steamCount = 0;
        var epicCount = 0;
        var gogCount = 0;

        // Load scanned games from LiteDB — deduplicate by exe name and display name
        foreach (var game in _gameDb.GetAllGames().OrderBy(g => g.DisplayName))
        {
            if (!seenExes.Add(game.ProcessName))
                continue;
            if (!seenNames.Add(game.DisplayName))
                continue;

            Games.Add(new GameLibraryItemViewModel(
                game.DisplayName, game.ProcessName, game.Source,
                game.SteamAppId, game.ArtworkPath));

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

        TotalCountText = $"{total} games ({string.Join(", ", parts)})";
    }
}
