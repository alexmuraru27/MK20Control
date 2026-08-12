namespace Mk20Control.Protocol.Theme.Items;

/// <summary>Which physical screen a <see cref="BackgroundItem"/> or <see cref="DynamicImageItem"/> targets.</summary>
public enum BackgroundSurface
{
    /// <summary>Unrecognized/unobserved value - the raw string is preserved on the owning item.</summary>
    Unknown = 0,

    /// <summary>The 20-key main screen (backgroundType="main" in the theme JSON).</summary>
    Main,

    /// <summary>The 2.8" secondary screen (backgroundType="secondary" in the theme JSON).</summary>
    Secondary,
}
