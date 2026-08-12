using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Mk20Control.Protocol;

/// <summary>
/// Layer-B JSON-RPC envelope shapes (PROTOCOL_WAVESHARE_MK20.md section 5, VERIFIED from the demo source).
/// </summary>
public sealed class JsonRpcRequest
{
    [JsonPropertyName("method")]
    public string Method { get; set; } = "";

    [JsonPropertyName("parameters")]
    public object? Parameters { get; set; }
}

public sealed class JsonRpcReply
{
    [JsonPropertyName("ack_method")]
    public string? AckMethod { get; set; }

    [JsonPropertyName("method")]
    public string? Method { get; set; }

    [JsonPropertyName("success")]
    public bool? Success { get; set; }

    [JsonPropertyName("errorString")]
    public string? ErrorString { get; set; }

    [JsonPropertyName("result")]
    public System.Text.Json.JsonElement? Result { get; set; }

    [JsonPropertyName("parameters")]
    public System.Text.Json.JsonElement? Parameters { get; set; }
}

/// <summary>getInfo result shape (PROTOCOL_WAVESHARE_MK20.md section 6).</summary>
public sealed class DeviceInfo
{
    [JsonPropertyName("deviceModel")]
    public string? DeviceModel { get; set; }

    [JsonPropertyName("deviceVersion")]
    public string? DeviceVersion { get; set; }

    [JsonPropertyName("deviceWidth")]
    public int DeviceWidth { get; set; }

    [JsonPropertyName("deviceHeight")]
    public int DeviceHeight { get; set; }

    [JsonPropertyName("screen_model")]
    public string? ScreenModel { get; set; }

    [JsonPropertyName("screen_width")]
    public int ScreenWidth { get; set; }

    [JsonPropertyName("screen_height")]
    public int ScreenHeight { get; set; }

    [JsonPropertyName("devicePanel")]
    public DevicePanel? DevicePanel { get; set; }
}

public sealed class DevicePanel
{
    [JsonPropertyName("rectCols")]
    public int RectCols { get; set; }

    [JsonPropertyName("rectRows")]
    public int RectRows { get; set; }

    [JsonPropertyName("rects")]
    public List<DeviceRect>? Rects { get; set; }
}

public sealed class DeviceRect
{
    [JsonPropertyName("x")] public int X { get; set; }
    [JsonPropertyName("y")] public int Y { get; set; }
    [JsonPropertyName("width")] public int Width { get; set; }
    [JsonPropertyName("height")] public int Height { get; set; }
    [JsonPropertyName("col")] public int Col { get; set; }
    [JsonPropertyName("row")] public int Row { get; set; }
    [JsonPropertyName("isKey")] public bool IsKey { get; set; }
}

/// <summary>keyStateChanged unsolicited event payload.</summary>
public sealed class KeyStateChanged
{
    [JsonPropertyName("col")] public int Col { get; set; }
    [JsonPropertyName("row")] public int Row { get; set; }
    [JsonPropertyName("pressed")] public bool Pressed { get; set; }
}
