using System.IO;
using Serilog;
using Serilog.Events;

namespace X3DCcdInspector.Logging;

public static class AppLogger
{
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "X3DCCDInspector", "logs");

    private static readonly string LogPath = Path.Combine(LogDir, "x3d-optimizer.log");

    public static void Initialize(string level = "Information")
    {
        Directory.CreateDirectory(LogDir);

        // Fresh log each launch — previous log saved as .prev for reference
        try
        {
            var prevPath = LogPath + ".prev";
            if (File.Exists(LogPath))
                File.Move(LogPath, prevPath, overwrite: true);
        }
        catch { }

        var logLevel = Enum.TryParse<LogEventLevel>(level, true, out var parsed)
            ? parsed
            : LogEventLevel.Information;

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(logLevel)
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                LogPath,
                fileSizeLimitBytes: 10 * 1024 * 1024,
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    public static void Shutdown()
    {
        Log.CloseAndFlush();
    }
}
