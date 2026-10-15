using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Tweakora.Core.Safety;

namespace Tweakora.App.Services;

public sealed class UiLogMessage
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public LogEventLevel Level { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed class UiLogSink : ILogEventSink
{
    private static readonly Lazy<UiLogSink> _instance = new(() => new UiLogSink());
    public static UiLogSink Instance => _instance.Value;

    public ObservableCollection<UiLogMessage> Messages { get; } = new();

    public void Emit(LogEvent logEvent)
    {
        string message = logEvent.RenderMessage();
        UiLogMessage entry = new()
        {
            Timestamp = logEvent.Timestamp.LocalDateTime,
            Level = logEvent.Level,
            Message = message
        };

        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                AddMessage(entry);
            });
        }
        else
        {
            AddMessage(entry);
        }
    }

    private void AddMessage(UiLogMessage entry)
    {
        if (Messages.Count > 500)
        {
            Messages.RemoveAt(0);
        }
        Messages.Add(entry);
    }
}

public static class LoggingConfiguration
{
    public static void InitializeLogging(string? logDir = null)
    {
        logDir ??= WinVeloStoragePaths.GetLogsDirectory(TestModeManager.IsTestMode);
        Directory.CreateDirectory(logDir);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                Path.Combine(logDir, "winvelo-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
            )
            .WriteTo.Sink(UiLogSink.Instance)
            .CreateLogger();

        Log.Information("WinVelo logger initialized. Log directory: {LogDir}", logDir);
    }
}
