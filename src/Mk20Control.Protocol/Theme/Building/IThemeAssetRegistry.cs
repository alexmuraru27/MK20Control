namespace Mk20Control.Protocol.Theme.Building;

/// <summary>
/// Shared capability needed by item builders (<see cref="KeyItemBuilder"/>,
/// <see cref="BackgroundItemBuilder"/>, etc.) to register new binary assets and allocate
/// unique item ids - implemented by both <see cref="ThemeBuilder"/> (building a theme from
/// scratch) and <see cref="ThemeEditor"/> (editing an existing theme), so the same item
/// builders work in both contexts.
/// </summary>
public interface IThemeAssetRegistry
{
    /// <summary>Registers a new binary asset and returns its virtual path for use in an item (e.g. <c>KeyItem.IconAssetPath</c>).</summary>
    string RegisterAsset(string suggestedFileName, byte[] data);

    /// <summary>Allocates a new unique item id string.</summary>
    string AllocateItemId();
}
