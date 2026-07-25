using System.Windows;
using System.IO;
using DQOPR.NetPulse.App.Smoke;

namespace DQOPR.NetPulse.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            ReportUnhandledException(args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => ReportUnhandledException((Exception)args.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            ReportUnhandledException(args.Exception);
            args.SetObserved();
        };

        var options = SmokeOptions.Parse(e.Args);
        var window = new MainWindow(options.DatabasePath, options.Enabled ? options.OutputDirectory : null, interactivePrompts: !options.Enabled);
        MainWindow = window;
        window.Show();

        if (options.Enabled)
        {
            window.ContentRendered += async (_, _) =>
            {
                try
                {
                    await SmokeRunner.RunAsync(window, options).ConfigureAwait(true);
                    Shutdown(0);
                }
                catch (Exception ex)
                {
                    Directory.CreateDirectory(options.OutputDirectory);
                    File.WriteAllText(Path.Combine(options.OutputDirectory, "smoke-error.txt"), ex.ToString());
                    Shutdown(1);
                }
            };
        }
    }

    private static void ReportUnhandledException(Exception exception)
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DQOPR NetPulse", "logs");
        Directory.CreateDirectory(folder);
        File.AppendAllText(Path.Combine(folder, "netpulse-errors.log"), $"{DateTimeOffset.UtcNow:O} {exception}{Environment.NewLine}");

        if (Current?.MainWindow?.IsVisible == true)
        {
            MessageBox.Show(Current.MainWindow, exception.Message, "DQOPR NetPulse error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
