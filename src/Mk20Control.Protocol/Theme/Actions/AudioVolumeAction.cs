namespace Mk20Control.Protocol.Theme.Actions;

/// <summary>Which class of OS audio device an <see cref="AudioVolumeAction"/> controls.</summary>
public enum AudioDeviceClass
{
    /// <summary>"type": "Microphone" - a recording/input device.</summary>
    Microphone,

    /// <summary>"type": "Loudspeaker" - a playback/output device.</summary>
    Loudspeaker,
}

/// <summary>
/// A key assigned to adjust the volume of a specific, named OS audio device
/// ("type": "Microphone" or "Loudspeaker") - confirmed to bind to a concrete device by its
/// OS-reported name (e.g. "Speakers (Logitech G733 Gaming Headset)"), not just "system volume"
/// in the abstract.
/// </summary>
public sealed record AudioVolumeAction : KeyAction
{
    public required AudioDeviceClass DeviceClass { get; init; }

    /// <summary>The exact OS device name this action targets, as captured from the "volumeAdjustDevice" field.</summary>
    public string? TargetDeviceName { get; init; }

    public int VolumeAdjustMode { get; init; }
    public int VolumeAdjustValue { get; init; }
    public bool IsSwitchDefaultDevice { get; init; }
}
