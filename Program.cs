using Avalonia;
using Avalonia.Headless;
using PaxWatermark.Services;

namespace PaxWatermark;

internal static class Program
{
    public const string AppId = "PaxWatermark";

    [STAThread]
    public static int Main(string[] args)
    {
        // Build step: "--write-icon <file.ico|file.png>" renders the app icon and exits, no window.
        if (args is ["--write-icon", var iconPath])
        {
            BuildAvaloniaApp().SetupWithoutStarting();
            if (iconPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                AppIcon.WritePng(iconPath);
            else
                AppIcon.WriteIco(iconPath);
            return 0;
        }

        var test = Array.FindIndex(args, a => a.Equals("--self-test", StringComparison.OrdinalIgnoreCase));
        if (test >= 0)
        {
            if (test + 1 >= args.Length)
                return 2;
            SelfTest.Dir = Path.GetFullPath(args[test + 1]);
            Directory.CreateDirectory(SelfTest.Dir);
            // Keep the real settings out of it unless the caller chose a folder.
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PAXWM_DATA")))
                Environment.SetEnvironmentVariable("PAXWM_DATA", Path.Combine(SelfTest.Dir, "data"));
            PaxStandard.PaxChrome.Configure(AppId, AppSettings.Folder);

            // Headless platform: windows are real Avalonia windows, but no OS window exists, so nothing can appear on screen.
            AppBuilder.Configure<App>()
                .UseSkia()
                .UseHeadless(new Avalonia.Headless.AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
            return SelfTest.RunAndReport();
        }

        if (!SingleInstance.Claim(SingleInstance.Name(AppId), args))
        {
            return 0;   // already running: it opens the photo (if any) and comes to the front
        }

        PaxStandard.PaxChrome.Configure(AppId, AppSettings.Folder);
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
