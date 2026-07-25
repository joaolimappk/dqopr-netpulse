using System.Windows;
using System.IO;
using DQOPR.NetPulse.App.Smoke;

namespace DQOPR.NetPulse.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var options = SmokeOptions.Parse(e.Args);
        var window = new MainWindow(options.DatabasePath);
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
}
