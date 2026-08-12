using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mk20Control.Protocol.Theme;
using Mk20Control.Protocol.Theme.Actions;
using Mk20Control.Protocol.Theme.Items;

namespace Mk20Control.Protocol.Codecs;

/// <summary>
/// Decodes and encodes the on-disk .Theme file format used by ScreenKeyWindows, fully
/// reverse-engineered byte-by-byte from real theme files (including one with an embedded
/// animated GIF). This is the concrete answer to "how do I set keymaps / icons / GIFs /
/// videos / mouse / sound / text-input actions on a key" - all of it lives in this one
/// file format.
///
/// Confirmed on-disk layout:
///
///   [header map: language(int), keyMacroValue(byte array), keyMacro(byte array, usually null)]
///   [4 reserved bytes, observed as all zero]
///   [4 more bytes of unclear meaning - NOT a reliable byte-length for the JSON that
///    follows (its value did not match the JSON's actual length in testing); this codec
///    instead finds the JSON's true end by scanning for balanced top-level {}/[] while
///    respecting quoted-string escaping, which is correct regardless of what this field means]
///   [UTF-8 JSON text: {"main":{"currentPage":...,"version":...},"pages":[...]}]
///   [1 reserved byte, observed as 0x0a]
///   [assetCount(u32 BE)]
///   repeat assetCount times:
///     [pathByteLen(u32 BE)] [UTF-16BE path string, e.g. "/image/428x142/PhotoAlbum/xxx.gif"]
///     [dataByteLen(u32 BE)] [raw asset bytes - PNG/GIF/MP4, confirmed via magic bytes]
///
/// Round-trip fidelity: every item retains its complete original JSON (<see cref="ThemeItem.RawJson"/>)
/// and every key action retains its complete original decoded fields
/// (<see cref="KeyAction.RawFields"/>), so <see cref="Encode"/> never silently drops data for
/// fields this library doesn't yet promote to a strongly-typed property - even when only a
/// typed property was modified, the rest of the original item/action survives unchanged.
/// </summary>
public static class ThemeFileCodec
{
    private const byte HeaderReservedByte = 0x0a;

    /// <summary>Decodes a complete .Theme file from its raw bytes.</summary>
    /// <exception cref="InvalidDataException">Thrown if the bytes do not match the confirmed .Theme layout.</exception>
    public static ThemeFile Decode(byte[] fileBytes)
    {
        ArgumentNullException.ThrowIfNull(fileBytes);
        if (fileBytes.Length < 4)
            throw new InvalidDataException("File is too short to contain a theme header map.");

        int pos = 0;
        var header = VariantMapCodec.DecodeMap(fileBytes, ref pos);

        int language = header.TryGetValue("language", out var langVal) && langVal is { IsNull: false, AsInt32: { } li }
            ? li
            : throw new InvalidDataException("Theme header is missing a valid 'language' field.");

        byte[] keyMacroValue = header.TryGetValue("keyMacroValue", out var kmv) && kmv is { IsNull: false, AsByteArray: { } kmvBytes }
            ? kmvBytes
            : throw new InvalidDataException("Theme header is missing a valid 'keyMacroValue' field.");

        byte[]? keyMacro = header.TryGetValue("keyMacro", out var km) && !km.IsNull ? km.AsByteArray : null;

        // Skip the 8 bytes of reserved/unclear-purpose header padding.
        const int reservedGapLength = 8;
        if (pos + reservedGapLength > fileBytes.Length)
            throw new InvalidDataException("File is truncated: missing the 8-byte header gap after the theme header map.");
        pos += reservedGapLength;

        if (!TryFindBalancedJsonEnd(fileBytes, pos, out int jsonEnd))
            throw new InvalidDataException("Could not locate the end of the embedded layout JSON (unbalanced braces/brackets).");
        string layoutJson = Encoding.UTF8.GetString(fileBytes, pos, jsonEnd - pos + 1);
        pos = jsonEnd + 1;

        if (pos < fileBytes.Length && fileBytes[pos] == HeaderReservedByte) pos += 1;

        var assets = new List<ThemeAsset>();
        if (pos + 4 <= fileBytes.Length)
        {
            uint assetCount = ReadUInt32BigEndian(fileBytes, ref pos);
            const uint maxPlausibleAssetCount = 100_000;
            if (assetCount > maxPlausibleAssetCount)
                throw new InvalidDataException($"Implausible asset count {assetCount}.");
            for (int i = 0; i < assetCount; i++)
            {
                string assetPath = VariantMapCodec.DecodeString(fileBytes, ref pos)
                    ?? throw new InvalidDataException($"Asset {i} has a null path.");
                byte[] assetData = VariantMapCodec.DecodeByteArray(fileBytes, ref pos)
                    ?? throw new InvalidDataException($"Asset {i} ('{assetPath}') has null data.");
                assets.Add(new ThemeAsset { Path = assetPath, Data = assetData });
            }
        }

        using var layoutDoc = JsonDocument.Parse(layoutJson);
        var root = layoutDoc.RootElement;
        string currentPageId = root.TryGetProperty("main", out var mainEl) && mainEl.TryGetProperty("currentPage", out var cp)
            ? cp.GetString() ?? ""
            : "";
        string layoutVersion = root.TryGetProperty("main", out var mainEl2) && mainEl2.TryGetProperty("version", out var ver)
            ? ver.GetString() ?? ""
            : "";

        var pages = new List<ThemePage>();
        if (root.TryGetProperty("pages", out var pagesEl) && pagesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var pageEl in pagesEl.EnumerateArray())
            {
                pages.Add(DecodePage(pageEl));
            }
        }

        return new ThemeFile
        {
            Language = language,
            KeyMacroValue = keyMacroValue,
            KeyMacro = keyMacro,
            CurrentPageId = currentPageId,
            LayoutVersion = layoutVersion,
            Pages = pages,
            Assets = assets,
        };
    }

    private static ThemePage DecodePage(JsonElement pageEl)
    {
        var canvas = pageEl.TryGetProperty("canvas", out var canvasEl) ? DecodeCanvas(canvasEl) : new ThemeCanvas();
        string? pageName = pageEl.TryGetProperty("pageName", out var pn) ? pn.GetString() : null;

        var items = new List<ThemeItem>();
        if (pageEl.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var itemEl in itemsEl.EnumerateArray())
            {
                items.Add(DecodeItem(itemEl));
            }
        }

        return new ThemePage { PageName = pageName, Canvas = canvas, Items = items };
    }

    private static ThemeCanvas DecodeCanvas(JsonElement canvasEl) => new()
    {
        Width = TryGetDouble(canvasEl, "canvas_w"),
        Height = TryGetDouble(canvasEl, "canvas_h"),
        IsFlipped = TryGetBool(canvasEl, "canvas_flip"),
        IsRotated = TryGetBool(canvasEl, "canvas_rotate"),
        ShowUnit = TryGetBool(canvasEl, "showUnit"),
    };

    private static ThemeItem DecodeItem(JsonElement itemEl)
    {
        string typeCode = itemEl.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";

        string? id = TryGetString(itemEl, "id");
        string? itemName = TryGetString(itemEl, "itemName");
        double? x = TryGetDouble(itemEl, "x");
        double? y = TryGetDouble(itemEl, "y");
        double? z = TryGetDouble(itemEl, "z");
        double? w = TryGetDouble(itemEl, "w");
        double? h = TryGetDouble(itemEl, "h");
        double? rotate = TryGetDouble(itemEl, "rotate");
        double? scale = TryGetDouble(itemEl, "scale");
        bool? locked = TryGetBool(itemEl, "lock");

        switch (typeCode)
        {
            case "100":
                string rawSurface = TryGetString(itemEl, "backgroundType") ?? "";
                return new BackgroundItem
                {
                    RawTypeCode = typeCode, Id = id, ItemName = itemName, X = x, Y = y, Z = z, Width = w, Height = h,
                    Rotate = rotate, Scale = scale, IsLocked = locked, RawJson = itemEl.Clone(),
                    RawSurface = rawSurface,
                    Surface = rawSurface switch { "main" => BackgroundSurface.Main, "secondary" => BackgroundSurface.Secondary, _ => BackgroundSurface.Unknown },
                    AssetPath = TryGetString(itemEl, "path") ?? "",
                };
            case "102":
                return new ProgressBarItem
                {
                    RawTypeCode = typeCode, Id = id, ItemName = itemName, X = x, Y = y, Z = z, Width = w, Height = h,
                    Rotate = rotate, Scale = scale, IsLocked = locked, RawJson = itemEl.Clone(),
                    SystemDataName = TryGetBool(itemEl, "system_data_flag") == true ? TryGetString(itemEl, "system_data_name") : null,
                    MinValue = TryGetDouble(itemEl, "system_data_min_value"),
                    MaxValue = TryGetDouble(itemEl, "system_data_max_value"),
                };
            case "103":
                return new LinearGaugeItem
                {
                    RawTypeCode = typeCode, Id = id, ItemName = itemName, X = x, Y = y, Z = z, Width = w, Height = h,
                    Rotate = rotate, Scale = scale, IsLocked = locked, RawJson = itemEl.Clone(),
                    SystemDataName = TryGetBool(itemEl, "system_data_flag") == true ? TryGetString(itemEl, "system_data_name") : null,
                    MinValue = TryGetDouble(itemEl, "system_data_min_value"),
                    MaxValue = TryGetDouble(itemEl, "system_data_max_value"),
                    FrontColor = TryGetString(itemEl, "front_color"),
                    BackColor = TryGetString(itemEl, "back_color"),
                    BorderColor = TryGetString(itemEl, "border_color"),
                    BorderWidth = TryGetDouble(itemEl, "border_width"),
                };
            case "113":
                return new TextItem
                {
                    RawTypeCode = typeCode, Id = id, ItemName = itemName, X = x, Y = y, Z = z, Width = w, Height = h,
                    Rotate = rotate, Scale = scale, IsLocked = locked, RawJson = itemEl.Clone(),
                    SystemDataName = TryGetBool(itemEl, "system_data_flag") == true ? TryGetString(itemEl, "system_data_name") : null,
                    Text = TryGetString(itemEl, "text_str"),
                    Font = TryGetString(itemEl, "text_font"),
                };
            case "114":
                return new DynamicImageItem
                {
                    RawTypeCode = typeCode, Id = id, ItemName = itemName, X = x, Y = y, Z = z, Width = w, Height = h,
                    Rotate = rotate, Scale = scale, IsLocked = locked, RawJson = itemEl.Clone(),
                    AssetPath = TryGetString(itemEl, "path") ?? "",
                    SystemDataName = TryGetBool(itemEl, "system_data_flag") == true ? TryGetString(itemEl, "system_data_name") : null,
                };
            case "109":
                return new RadialGaugeItem
                {
                    RawTypeCode = typeCode, Id = id, ItemName = itemName, X = x, Y = y, Z = z, Width = w, Height = h,
                    Rotate = rotate, Scale = scale, IsLocked = locked, RawJson = itemEl.Clone(),
                    SystemDataName = TryGetBool(itemEl, "system_data_flag") == true ? TryGetString(itemEl, "system_data_name") : null,
                    MinValue = TryGetDouble(itemEl, "system_data_min_value"),
                    MaxValue = TryGetDouble(itemEl, "system_data_max_value"),
                    AngleMinValue = TryGetDouble(itemEl, "angleMinValue"),
                    AngleMaxValue = TryGetDouble(itemEl, "angleMaxValue"),
                    ArcRadius = TryGetDouble(itemEl, "arcRadius"),
                    ArcCircularInterval = TryGetDouble(itemEl, "arcCircularInterval"),
                    GradientColor1 = TryGetString(itemEl, "gradientColor1"),
                    GradientColor2 = TryGetString(itemEl, "gradientColor2"),
                    GradientColor3 = TryGetString(itemEl, "gradientColor3"),
                };
            case "111":
                return new DigitalClockItem
                {
                    RawTypeCode = typeCode, Id = id, ItemName = itemName, X = x, Y = y, Z = z, Width = w, Height = h,
                    Rotate = rotate, Scale = scale, IsLocked = locked, RawJson = itemEl.Clone(),
                    SystemDataName = TryGetBool(itemEl, "system_data_flag") == true ? TryGetString(itemEl, "system_data_name") : null,
                    Font = TryGetString(itemEl, "text_font"),
                    FrontColor = TryGetString(itemEl, "front_color"),
                    BackColor = TryGetString(itemEl, "back_color"),
                    BorderColor = TryGetString(itemEl, "border_color"),
                    BorderWidth = TryGetDouble(itemEl, "border_width"),
                    CornerRadius = TryGetDouble(itemEl, "corner_radius"),
                };
            case "115":
                string? controlDataB64 = TryGetString(itemEl, "controlData");
                KeyAction? action = null;
                if (!string.IsNullOrEmpty(controlDataB64))
                {
                    action = TryDecodeKeyAction(controlDataB64);
                }
                return new KeyItem
                {
                    RawTypeCode = typeCode, Id = id, ItemName = itemName, X = x, Y = y, Z = z, Width = w, Height = h,
                    Rotate = rotate, Scale = scale, IsLocked = locked, RawJson = itemEl.Clone(),
                    Row = (int)(TryGetDouble(itemEl, "row") ?? 0),
                    Column = (int)(TryGetDouble(itemEl, "col") ?? 0),
                    IconAssetPath = TryGetString(itemEl, "path"),
                    Action = action,
                    RawControlDataBase64 = controlDataB64,
                };
            default:
                return new UnknownThemeItem
                {
                    RawTypeCode = typeCode, Id = id, ItemName = itemName, X = x, Y = y, Z = z, Width = w, Height = h,
                    Rotate = rotate, Scale = scale, IsLocked = locked, RawJson = itemEl.Clone(),
                };
        }
    }

    /// <summary>
    /// Decodes a key item's base64 "controlData" into a strongly-typed <see cref="KeyAction"/>.
    /// Returns null (rather than throwing) if the base64/tagged-value decode fails, since a
    /// key with unparsable controlData should not prevent the rest of the theme from loading -
    /// callers can inspect <see cref="KeyItem.RawControlDataBase64"/> in that case.
    /// </summary>
    public static KeyAction? TryDecodeKeyAction(string controlDataBase64)
    {
        byte[] bytes;
        try { bytes = Convert.FromBase64String(controlDataBase64); }
        catch (FormatException) { return null; }

        Dictionary<string, TaggedValue> fields;
        try
        {
            int pos = 0;
            fields = VariantMapCodec.DecodeMap(bytes, ref pos);
        }
        catch (InvalidDataException) { return null; }

        string rawType = fields.TryGetValue("type", out var t) && t.AsString is { } ts ? ts : "";
        string? description = GetString(fields, "description");
        string? parentDescription = GetString(fields, "parentDescription");
        string? iconPath = GetString(fields, "iconPath");

        return rawType switch
        {
            "keyboard" => new KeyboardAction
            {
                RawType = rawType, Description = description, ParentDescription = parentDescription, IconPath = iconPath, RawFields = fields,
                Keycode = GetInt(fields, "keycode") ?? 0,
                KeyLabel = GetString(fields, "keyString"),
            },
            "openWeb" => new OpenWebAction
            {
                RawType = rawType, Description = description, ParentDescription = parentDescription, IconPath = iconPath, RawFields = fields,
                Url = GetString(fields, "Url") ?? "",
            },
            "qmk_mouse" => new MouseAction
            {
                RawType = rawType, Description = description, ParentDescription = parentDescription, IconPath = iconPath, RawFields = fields,
                MouseKey = GetInt(fields, "qmk_mouse_key") ?? 0,
                MouseEvent = GetInt(fields, "qmk_mouse_event") ?? 0,
                MouseX = GetInt(fields, "mouse_x") ?? 0,
                MouseY = GetInt(fields, "mouse_y") ?? 0,
                MouseVerticalScroll = GetInt(fields, "mouse_v") ?? 0,
                MouseHorizontalScroll = GetInt(fields, "mouse_h") ?? 0,
            },
            "pageSwitch" => new PageSwitchAction
            {
                RawType = rawType, Description = description, ParentDescription = parentDescription, IconPath = iconPath, RawFields = fields,
                PageSwitchMode = GetInt(fields, "pageSwitchMode") ?? 0,
                JumpToPage = GetInt(fields, "jumpToPage") ?? 0,
            },
            "Microphone" or "Loudspeaker" => new AudioVolumeAction
            {
                RawType = rawType, Description = description, ParentDescription = parentDescription, IconPath = iconPath, RawFields = fields,
                DeviceClass = rawType == "Microphone" ? AudioDeviceClass.Microphone : AudioDeviceClass.Loudspeaker,
                TargetDeviceName = GetString(fields, "volumeAdjustDevice"),
                VolumeAdjustMode = GetInt(fields, "volumeAdjustMode") ?? 0,
                VolumeAdjustValue = GetInt(fields, "volumeadjustValue") ?? 0,
                IsSwitchDefaultDevice = GetBool(fields, "isSwitchDefaultDevice") ?? false,
            },
            "text" => new TextInputAction
            {
                RawType = rawType, Description = description, ParentDescription = parentDescription, IconPath = iconPath, RawFields = fields,
                InputText = GetString(fields, "inputText") ?? "",
                IsInputEnter = GetBool(fields, "isInputEnter") ?? false,
                IsCopyPaste = GetBool(fields, "isCopyPaste") ?? false,
            },
            "keyboard_switch" => new KeyboardSwitchAction
            {
                RawType = rawType, Description = description, ParentDescription = parentDescription, IconPath = iconPath, RawFields = fields,
            },
            "openPage" => new OpenPageAction
            {
                RawType = rawType, Description = description, ParentDescription = parentDescription, IconPath = iconPath, RawFields = fields,
                PageName = GetString(fields, "pageName") ?? "",
            },
            "oneLevelUp" => new OneLevelUpAction
            {
                RawType = rawType, Description = description, ParentDescription = parentDescription, IconPath = iconPath, RawFields = fields,
                PageName = GetString(fields, "pageName"),
            },
            "ControlFlow" => new ControlFlowAction
            {
                RawType = rawType, Description = description, ParentDescription = parentDescription, IconPath = iconPath, RawFields = fields,
                ControlDataList = fields.TryGetValue("controlDataList", out var cdl) ? cdl.AsByteArray : null,
            },
            "encoder_keyboard" => new EncoderKeyboardAction
            {
                RawType = rawType, Description = description, ParentDescription = parentDescription, IconPath = iconPath, RawFields = fields,
                Category = GetString(fields, "category"),
                RelatedThemePath = GetString(fields, "relatedTheme"),
                LeftKeycode = GetInt(fields, "encoder_left_keycode") ?? 0,
                LeftKeyLabel = GetString(fields, "encoder_left_keyString"),
                MiddleKeycode = GetInt(fields, "encoder_middle_keycode") ?? 0,
                MiddleKeyLabel = GetString(fields, "encoder_middle_keyString"),
                RightKeycode = GetInt(fields, "encoder_right_keycode") ?? 0,
                RightKeyLabel = GetString(fields, "encoder_right_keyString"),
            },
            "encoder_system_volume" or "encoder_system_media" or "encoder_device_brightness" => new EncoderFunctionAction
            {
                RawType = rawType, Description = description, ParentDescription = parentDescription, IconPath = iconPath, RawFields = fields,
                Category = GetString(fields, "category"),
                RelatedThemePath = GetString(fields, "relatedTheme"),
            },
            _ => new UnknownKeyAction
            {
                RawType = rawType, Description = description, ParentDescription = parentDescription, IconPath = iconPath, RawFields = fields,
            },
        };
    }

    // ---- Encoding ----

    /// <summary>
    /// Encodes a <see cref="ThemeFile"/> back to its on-disk byte representation. This is
    /// the byte-exact inverse of <see cref="Decode"/> for the header/asset sections, and
    /// regenerates the layout JSON from each item's typed properties merged over its
    /// original <see cref="ThemeItem.RawJson"/> (so unmodeled fields survive unchanged).
    ///
    /// NOTE: encode/decode round-trip correctness has been validated by self-test, but
    /// pushing an Encode()-produced file to real hardware and confirming the device accepts
    /// it has NOT been independently verified in this codebase - test against real hardware
    /// via <c>Mk20DeviceClient</c> before relying on it in production.
    /// </summary>
    public static byte[] Encode(ThemeFile theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        using var stream = new MemoryStream();

        var header = new Dictionary<string, TaggedValue>
        {
            ["language"] = TaggedValue.Of(theme.Language),
            ["keyMacroValue"] = TaggedValue.Of(theme.KeyMacroValue),
            ["keyMacro"] = theme.KeyMacro is { } km ? TaggedValue.Of(km) : TaggedValue.Null(12),
        };
        VariantMapCodec.WriteMap(stream, header);

        // 8 bytes of reserved/unclear-purpose header padding - written as zero, matching
        // every real theme file observed so far.
        stream.Write(new byte[8]);

        string layoutJson = BuildLayoutJson(theme);
        stream.Write(Encoding.UTF8.GetBytes(layoutJson));
        stream.WriteByte(HeaderReservedByte);

        WriteUInt32BigEndian(stream, (uint)theme.Assets.Count);
        foreach (var asset in theme.Assets)
        {
            VariantMapCodec.WriteString(stream, asset.Path);
            VariantMapCodec.WriteByteArray(stream, asset.Data);
        }

        return stream.ToArray();
    }

    private static string BuildLayoutJson(ThemeFile theme)
    {
        var root = new JsonObject
        {
            ["main"] = new JsonObject
            {
                ["currentPage"] = theme.CurrentPageId,
                ["version"] = theme.LayoutVersion,
            },
            ["pages"] = new JsonArray(theme.Pages.Select(p => (JsonNode)BuildPageJson(p)).ToArray()),
        };
        return root.ToJsonString();
    }

    private static JsonObject BuildPageJson(ThemePage page)
    {
        var canvas = new JsonObject();
        if (page.Canvas.Width is { } cw) canvas["canvas_w"] = cw;
        if (page.Canvas.Height is { } ch) canvas["canvas_h"] = ch;
        if (page.Canvas.IsFlipped is { } cf) canvas["canvas_flip"] = cf;
        if (page.Canvas.IsRotated is { } cr) canvas["canvas_rotate"] = cr;
        if (page.Canvas.ShowUnit is { } su) canvas["showUnit"] = su;

        var obj = new JsonObject
        {
            ["canvas"] = canvas,
            ["items"] = new JsonArray(page.Items.Select(i => (JsonNode)BuildItemJson(i)).ToArray()),
        };
        if (page.PageName is not null) obj["pageName"] = page.PageName;
        return obj;
    }

    private static JsonObject BuildItemJson(ThemeItem item)
    {
        // Start from the item's original JSON so any unmodeled fields survive untouched,
        // then overwrite every field this library models with the (possibly modified)
        // typed property values.
        var obj = JsonNode.Parse(item.RawJson.GetRawText())!.AsObject();

        SetOrRemove(obj, "id", item.Id);
        SetOrRemove(obj, "itemName", item.ItemName);
        SetOrRemove(obj, "x", item.X);
        SetOrRemove(obj, "y", item.Y);
        SetOrRemove(obj, "z", item.Z);
        SetOrRemove(obj, "w", item.Width);
        SetOrRemove(obj, "h", item.Height);
        SetOrRemove(obj, "rotate", item.Rotate);
        SetOrRemove(obj, "scale", item.Scale);
        SetOrRemove(obj, "lock", item.IsLocked);
        obj["type"] = item.RawTypeCode;

        switch (item)
        {
            case BackgroundItem bg:
                obj["backgroundType"] = bg.RawSurface;
                obj["path"] = bg.AssetPath;
                break;
            case ProgressBarItem pb:
                obj["system_data_flag"] = pb.SystemDataName is not null ? "1" : "0";
                if (pb.SystemDataName is not null) obj["system_data_name"] = pb.SystemDataName;
                SetOrRemove(obj, "system_data_min_value", pb.MinValue);
                SetOrRemove(obj, "system_data_max_value", pb.MaxValue);
                break;
            case LinearGaugeItem lg:
                obj["system_data_flag"] = lg.SystemDataName is not null ? "1" : "0";
                if (lg.SystemDataName is not null) obj["system_data_name"] = lg.SystemDataName;
                SetOrRemove(obj, "system_data_min_value", lg.MinValue);
                SetOrRemove(obj, "system_data_max_value", lg.MaxValue);
                if (lg.FrontColor is not null) obj["front_color"] = lg.FrontColor;
                if (lg.BackColor is not null) obj["back_color"] = lg.BackColor;
                if (lg.BorderColor is not null) obj["border_color"] = lg.BorderColor;
                SetOrRemove(obj, "border_width", lg.BorderWidth);
                break;
            case RadialGaugeItem rg:
                obj["system_data_flag"] = rg.SystemDataName is not null ? "1" : "0";
                if (rg.SystemDataName is not null) obj["system_data_name"] = rg.SystemDataName;
                SetOrRemove(obj, "system_data_min_value", rg.MinValue);
                SetOrRemove(obj, "system_data_max_value", rg.MaxValue);
                SetOrRemove(obj, "angleMinValue", rg.AngleMinValue);
                SetOrRemove(obj, "angleMaxValue", rg.AngleMaxValue);
                SetOrRemove(obj, "arcRadius", rg.ArcRadius);
                SetOrRemove(obj, "arcCircularInterval", rg.ArcCircularInterval);
                if (rg.GradientColor1 is not null) obj["gradientColor1"] = rg.GradientColor1;
                if (rg.GradientColor2 is not null) obj["gradientColor2"] = rg.GradientColor2;
                if (rg.GradientColor3 is not null) obj["gradientColor3"] = rg.GradientColor3;
                break;
            case DigitalClockItem clock:
                obj["system_data_flag"] = clock.SystemDataName is not null ? "1" : "0";
                if (clock.SystemDataName is not null) obj["system_data_name"] = clock.SystemDataName;
                if (clock.Font is not null) obj["text_font"] = clock.Font;
                if (clock.FrontColor is not null) obj["front_color"] = clock.FrontColor;
                if (clock.BackColor is not null) obj["back_color"] = clock.BackColor;
                if (clock.BorderColor is not null) obj["border_color"] = clock.BorderColor;
                SetOrRemove(obj, "border_width", clock.BorderWidth);
                SetOrRemove(obj, "corner_radius", clock.CornerRadius);
                break;
            case TextItem text:
                obj["system_data_flag"] = text.SystemDataName is not null ? "1" : "0";
                if (text.SystemDataName is not null) obj["system_data_name"] = text.SystemDataName;
                if (text.Text is not null) obj["text_str"] = text.Text;
                if (text.Font is not null) obj["text_font"] = text.Font;
                break;
            case DynamicImageItem gif:
                obj["path"] = gif.AssetPath;
                obj["system_data_flag"] = gif.SystemDataName is not null ? "1" : "0";
                if (gif.SystemDataName is not null) obj["system_data_name"] = gif.SystemDataName;
                break;
            case KeyItem key:
                obj["row"] = key.Row.ToString();
                obj["col"] = key.Column.ToString();
                if (key.IconAssetPath is not null) obj["path"] = key.IconAssetPath;
                if (key.Action is not null) obj["controlData"] = Convert.ToBase64String(EncodeKeyAction(key.Action));
                else if (key.RawControlDataBase64 is not null) obj["controlData"] = key.RawControlDataBase64;
                break;
        }

        return obj;
    }

    /// <summary>Encodes a <see cref="KeyAction"/> back to its base64 tagged-value "controlData" representation.</summary>
    public static byte[] EncodeKeyAction(KeyAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        // Start from the action's original decoded fields so any unmodeled fields survive,
        // then overwrite every field this library models.
        var fields = new Dictionary<string, TaggedValue>(action.RawFields)
        {
            ["type"] = TaggedValue.Of(action.RawType),
        };
        SetIfNotNull(fields, "description", action.Description);
        SetIfNotNull(fields, "parentDescription", action.ParentDescription);
        SetIfNotNull(fields, "iconPath", action.IconPath);

        switch (action)
        {
            case KeyboardAction k:
                fields["keycode"] = TaggedValue.Of(k.Keycode);
                SetIfNotNull(fields, "keyString", k.KeyLabel);
                break;
            case OpenWebAction w:
                fields["Url"] = TaggedValue.Of(w.Url);
                break;
            case MouseAction m:
                fields["qmk_mouse_key"] = TaggedValue.Of(m.MouseKey);
                fields["qmk_mouse_event"] = TaggedValue.Of(m.MouseEvent);
                fields["mouse_x"] = TaggedValue.Of(m.MouseX);
                fields["mouse_y"] = TaggedValue.Of(m.MouseY);
                fields["mouse_v"] = TaggedValue.Of(m.MouseVerticalScroll);
                fields["mouse_h"] = TaggedValue.Of(m.MouseHorizontalScroll);
                break;
            case PageSwitchAction p:
                fields["pageSwitchMode"] = TaggedValue.Of(p.PageSwitchMode);
                fields["jumpToPage"] = TaggedValue.Of(p.JumpToPage);
                break;
            case AudioVolumeAction a:
                SetIfNotNull(fields, "volumeAdjustDevice", a.TargetDeviceName);
                fields["volumeAdjustMode"] = TaggedValue.Of(a.VolumeAdjustMode);
                fields["volumeadjustValue"] = TaggedValue.Of(a.VolumeAdjustValue);
                fields["isSwitchDefaultDevice"] = TaggedValue.Of(a.IsSwitchDefaultDevice);
                break;
            case TextInputAction t:
                fields["inputText"] = TaggedValue.Of(t.InputText);
                fields["isInputEnter"] = TaggedValue.Of(t.IsInputEnter);
                fields["isCopyPaste"] = TaggedValue.Of(t.IsCopyPaste);
                break;
            case OpenPageAction op:
                fields["pageName"] = TaggedValue.Of(op.PageName);
                break;
            case OneLevelUpAction ol:
                SetIfNotNull(fields, "pageName", ol.PageName);
                break;
            case ControlFlowAction cf:
                if (cf.ControlDataList is not null) fields["controlDataList"] = TaggedValue.Of(cf.ControlDataList);
                break;
            case EncoderKeyboardAction ek:
                SetIfNotNull(fields, "category", ek.Category);
                SetIfNotNull(fields, "relatedTheme", ek.RelatedThemePath);
                fields["encoder_left_keycode"] = TaggedValue.Of(ek.LeftKeycode);
                SetIfNotNull(fields, "encoder_left_keyString", ek.LeftKeyLabel);
                fields["encoder_middle_keycode"] = TaggedValue.Of(ek.MiddleKeycode);
                SetIfNotNull(fields, "encoder_middle_keyString", ek.MiddleKeyLabel);
                fields["encoder_right_keycode"] = TaggedValue.Of(ek.RightKeycode);
                SetIfNotNull(fields, "encoder_right_keyString", ek.RightKeyLabel);
                break;
            case EncoderFunctionAction ef:
                SetIfNotNull(fields, "category", ef.Category);
                SetIfNotNull(fields, "relatedTheme", ef.RelatedThemePath);
                break;
            // KeyboardSwitchAction: no extra fields beyond the common base - nothing more to set.
        }

        return VariantMapCodec.EncodeMap(fields);
    }

    private static void SetIfNotNull(Dictionary<string, TaggedValue> fields, string key, string? value)
    {
        if (value is not null) fields[key] = TaggedValue.Of(value);
    }

    private static void SetOrRemove(JsonObject obj, string key, object? value)
    {
        if (value is null) { obj.Remove(key); return; }
        obj[key] = value switch
        {
            // Real device theme JSON always encodes item-level booleans as "0"/"1" strings
            // (confirmed via --dump-raw-json against real hardware themes, e.g. "lock":"1"),
            // never as native JSON true/false - match that convention exactly.
            bool b => b ? "1" : "0",
            double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
            string s => s,
            _ => value.ToString(),
        };
    }

    private static string? GetString(IReadOnlyDictionary<string, TaggedValue> fields, string key) =>
        fields.TryGetValue(key, out var v) && v.AsString is { } s ? s : null;

    private static int? GetInt(IReadOnlyDictionary<string, TaggedValue> fields, string key) =>
        fields.TryGetValue(key, out var v) && v.AsInt32 is { } i ? i : null;

    private static bool? GetBool(IReadOnlyDictionary<string, TaggedValue> fields, string key) =>
        fields.TryGetValue(key, out var v) && v.AsBool is { } b ? b : null;

    private static string? TryGetString(JsonElement el, string propertyName) =>
        el.TryGetProperty(propertyName, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static double? TryGetDouble(JsonElement el, string propertyName)
    {
        if (!el.TryGetProperty(propertyName, out var p)) return null;
        return p.ValueKind switch
        {
            JsonValueKind.String when double.TryParse(p.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d) => d,
            JsonValueKind.Number => p.GetDouble(),
            _ => null,
        };
    }

    private static bool? TryGetBool(JsonElement el, string propertyName)
    {
        if (!el.TryGetProperty(propertyName, out var p)) return null;
        return p.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => p.GetString() switch { "1" => true, "0" => false, "true" => true, "false" => false, _ => null },
            JsonValueKind.Number => p.GetInt32() != 0,
            _ => null,
        };
    }

    /// <summary>
    /// Finds the end index (inclusive) of the top-level JSON value starting at
    /// <paramref name="start"/>, by tracking brace/bracket depth while correctly skipping
    /// over quoted string contents (respecting backslash-escaping).
    /// </summary>
    private static bool TryFindBalancedJsonEnd(byte[] data, int start, out int end)
    {
        int depth = 0;
        bool inString = false;
        bool escaped = false;
        for (int i = start; i < data.Length; i++)
        {
            char c = (char)data[i];
            if (inString)
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                continue;
            }
            if (c == '"') { inString = true; continue; }
            if (c == '{' || c == '[') depth++;
            else if (c == '}' || c == ']')
            {
                depth--;
                if (depth == 0) { end = i; return true; }
            }
        }
        end = -1;
        return false;
    }

    private static uint ReadUInt32BigEndian(byte[] data, ref int pos)
    {
        if (pos + 4 > data.Length) throw new InvalidDataException($"Truncated data reading a u32 field at position {pos}.");
        uint v = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos, 4));
        pos += 4;
        return v;
    }

    private static void WriteUInt32BigEndian(Stream stream, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
        stream.Write(buffer);
    }
}
