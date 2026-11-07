using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using PaxWatermark.Imaging;
using PaxWatermark.Services;
using PaxWatermark.Views;

namespace PaxWatermark;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
        {
            if (SelfTest.Dir is { } dir)
            {
                lifetime.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;
                var report = new System.Text.StringBuilder();
                bool ok;
                try
                {
                    ok = SelfTest.Run(report);
                }
                catch (Exception ex)
                {
                    report.AppendLine("CRASH: " + ex);
                    ok = false;
                }

                report.AppendLine(ok ? "RESULT: PASS" : "RESULT: FAIL");
                File.WriteAllText(Path.Combine(dir, "report.txt"), report.ToString());
                Dispatcher.UIThread.Post(() => lifetime.Shutdown(ok ? 0 : 1));
                base.OnFrameworkInitializationCompleted();
                return;
            }

            var window = new MainWindow(AppSettings.Load());
            lifetime.MainWindow = window;

            // "Open with" / a photo dropped on the exe: open it straight away.
            var photo = lifetime.Args?.FirstOrDefault(a => File.Exists(a) && Stamper.IsImage(a));
            window.Opened += async (_, _) =>
            {
                await window.RestoreLastWatermark();
                if (photo is not null)
                    await window.OpenPhoto(photo);
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
