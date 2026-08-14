using System;
using Mk20Control.Protocol.Model;
using NUnit.Framework;

namespace Mk20Control.IntegrationTests.OfflineThemeTests;

/// <summary>
/// Pins <see cref="DeviceThemePath"/> to the confirmed on-device layout
/// (<c>/data/theme/MK20/&lt;name&gt;/&lt;name&gt;.Theme</c>, see PROTOCOL_WAVESHARE_MK20.md
/// §5.2) and to its real job: making it impossible for a caller-supplied name to address
/// anything outside its own theme folder. No hardware required.
/// </summary>
public class DeviceThemePathTests
{
    [Test]
    public void ForTheme_BuildsTheConfirmedLayout()
    {
        Assert.That(
            DeviceThemePath.ForTheme("example-monitor"),
            Is.EqualTo("/data/theme/MK20/example-monitor/example-monitor.Theme"));
    }

    [Test]
    public void ForTheme_AcceptsNonAsciiNames()
    {
        // The vendor's own shipped themes are named in Chinese, so this must not be
        // restricted to ASCII.
        Assert.That(DeviceThemePath.ForTheme("字母"), Is.EqualTo("/data/theme/MK20/字母/字母.Theme"));
    }

    [TestCase("../evil", Description = "escapes the theme root")]
    [TestCase("a/b", Description = "forward slash")]
    [TestCase("a\\b", Description = "backslash")]
    [TestCase("..", Description = "parent directory")]
    [TestCase(".", Description = "current directory")]
    [TestCase("a..b", Description = "dot-dot anywhere")]
    [TestCase("", Description = "empty")]
    [TestCase("   ", Description = "whitespace only")]
    [TestCase(" pad", Description = "leading whitespace")]
    [TestCase("pad ", Description = "trailing whitespace")]
    [TestCase("nul\0name", Description = "control character")]
    public void ForTheme_RejectsAnythingThatIsNotASingleFolderName(string themeName)
    {
        Assert.Multiple(() =>
        {
            Assert.That(DeviceThemePath.IsValidThemeName(themeName), Is.False);
            Assert.That(() => DeviceThemePath.ForTheme(themeName), Throws.ArgumentException);
        });
    }

    [Test]
    public void ForTheme_RejectsAnOverlongName()
    {
        string tooLong = new('a', DeviceThemePath.MaxThemeNameLength + 1);

        Assert.That(() => DeviceThemePath.ForTheme(tooLong), Throws.ArgumentException);
        Assert.That(DeviceThemePath.ForTheme(new string('a', DeviceThemePath.MaxThemeNameLength)),
            Does.Contain(tooLong[..DeviceThemePath.MaxThemeNameLength]),
            "a name exactly at the limit is still accepted");
    }

    [Test]
    public void TryGetThemeName_RoundTripsForTheme()
    {
        string path = DeviceThemePath.ForTheme("example-racing");

        Assert.That(DeviceThemePath.TryGetThemeName(path, out string name), Is.True);
        Assert.That(name, Is.EqualTo("example-racing"));
    }

    [TestCase("/data/theme/MK20/a/b.Theme", Description = "file name differs from its folder")]
    [TestCase("/data/theme/MK20/a/a.theme", Description = "wrong extension case")]
    [TestCase("/data/theme/MK20/SecondaryScreen/1/1.Theme", Description = "nested, not name-addressable")]
    [TestCase("/data/theme/MK20/a.Theme", Description = "no folder")]
    [TestCase("/elsewhere/a/a.Theme", Description = "outside the theme root")]
    [TestCase(null, Description = "null")]
    public void TryGetThemeName_ReturnsFalseForAnythingOffTheStandardLayout(string? deviceThemePath)
    {
        Assert.That(DeviceThemePath.TryGetThemeName(deviceThemePath, out string name), Is.False);
        Assert.That(name, Is.Empty);
    }
}
