using Mk20Control.IntegrationTests.Support;
using Mk20Control.Protocol.Codecs;
using Mk20Control.Protocol.Theme.Actions;
using Mk20Control.Protocol.Theme.Building;
using Mk20Control.Protocol.Theme.Items;
using NUnit.Framework;

namespace Mk20Control.IntegrationTests.OfflineThemeTests;

/// <summary>
/// Covers the raw <see cref="KeyActions.TypeText"/> action - the low-level <c>text</c> action
/// that <see cref="KeyActions.Command"/> is built on, kept so vendor themes survive a
/// decode/encode round trip with their <c>isInputEnter</c>/<c>isCopyPaste</c> flags intact.
///
/// NOTHING TYPES THESE. Confirmed by USB capture: a text key emits zero HID keystrokes - the
/// device merely reports the press with the string. That property is exactly what makes the
/// action usable as a command-ID carrier (see <see cref="CommandThemeBuilderTests"/>); this
/// class exists to pin the file-format details such keys depend on.
///
/// The field shape asserted here was confirmed against a real capture of a vendor-editor
/// text key (tools/Captures/capture7): <c>inputText</c>/<c>isInputEnter</c>/<c>isCopyPaste</c>
/// plus <c>description":"Text"</c>, <c>parentDescription":"System input control"</c>,
/// <c>iconPath":"/static/icon/dark/Text.png"</c> and an empty <c>AISoundControlKeyword</c>.
/// No hardware required.
/// </summary>
public class TextMacroThemeBuilderTests
{
    /// <summary>
    /// Deliberately mirrors the vendor-made <c>textInput.Theme</c> key-for-key - same grid
    /// positions, titles, texts and flags - so a wire capture of this theme can be compared
    /// 1:1 against a capture of the vendor's, with the theme file as the only variable.
    /// </summary>
    private static readonly (int Row, int Col, string Title, string Text, bool Enter, bool Paste)[] TextKeys =
    {
        (0, 0, "CLEAR",      "#clear",     false, false),
        (0, 1, "NO TIRES",   "#tires off", false, false),
        (0, 2, "FUEL 20",    "#fuel 20",   false, false),
        (0, 3, "WINDSCREEN", "#ws",        false, false),
        (0, 4, "SORRY",      "Sorry!",     false, false),
        (1, 0, "PASTE",      "Long text that is faster to paste than to type one character at a time.", false, false),
        (1, 1, "NO ENTER",   "typed but not sent", true, false),
    };

    public static byte[] BuildTextMacroTheme()
    {
        string iconPath = TestPaths.IconFile(1);
        Assert.That(File.Exists(iconPath), Is.True, $"Missing icon file: {iconPath}. Run tools\\AssetGenerator first.");
        byte[] iconBytes = File.ReadAllBytes(iconPath);

        var builder = new ThemeBuilder();
        builder.AddPage(page =>
        {
            page.SetCanvas(640, 656);

            foreach (var (row, col, title, text, enter, paste) in TextKeys)
            {
                page.AddKey(row, col, key => key
                    .Icon("icon_01.png", iconBytes)
                    .Title(title)
                    .Action(KeyActions.TypeText(text, pressEnterAfter: enter, useCopyPaste: paste)));
            }

            // Matches the vendor theme's own control key: a plain keyboard 'T'
            // (USB HID 0x17 = 23), which is device-native and known to work.
            page.AddKey(1, 2, key => key
                .Icon("icon_01.png", iconBytes)
                .Title("CHAT")
                .Action(KeyActions.Keyboard(HidKey.T, "T")));
        });

        return ThemeFileCodec.Encode(builder.Build());
    }

    [Test]
    public void TextMacros_RoundTripWithTheirTextAndEnterFlag()
    {
        var decoded = ThemeFileCodec.Decode(BuildTextMacroTheme());
        var keys = decoded.Pages[0].Items.OfType<KeyItem>().ToList();
        var actions = keys.Select(k => k.Action).OfType<TextInputAction>().ToList();

        Assert.That(actions, Has.Count.EqualTo(TextKeys.Length));

        for (int i = 0; i < TextKeys.Length; i++)
        {
            var (row, col, _, text, enter, paste) = TextKeys[i];
            Assert.Multiple(() =>
            {
                Assert.That(keys[i].Row, Is.EqualTo(row));
                Assert.That(keys[i].Column, Is.EqualTo(col));
                Assert.That(actions[i].InputText, Is.EqualTo(text));
                Assert.That(actions[i].IsInputEnter, Is.EqualTo(enter));
                Assert.That(actions[i].IsCopyPaste, Is.EqualTo(paste));
            });
        }

        // Write the built theme out so it can be installed/diffed against a vendor-made one.
        string outPath = Path.Combine(Path.GetTempPath(), "mk20-textmacro-theme.Theme");
        File.WriteAllBytes(outPath, BuildTextMacroTheme());
        TestContext.WriteLine($"Wrote {outPath}");
    }

    [Test]
    public void TypeText_EmitsTheConfirmedVendorFieldOrder()
    {
        // A vendor-saved text key writes its controlData fields in this exact order; matching
        // it keeps a builder-made key byte-order-identical to a real one.
        var action = KeyActions.TypeText("#clear", pressEnterAfter: true);

        Assert.That(action.RawFields.Keys, Is.EqualTo(new[]
        {
            "type", "parentDescription", "isInputEnter", "isCopyPaste",
            "inputText", "iconPath", "description", "AISoundControlKeyword",
        }));
    }

    [Test]
    public void BuiltTheme_CarriesTheKeyMacroTableTextKeysRequire()
    {
        // Confirmed on real hardware: with an empty keyMacroValue, keyboard keys still work
        // but text keys are completely dead. Every real vendor theme carries this same
        // 92-byte value, so assert a builder-made theme does too.
        const string Expected = "AAAAEAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

        var decoded = ThemeFileCodec.Decode(BuildTextMacroTheme());

        Assert.That(decoded.KeyMacroValue, Is.Not.Empty, "text/macro keys do not work without the key-macro table");
        Assert.That(System.Text.Encoding.ASCII.GetString(decoded.KeyMacroValue), Is.EqualTo(Expected));
    }

    [Test]
    public void EncodedTheme_EndsWithTheFourByteTrailerRealThemesHave()
    {
        // Every one of the 38 real vendor themes examined ends with four zero bytes after the
        // last asset; files this encoder produced were the only ones missing it. Omitting it
        // left text keys inert on real hardware.
        byte[] encoded = BuildTextMacroTheme();

        Assert.That(encoded.Length, Is.GreaterThan(4));
        Assert.That(encoded.Skip(encoded.Length - 4).ToArray(), Is.EqualTo(new byte[] { 0, 0, 0, 0 }));
    }

    [Test]
    public void TypeText_CarriesConfirmedRealMetadata()
    {
        var action = KeyActions.TypeText("!notirechange", pressEnterAfter: true);

        Assert.Multiple(() =>
        {
            Assert.That(action.RawType, Is.EqualTo("text"));
            Assert.That(action.Description, Is.EqualTo("Text"));
            Assert.That(action.ParentDescription, Is.EqualTo("System input control"));
            Assert.That(action.IconPath, Is.EqualTo("/static/icon/dark/Text.png"));
            Assert.That(action.RawFields.ContainsKey("AISoundControlKeyword"), Is.True);
        });
    }

    [Test]
    public void TypeText_PreservesPunctuationAndSpacing()
    {
        // Command ids and vendor chat strings both lean on punctuation and spaces; a corrupted
        // payload would route to the wrong handler, so assert the exact string survives
        // encode/decode.
        string[] samples = { "!notirechange", "!fuel 20", "!tires 2 2", "!pit", "P1: sorry mate!" };

        var builder = new ThemeBuilder();
        builder.AddPage(page =>
        {
            page.SetCanvas(640, 656);
            for (int i = 0; i < samples.Length; i++)
            {
                page.AddKey(i / 5, i % 5, key => key.Action(KeyActions.TypeText(samples[i], pressEnterAfter: true)));
            }
        });

        var decoded = ThemeFileCodec.Decode(ThemeFileCodec.Encode(builder.Build()));
        var texts = decoded.Pages[0].Items.OfType<KeyItem>()
            .Select(k => k.Action).OfType<TextInputAction>()
            .Select(a => a.InputText).ToList();

        Assert.That(texts, Is.EqualTo(samples));
    }
}
