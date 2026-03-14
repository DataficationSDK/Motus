using System.Runtime.CompilerServices;
using Motus.Abstractions;
using SkiaSharp;

[assembly: InternalsVisibleTo("Motus.Tests")]

namespace Motus.Runner.Services.VisualRegression;

internal sealed class VisualRegressionService : IVisualRegressionService
{
    private readonly string _baselinePath;
    private readonly List<VisualCapture> _captures = [];

    public VisualRegressionService(RunnerOptions options)
    {
        _baselinePath = options.BaselinePath ?? "./motus-baselines";
    }

    public IReadOnlyList<VisualCapture> AllCaptures
    {
        get
        {
            lock (_captures)
                return _captures.ToList();
        }
    }

    public event Action? CapturesChanged;

    public async Task<VisualCapture> CaptureAsync(IPage page, string testName, string captureName, CancellationToken ct = default)
    {
        var screenshot = await page.ScreenshotAsync(null).ConfigureAwait(false);
        var baselineFile = GetBaselinePath(testName, captureName);

        byte[]? baseline = null;
        if (File.Exists(baselineFile))
            baseline = await File.ReadAllBytesAsync(baselineFile, ct).ConfigureAwait(false);

        DiffResult? diff = null;
        if (baseline is not null)
            diff = PixelDiff(screenshot, baseline);

        var capture = new VisualCapture(testName, captureName, screenshot, baseline, diff);

        lock (_captures)
            _captures.Add(capture);

        CapturesChanged?.Invoke();
        return capture;
    }

    public async Task AcceptBaselineAsync(string testName, string captureName, byte[] screenshot)
    {
        var path = GetBaselinePath(testName, captureName);
        var dir = Path.GetDirectoryName(path);
        if (dir is not null)
            Directory.CreateDirectory(dir);
        await File.WriteAllBytesAsync(path, screenshot).ConfigureAwait(false);

        // Update existing capture entry with new baseline and zero diff
        lock (_captures)
        {
            for (int i = 0; i < _captures.Count; i++)
            {
                var c = _captures[i];
                if (c.TestName == testName && c.CaptureName == captureName)
                {
                    _captures[i] = c with { Baseline = screenshot, Diff = new DiffResult(true, 0, 0, null) };
                    break;
                }
            }
        }

        CapturesChanged?.Invoke();
    }

    public void Reject(string testName, string captureName)
    {
        lock (_captures)
            _captures.RemoveAll(c => c.TestName == testName && c.CaptureName == captureName);
        CapturesChanged?.Invoke();
    }

    internal static DiffResult PixelDiff(byte[] current, byte[] baseline)
    {
        using var currentBmp = SKBitmap.Decode(current);
        using var baselineBmp = SKBitmap.Decode(baseline);

        if (currentBmp is null || baselineBmp is null)
            return new DiffResult(false, 100, 0, null);

        if (currentBmp.Width != baselineBmp.Width || currentBmp.Height != baselineBmp.Height)
        {
            var totalPixels = Math.Max(currentBmp.Width * currentBmp.Height, baselineBmp.Width * baselineBmp.Height);
            return new DiffResult(false, 100, totalPixels, null);
        }

        var width = currentBmp.Width;
        var height = currentBmp.Height;
        var diffCount = 0;
        const int threshold = 10;

        using var diffBmp = new SKBitmap(width, height);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var c = currentBmp.GetPixel(x, y);
                var b = baselineBmp.GetPixel(x, y);

                var dr = Math.Abs(c.Red - b.Red);
                var dg = Math.Abs(c.Green - b.Green);
                var db = Math.Abs(c.Blue - b.Blue);

                if (dr > threshold || dg > threshold || db > threshold)
                {
                    diffCount++;
                    diffBmp.SetPixel(x, y, new SKColor(255, 0, 0, 180));
                }
                else
                {
                    diffBmp.SetPixel(x, y, new SKColor(c.Red, c.Green, c.Blue, 60));
                }
            }
        }

        var totalPx = width * height;
        var diffPercent = totalPx > 0 ? (double)diffCount / totalPx * 100 : 0;
        var isMatch = diffCount == 0;

        byte[]? diffImage = null;
        using (var image = SKImage.FromBitmap(diffBmp))
        using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
        {
            diffImage = data.ToArray();
        }

        return new DiffResult(isMatch, Math.Round(diffPercent, 2), diffCount, diffImage);
    }

    private string GetBaselinePath(string testName, string captureName)
    {
        return Path.Combine(_baselinePath, SafeFileName(testName), SafeFileName(captureName) + ".png");
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var result = new char[name.Length];
        for (int i = 0; i < name.Length; i++)
            result[i] = Array.IndexOf(invalid, name[i]) >= 0 ? '_' : name[i];
        return new string(result);
    }
}
