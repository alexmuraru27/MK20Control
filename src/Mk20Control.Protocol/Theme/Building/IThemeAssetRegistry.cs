namespace Mk20Control.Protocol.Theme.Building;

/// <summary>
/// Shared capability needed by item builders (<see cref="KeyItemBuilder"/>,
/// <see cref="BackgroundItemBuilder"/>, etc.) to register new binary assets and allocate
/// unique item ids - implemented by both <see cref="ThemeBuilder"/> (building a theme from
/// scratch) and <see cref="ThemeEditor"/> (editing an existing theme), so the same item
/// builders work in both contexts.
///
/// INTERNAL: this is plumbing, not API. Asset paths follow per-item-type conventions the
/// device is strict about (a key icon, a main-screen background and a secondary-screen GIF
/// each live under a different namespace), so callers describe *what* they are adding - via
/// <see cref="KeyItemBuilder.Icon"/>, <see cref="BackgroundItemBuilder.MainScreen"/> and so
/// on - and the matching builder puts it in the right place.
/// </summary>
internal interface IThemeAssetRegistry
{
    /// <summary>Registers a new binary asset and returns its virtual path for use in an item (e.g. <c>KeyItem.IconAssetPath</c>).</summary>
    string RegisterAsset(string suggestedFileName, byte[] data);

    /// <summary>
    /// Registers a new binary asset under an EXACT, caller-specified virtual path (not
    /// derived/namespaced automatically like <see cref="RegisterAsset"/>) - for item types
    /// confirmed to use a different asset-path convention than key icons, e.g.
    /// <c>BackgroundItem</c>'s confirmed real path <c>/theme/MK20-PLUS/MainScreen/&lt;file&gt;</c>
    /// (see PROTOCOL_WAVESHARE_MK20.md §7.1). Returns <paramref name="fullPath"/> unchanged
    /// for convenience. If the same path is registered twice with different bytes, the
    /// later registration silently overwrites the former (caller is expected to pass unique
    /// paths when that matters).
    /// </summary>
    string RegisterAssetAtPath(string fullPath, byte[] data);

    /// <summary>Allocates a new unique item id string.</summary>
    string AllocateItemId();
}
