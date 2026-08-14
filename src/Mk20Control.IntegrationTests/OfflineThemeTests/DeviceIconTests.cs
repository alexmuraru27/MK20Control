using System;
using System.Linq;
using Mk20Control.Protocol.Theme.Building;
using NUnit.Framework;

namespace Mk20Control.IntegrationTests.OfflineThemeTests;

/// <summary>
/// Pins <see cref="DeviceIcon"/> to the confirmed built-in artwork paths. Callers select an
/// icon by name and the library spells the path, so this is the one place those strings are
/// allowed to appear - if the vendor renames one, exactly one test fails. No hardware required.
/// </summary>
public class DeviceIconTests
{
    [Test]
    public void Values_AreSequentialFromZero()
    {
        int[] values = Enum.GetValues<DeviceIcon>().Cast<int>().ToArray();

        Assert.That(values, Is.EqualTo(Enumerable.Range(0, values.Length).ToArray()),
            "the enum is part of the public API, so its values stay 0,1,2,... with no gaps");
    }

    [Test]
    public void EveryIcon_ResolvesToAConfirmedDevicePath()
    {
        foreach (DeviceIcon icon in Enum.GetValues<DeviceIcon>())
        {
            string path = DeviceIcons.PathOf(icon);

            Assert.That(path, Does.StartWith("/static/icon/"), $"{icon} must live under the device's icon store");
            Assert.That(path, Does.EndWith(".png"), $"{icon} must be a png");
        }
    }

    [Test]
    public void EveryIcon_ResolvesToADistinctPath()
    {
        string[] paths = Enum.GetValues<DeviceIcon>().Select(DeviceIcons.PathOf).ToArray();

        Assert.That(paths, Is.Unique, "two icons resolving to the same artwork would make one of them pointless");
    }

    [Test]
    public void PathOf_ThrowsForAValueOutsideTheEnum()
    {
        Assert.That(() => DeviceIcons.PathOf((DeviceIcon)999), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [TestCase(DeviceIcon.PageSwitch, "/static/icon/dark/PageSwitch_128x128.png")]
    [TestCase(DeviceIcon.OpenFolder, "/static/icon/dark/createFolder_128x128.png")]
    [TestCase(DeviceIcon.OneLevelUp, "/static/icon/dark/oneLevelUp_128x128.png")]
    [TestCase(DeviceIcon.Keyboard, "/static/icon/dark/keyboard_128x128.png")]
    [TestCase(DeviceIcon.EncoderSystemVolume, "/static/icon/white/systemVolume_.png")]
    [TestCase(DeviceIcon.EncoderDeviceBrightness, "/static/icon/white/deviceBrightness_.png")]
    [TestCase(DeviceIcon.EncoderDeviceVolume, "/static/icon/white/deviceVolume_.png")]
    [TestCase(DeviceIcon.EncoderSystemMedia, "/static/icon/white/systemMedia_214x142.png")]
    [TestCase(DeviceIcon.EncoderKeyboard, "/static/icon/white/keyboard_214x142.png")]
    public void PathOf_MatchesTheVendorSpelling(DeviceIcon icon, string expected)
    {
        // The vendor's own naming is inconsistent (dimensions on some, a trailing underscore
        // on others), which is exactly why callers should not have to type these.
        Assert.That(DeviceIcons.PathOf(icon), Is.EqualTo(expected));
    }

    [TestCase(EncoderFunctionType.SystemVolume, DeviceIcon.EncoderSystemVolume)]
    [TestCase(EncoderFunctionType.DeviceBrightness, DeviceIcon.EncoderDeviceBrightness)]
    [TestCase(EncoderFunctionType.DeviceVolume, DeviceIcon.EncoderDeviceVolume)]
    [TestCase(EncoderFunctionType.SystemMedia, DeviceIcon.EncoderSystemMedia)]
    public void ForEncoderFunction_PairsTheArtworkWithTheFunction(EncoderFunctionType type, DeviceIcon expected)
    {
        Assert.That(DeviceIcons.ForEncoderFunction(type), Is.EqualTo(expected));
    }

    [Test]
    public void IconDevice_ProducesTheSameKeyAsSpellingTheRawPath()
    {
        // The enum overload must be pure convenience - it has to set exactly the same asset
        // path the caller would have typed. (Whole-file bytes can't be compared: every page
        // gets a fresh Guid.NewGuid() id, so two builds always differ.)
        var builder = new ThemeBuilder();
        builder.AddPage(page => page
            .SetCanvas(640, 656)
            .AddKey(0, 0, key => key.IconDevice(DeviceIcon.OpenFolder))
            .AddKey(0, 1, key => key.IconAssetPath(DeviceIcons.PathOf(DeviceIcon.OpenFolder))));

        var keys = builder.Build().Pages[0].Items.OfType<Mk20Control.Protocol.Theme.Items.KeyItem>().ToList();
        string viaEnum = keys[0].IconAssetPath!;
        string viaPath = keys[1].IconAssetPath!;

        Assert.That(viaEnum, Is.EqualTo(viaPath));
        Assert.That(viaEnum, Is.EqualTo("/static/icon/dark/createFolder_128x128.png"));
    }
}
