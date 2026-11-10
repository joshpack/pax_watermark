using System.Text.Json;
using pxdWatermark.Imaging;

namespace pxdWatermark.Services;

/// <summary>What the app remembers between runs, saved as JSON in the per-user app-data folder.</summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string? PhotoFolder { get; set; }
    public string? WatermarkFolder { get; set; }
    public string? SaveFolder { get; set; }
    public string? BatchFolder { get; set; }

    /// <summary>The last watermark image, opened again at start-up if it still exists.</summary>
    public string? WatermarkFile { get; set; }

    /// <summary>Watermark width as a fraction of the photo's width.</summary>
    public double Size { get; set; } = Placement.Default.Size;
    public double Opacity { get; set; } = Placement.Default.Opacity;
    public double Rotation { get; set; } = Placement.Default.Rotation;
    public double CenterX { get; set; } = Placement.Default.CenterX;
    public double CenterY { get; set; } = Placement.Default.CenterY;

    public bool FitToWindow { get; set; } = true;

    /// <summary>%APPDATA%\pxdWatermark, or the folder in PXDWM_DATA (used by tests to keep the real settings untouched).</summary>
    public static string Folder =>
        Environment.GetEnvironmentVariable("PXDWM_DATA") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "pxdWatermark");

    public static string FilePath => Path.Combine(Folder, "settings.json");

    [System.Text.Json.Serialization.JsonIgnore]
    public Placement Placement
    {
        get => new(Math.Clamp(CenterX, 0, 1), Math.Clamp(CenterY, 0, 1), Math.Clamp(Size, 0.02, 1), Math.Clamp(Opacity, 0, 1), Math.Clamp(Rotation, -180, 180));
        set => (CenterX, CenterY, Size, Opacity, Rotation) = (value.CenterX, value.CenterY, value.Size, value.Opacity, value.Rotation);
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch
        {
            /* a damaged file just means defaults */
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch
        {
            /* best effort */
        }
    }
}
