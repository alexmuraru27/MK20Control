using System;
using System.Globalization;

using Mk20Control.Protocol.Compat;

namespace Mk20Control.Protocol.Theme.Building;

/// <summary>
/// An RGBA colour for theme widgets, replacing hand-written
/// <c>"r=0,g=170,b=255,a=220"</c> strings with named, range-checked components.
///
/// Two textual forms exist in real theme files and both are accepted by
/// <see cref="Parse"/>: the widget wire form <c>"r=&lt;0-255&gt;,g=…,b=…,a=…"</c> (components
/// are sometimes zero-padded, e.g. <c>"r=000"</c>) and the CSS-style hex form
/// <c>"#rrggbb"</c>/<c>"#rrggbbaa"</c> used by a key's title colour.
///
/// A value parsed from text remembers its exact source spelling and reproduces it from
/// <see cref="ToWireString"/>, so decoding and re-encoding a theme cannot alter its bytes.
/// Equality compares the colour components only, never the spelling.
/// </summary>
public readonly struct ThemeColor : IEquatable<ThemeColor>
{
    private readonly string? _sourceText;

    /// <summary>Red component, 0-255.</summary>
    public byte R { get; }

    /// <summary>Green component, 0-255.</summary>
    public byte G { get; }

    /// <summary>Blue component, 0-255.</summary>
    public byte B { get; }

    /// <summary>Alpha component: 0 = fully transparent, 255 = fully opaque.</summary>
    public byte A { get; }

    /// <summary>Creates a colour from components in the range 0-255.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A component is outside 0-255.</exception>
    public ThemeColor(int r, int g, int b, int a = 255)
    {
        R = Component(r, nameof(r));
        G = Component(g, nameof(g));
        B = Component(b, nameof(b));
        A = Component(a, nameof(a));
        _sourceText = null;
    }

    private ThemeColor(byte r, byte g, byte b, byte a, string? sourceText)
    {
        R = r; G = g; B = b; A = a;
        _sourceText = sourceText;
    }

    private static byte Component(int value, string name) =>
        value is >= 0 and <= 255
            ? (byte)value
            : throw new ArgumentOutOfRangeException(name, value, "Colour components must be in the range 0-255.");

    /// <summary>Fully transparent - use to hide a widget while keeping it functional.</summary>
    public static ThemeColor Transparent => new(0, 0, 0, 0);

    /// <summary>Opaque black.</summary>
    public static ThemeColor Black => new(0, 0, 0);

    /// <summary>Opaque white.</summary>
    public static ThemeColor White => new(255, 255, 255);

    /// <summary>Returns this colour with a different alpha, keeping its RGB components.</summary>
    public ThemeColor WithAlpha(int alpha) => new(R, G, B, alpha);

    /// <summary>
    /// Parses either the widget wire form (<c>"r=0,g=170,b=255,a=220"</c>, components
    /// optionally zero-padded) or a hex form (<c>"#rrggbb"</c>, <c>"#rrggbbaa"</c>, with or
    /// without the leading <c>#</c>). The exact input text is preserved for re-encoding.
    /// </summary>
    /// <exception cref="FormatException">The text matches neither form.</exception>
    public static ThemeColor Parse(string text)
    {
        Guard.NotNull(text);
        return TryParse(text, out var colour)
            ? colour
            : throw new FormatException(
                $"'{text}' is not a valid colour. Expected \"r=0,g=170,b=255,a=220\" or \"#rrggbb\"/\"#rrggbbaa\".");
    }

    /// <summary>Attempts to parse either accepted colour form. Returns false instead of throwing.</summary>
    public static bool TryParse(string? text, out ThemeColor colour)
    {
        colour = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        string value = text!.Trim();

        if (value.StartsWith('#') || IsHexOnly(value))
            return TryParseHex(value, out colour);

        int r = -1, g = -1, b = -1, a = 255;
        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0) return false;

            string key = part[..eq].Trim();
            if (!int.TryParse(part[(eq + 1)..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                return false;
            if (n is < 0 or > 255) return false;

            switch (key)
            {
                case "r": r = n; break;
                case "g": g = n; break;
                case "b": b = n; break;
                case "a": a = n; break;
                default: return false;
            }
        }

        if (r < 0 || g < 0 || b < 0) return false;

        colour = new ThemeColor((byte)r, (byte)g, (byte)b, (byte)a, value);
        return true;
    }

    private static bool IsHexOnly(string value)
    {
        if (value.Length is not (6 or 8)) return false;
        foreach (char c in value)
            if (!Uri.IsHexDigit(c)) return false;
        return true;
    }

    private static bool TryParseHex(string value, out ThemeColor colour)
    {
        colour = default;
        string digits = value.StartsWith('#') ? value[1..] : value;
        if (digits.Length is not (6 or 8)) return false;

        foreach (char c in digits)
            if (!Uri.IsHexDigit(c)) return false;

        byte r = Convert.ToByte(digits[..2], 16);
        byte g = Convert.ToByte(digits.Substring(2, 2), 16);
        byte b = Convert.ToByte(digits.Substring(4, 2), 16);
        byte a = digits.Length == 8 ? Convert.ToByte(digits.Substring(6, 2), 16) : (byte)255;

        colour = new ThemeColor(r, g, b, a, value);
        return true;
    }

    /// <summary>
    /// The widget wire form. A colour parsed from text returns that exact text, so a
    /// decode/encode cycle is byte-preserving; a colour built from components is rendered as
    /// <c>"r=…,g=…,b=…,a=…"</c>.
    /// </summary>
    public string ToWireString() =>
        _sourceText ?? FormattableString.Invariant($"r={R},g={G},b={B},a={A}");

    /// <summary>The <c>"#rrggbb"</c> form used by a key's title colour, or <c>"#rrggbbaa"</c> when not fully opaque.</summary>
    public string ToHexString() => A == 255
        ? FormattableString.Invariant($"#{R:x2}{G:x2}{B:x2}")
        : FormattableString.Invariant($"#{R:x2}{G:x2}{B:x2}{A:x2}");

    /// <summary>Returns <see cref="ToWireString"/>.</summary>
    public override string ToString() => ToWireString();

    /// <summary>Parses a colour string, so existing literals remain valid wherever a <see cref="ThemeColor"/> is expected.</summary>
    public static implicit operator ThemeColor(string text) => Parse(text);

    /// <summary>Compares colour components only; the source spelling is ignored.</summary>
    public bool Equals(ThemeColor other) => R == other.R && G == other.G && B == other.B && A == other.A;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ThemeColor other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(R, G, B, A);

    public static bool operator ==(ThemeColor left, ThemeColor right) => left.Equals(right);

    public static bool operator !=(ThemeColor left, ThemeColor right) => !left.Equals(right);
}
