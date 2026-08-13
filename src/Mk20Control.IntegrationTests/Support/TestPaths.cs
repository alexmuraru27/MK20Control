namespace Mk20Control.IntegrationTests.Support;

/// <summary>
/// Resolves the repo-relative `assets/` folders used by every test in this project - icons,
/// backgrounds, and GIFs are all checked into the repo (see README.md "Assets") so every
/// test here runs unmodified on any machine with the repo cloned; no machine-specific paths.
/// </summary>
public static class TestPaths
{
    public static string RepoRoot { get; } =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    public static string AssetsDir { get; } = Path.Combine(RepoRoot, "assets");
    public static string IconsDir { get; } = Path.Combine(AssetsDir, "icons");
    public static string BackgroundsDir { get; } = Path.Combine(AssetsDir, "backgrounds");
    public static string GifsDir { get; } = Path.Combine(AssetsDir, "gifs");

    public static string IconFile(int number) => Path.Combine(IconsDir, $"icon_{number:D2}.png");
    public static string BackgroundFile(string fileName) => Path.Combine(BackgroundsDir, fileName);
    public static string GifFile(string fileName) => Path.Combine(GifsDir, fileName);
}
