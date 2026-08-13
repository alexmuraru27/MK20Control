namespace Mk20Control.Protocol.Theme.Items.Widgets;

/// <summary>
/// A progress bar item ("type": "102" in the theme layout JSON), circular or linear,
/// optionally bound to a live data source (e.g. "device_bl", "Volume", "CPU Usage").
/// </summary>
public sealed record ProgressBarItem : ThemeItem
{
    /// <summary>
    /// The data-source key this bar is bound to (e.g. "device_bl"), when
    /// "system_data_flag" is "1"; null if the bar is not data-bound.
    /// </summary>
    public string? SystemDataName { get; init; }

    public double? MinValue { get; init; }
    public double? MaxValue { get; init; }
}
