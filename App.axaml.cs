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
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        Styles.Add(PaxStandard.PaxChrome.Styles());   // caption-button hover colours of the PAX title bar
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
        {
            var window = new MainWindow(AppSettings.Load());
            PaxStandard.PaxChrome.RememberPlacement(window, "main");
            lifetime.MainWindow = window;

            var restoreTo = window.WindowState;
            window.PropertyChanged += (_, e) =>
            {
                if (e.Property == Avalonia.Controls.Window.WindowStateProperty && window.WindowState != Avalonia.Controls.WindowState.Minimized)
                    restoreTo = window.WindowState;
            };

            // Started again: open the photo it was given (if any) and bring this window to the front.
            var listener = SingleInstance.Listen(SingleInstance.Name(Program.AppId), args => Dispatcher.UIThread.Post(async () =>
            {
                if (window.WindowState == Avalonia.Controls.WindowState.Minimized)
                    window.WindowState = restoreTo;
                window.Activate();
                var next = args.FirstOrDefault(a => File.Exists(a) && Stamper.IsImage(a));
                if (next is not null)
                    await window.OpenPhoto(next);
            }));
            lifetime.Exit += (_, _) => listener.Dispose();

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
