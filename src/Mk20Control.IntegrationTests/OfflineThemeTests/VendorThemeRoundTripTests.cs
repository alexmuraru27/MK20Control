using Mk20Control.Protocol.Codecs;
using Mk20Control.Protocol.Theme.Actions;
using Mk20Control.Protocol.Theme.Items;
using NUnit.Framework;

namespace Mk20Control.IntegrationTests.OfflineThemeTests;

/// <summary>
/// Decodes every real vendor <c>.Theme</c> file installed by ScreenKeyWindows, re-encodes it,
/// and asserts every key action comes back byte-for-byte unchanged.
///
/// This is the safety net for modelling only a subset of the vendor's action types. This
/// library deliberately models just the actions it needs (keyboard, page navigation, text/
/// command, encoders); everything else - <c>openWeb</c>, <c>qmk_mouse</c>, <c>Microphone</c>,
/// <c>Loudspeaker</c>, <c>keyboard_switch</c>, <c>ControlFlow</c>, and anything the vendor
/// adds later - decodes to <see cref="UnknownKeyAction"/>. Unmodelled must not mean lost:
/// every field stays in <see cref="KeyAction.RawFields"/> and is written back verbatim, so
/// editing one key of a vendor theme cannot corrupt the others.
///
/// Skips itself when no vendor themes are installed (e.g. on CI). Point
/// <c>MK20_VENDOR_THEME_DIR</c> at a theme directory to override the default location.
/// </summary>
public class VendorThemeRoundTripTests
{
    private const string DirectoryEnvironmentVariable = "MK20_VENDOR_THEME_DIR";

    private static IEnumerable<string> VendorThemeFiles()
    {
        string? dir = Environment.GetEnvironmentVariable(DirectoryEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(dir))
        {
            // Default install layout: MK20Software sits alongside this repository.
            string repoRoot = Support.TestPaths.RepoRoot;
            dir = Path.GetFullPath(Path.Combine(
                repoRoot, "..", "MK20Software", "ScreenKeyWindows_v1_1", "theme", "MK20"));
        }

        if (!Directory.Exists(dir))
            return Array.Empty<string>();

        return Directory.EnumerateFiles(dir, "*.theme", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    [Test]
    public void EveryKeyAction_SurvivesADecodeEncodeDecodeCycle()
    {
        var files = VendorThemeFiles().ToList();
        if (files.Count == 0)
            Assert.Ignore($"No vendor themes found - set {DirectoryEnvironmentVariable} to run this.");

        var problems = new List<string>();
        int themes = 0, actions = 0, byteIdentical = 0;

        foreach (string file in files)
        {
            byte[] original = File.ReadAllBytes(file);

            Mk20Control.Protocol.Theme.ThemeFile first, second;
            byte[] reEncoded;
            try
            {
                first = ThemeFileCodec.Decode(original);
                reEncoded = ThemeFileCodec.Encode(first);
                second = ThemeFileCodec.Decode(reEncoded);
            }
            catch (Exception ex)
            {
                problems.Add($"{Path.GetFileName(file)}: decode/encode threw {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            themes++;
            if (original.AsSpan().SequenceEqual(reEncoded)) byteIdentical++;

            var before = ActionFingerprints(first).ToList();
            var after = ActionFingerprints(second).ToList();
            actions += before.Count;

            if (before.Count != after.Count)
            {
                problems.Add($"{Path.GetFileName(file)}: {before.Count} key action(s) in, {after.Count} out");
                continue;
            }

            for (int i = 0; i < before.Count; i++)
            {
                if (before[i] != after[i])
                    problems.Add($"{Path.GetFileName(file)}: key action #{i} changed\n      before: {before[i]}\n      after:  {after[i]}");
            }
        }

        TestContext.WriteLine(
            $"Checked {actions} key action(s) across {themes} vendor theme(s); " +
            $"{byteIdentical} theme(s) also re-encoded byte-for-byte identically.");

        Assert.That(problems, Is.Empty, "vendor key actions were altered by a decode/encode cycle:\n  " + string.Join("\n  ", problems));
    }

    /// <summary>
    /// A key action's complete on-wire identity: its type plus the exact bytes its full field
    /// map encodes to. Comparing these before and after catches a dropped or reordered field,
    /// which is what would happen if an unmodelled action type were decoded lossily.
    /// </summary>
    private static IEnumerable<string> ActionFingerprints(Mk20Control.Protocol.Theme.ThemeFile theme) =>
        theme.Pages
            .SelectMany(p => p.Items.OfType<KeyItem>())
            .Select(k => k.Action)
            .Select(a => a is null
                ? "(no action)"
                : $"{a.RawType}:{BitConverter.ToString(VariantMapCodec.EncodeMap(a.RawFields)).Replace("-", "")}");

    [Test]
    public void UnmodelledVendorActions_KeepAllTheirFields()
    {
        var files = VendorThemeFiles().ToList();
        if (files.Count == 0)
            Assert.Ignore($"No vendor themes found - set {DirectoryEnvironmentVariable} to run this.");

        var unmodelled = new SortedDictionary<string, int>(StringComparer.Ordinal);

        foreach (string file in files)
        {
            Mk20Control.Protocol.Theme.ThemeFile decoded;
            try { decoded = ThemeFileCodec.Decode(File.ReadAllBytes(file)); }
            catch { continue; }

            foreach (var action in decoded.Pages
                         .SelectMany(p => p.Items.OfType<KeyItem>())
                         .Select(k => k.Action)
                         .OfType<UnknownKeyAction>())
            {
                unmodelled.TryGetValue(action.RawType, out int count);
                unmodelled[action.RawType] = count + 1;

                // The whole point: an action this library does not model still carries its
                // full field set, which is what lets it be re-encoded untouched.
                Assert.That(action.RawFields, Is.Not.Empty, $"'{action.RawType}' decoded with no fields - data was dropped");
                Assert.That(action.RawFields.ContainsKey("type"), Is.True, $"'{action.RawType}' lost its type field");
            }
        }

        TestContext.WriteLine(unmodelled.Count == 0
            ? "No unmodelled vendor action types present in the installed themes."
            : "Unmodelled vendor action types found (all preserved verbatim): " +
              string.Join(", ", unmodelled.Select(kv => $"{kv.Key} x{kv.Value}")));
    }
}
