using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace AIHotDesktop;

public partial class App : Application
{
    private static readonly string StartupLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AIHotDesktop",
        "startup.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += App_DispatcherUnhandledException;

        try
        {
            WriteStartupLog("Creating main window.");
            var qaItemCount = ParseQaItemCount(e.Args);
            var window = new MainWindow(qaItemCount);
            if (e.Args.Contains("--qa-window", StringComparer.OrdinalIgnoreCase))
            {
                window.ShowInTaskbar = true;
            }
            MainWindow = window;
            window.Show();
            WriteStartupLog("Main window shown.");
        }
        catch (Exception exception)
        {
            WriteStartupLog($"Startup failure:{Environment.NewLine}{exception}");
            MessageBox.Show(
                $"AI HOT Desktop 启动失败。{Environment.NewLine}{Environment.NewLine}"
                + $"诊断日志：{StartupLogPath}",
                "AI HOT Desktop",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    private static int? ParseQaItemCount(IEnumerable<string> arguments)
    {
        const string prefix = "--qa-items=";
        var raw = arguments.FirstOrDefault(argument =>
            argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        return raw is not null
            && int.TryParse(raw[prefix.Length..], out var count)
            && count is >= 0 and <= 100
                ? count
                : null;
    }

    private void App_DispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        WriteStartupLog($"Unhandled UI exception:{Environment.NewLine}{e.Exception}");
    }

    private static void WriteStartupLog(string message)
    {
        try
        {
            var directory = Path.GetDirectoryName(StartupLogPath)!;
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                StartupLogPath,
                $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Diagnostics must never become a second startup failure.
        }
    }
}
