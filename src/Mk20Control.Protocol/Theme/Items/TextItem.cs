namespace Mk20Control.Protocol.Theme.Items;

/// <summary>
/// A text item ("type": "113" in the theme layout JSON), optionally bound to a live data
/// source for its displayed value (e.g. showing the current backlight percentage).
/// </summary>
public sealed record TextItem : ThemeItem
{
    /// <summary>
    /// The data-source key this text is bound to (e.g. "device_bl"), when
    /// "system_data_flag" is "1"; null if the text is static.
    /// </summary>
    public string? SystemDataName { get; init; }

    /// <summary>The static text content ("text_str"); may be a placeholder when data-bound.</summary>
    public string? Text { get; init; }

    /// <summary>The raw font descriptor string (e.g. "Arial,72,-1,5,75,0,0,0,0,0").</summary>
    public string? Font { get; init; }
}
