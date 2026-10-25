var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// The SkiaSharp the app was given, by its assembly version.
app.MapGet("/skia", () => typeof(SkiaSharp.SKBitmap).Assembly.GetName().Version!.Major.ToString());

app.Run();

public partial class Program;
