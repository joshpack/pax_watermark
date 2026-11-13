using Avalonia;
using Avalonia.Headless;
using pxdWatermark.Services;

namespace pxdWatermark;

internal static class Program
{
    public const string AppId = "pxdWatermark";

    /// <summary>Where updates come from: this app's releases (asked at pxdtek.com first, GitHub as the fallback) and its Windows zip (the same asset the pxd Toolkit installs).</summary>
    public const string UpdateRepo = "pxdtek/pxdWatermark";
    public const string UpdateAsset = @"^pxdWatermark-.*-win-x64\.zip$";

    /// <summary>Runs that never update first: the build step.</summary>
    public static readonly string[] NoUpdateArgs = ["--write-icon"];

    [STAThread]
    public static int Main(string[] args)
    {
        // pxd standard rule 6: an installed copy updates itself before it opens, however it was started. A no-op for dev
        // builds (bin\), --self-test, PXD_NO_UPDATE=1, offline or when neither pxdtek.com nor GitHub answers in time; when it updated, the new copy is already starting.
        if (!args.Any(a => NoUpdateArgs.Contains(a, StringComparer.OrdinalIgnoreCase))
            && pxdStandard.pxdUpdate.BeforeStart(UpdateRepo, UpdateAsset, args))
            return 0;

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
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PXDWM_DATA")))
                Environment.SetEnvironmentVariable("PXDWM_DATA", Path.Combine(SelfTest.Dir, "data"));
            pxdStandard.pxdChrome.Configure(AppId, AppSettings.Folder);

            // Headless platform: windows are real Avalonia windows, but no OS window exists, so nothing can appear on screen.
            AppBuilder.Configure<App>()
                .UseSkia()
                .UseHeadless(new Avalonia.Headless.AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
            return SelfTest.RunAndReport();
        }

        // Settings kept under the app's former name move to the new folder before anything reads them.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PXDWM_DATA")))
            Migration.Run(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

        if (!SingleInstance.Claim(SingleInstance.Name(AppId), args))
        {
            return 0;   // already running: it opens the photo (if any) and comes to the front
        }

        pxdStandard.pxdChrome.Configure(AppId, AppSettings.Folder);
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
