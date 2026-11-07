using SkiaSharp;

namespace PaxWatermark.Imaging;

public sealed record BatchResult(string OutputFolder, int Done, IReadOnlyList<string> Failed, int Skipped);

/// <summary>Stamps every image in a folder with the same relative placement, into a "watermarked" subfolder.</summary>
public static class Batch
{
    public const string OutputFolderName = "watermarked";

    /// <summary>The images a batch over <paramref name="folder"/> would stamp (top level only, sorted by name).</summary>
    public static List<string> FilesIn(string folder, string? skip = null) =>
        Directory.EnumerateFiles(folder)
            .Where(Stamper.IsImage)
            .Where(f => skip is null || !string.Equals(Path.GetFullPath(f), Path.GetFullPath(skip), StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Where the stamped copy of <paramref name="source"/> goes: same name, a format that matches its extension.</summary>
    public static string OutputPathFor(string source, string outputFolder) =>
        Path.Combine(outputFolder, Path.GetFileNameWithoutExtension(source) + Stamper.OutputExtension(source));

    /// <summary>
    /// Runs the batch. The originals are never touched. <paramref name="watermarkPath"/>, if it lives in the folder, is
    /// left out. <paramref name="progress"/> gets (done so far, total, current file name).
    /// </summary>
    public static BatchResult Run(string folder, SKBitmap watermark, Placement placement, string? watermarkPath = null,
        Action<int, int, string>? progress = null, CancellationToken cancel = default)
    {
        var input = Path.GetFullPath(folder);
        var output = Path.Combine(input, OutputFolderName);
        var files = FilesIn(input, watermarkPath);
        var failed = new List<string>();
        var done = 0;
        var skipped = 0;
        Directory.CreateDirectory(output);

        // Two photos named alike (a.jpg and a.bmp -> a.png) would overwrite each other: number the later ones.
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < files.Count; i++)
        {
            if (cancel.IsCancellationRequested)
            {
                skipped = files.Count - i;
                break;
            }

            var file = files[i];
            progress?.Invoke(i, files.Count, Path.GetFileName(file));
            try
            {
                var target = OutputPathFor(file, output);
                for (var n = 2; !taken.Add(target); n++)
                    target = Path.Combine(output, $"{Path.GetFileNameWithoutExtension(file)} ({n}){Stamper.OutputExtension(file)}");

                using var photo = Stamper.Load(file);
                using var stamped = Stamper.Stamp(photo, watermark, placement);
                Stamper.Save(stamped, target);
                done++;
            }
            catch (Exception ex)
            {
                failed.Add($"{Path.GetFileName(file)}: {ex.Message}");
            }
        }

        progress?.Invoke(files.Count - skipped, files.Count, "");
        return new BatchResult(output, done, failed, skipped);
    }
}
