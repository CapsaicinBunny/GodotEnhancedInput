using System;
using Godot;

namespace GodotEnhancedInput;

/// <summary>Which table of raw input a <see cref="SourceKey"/> addresses.</summary>
public enum RawKind
{
    PhysicalKey,
    LogicalKey,
    KeyLabel,
    MouseButton,
    MouseMotion,
    MousePosition,
    JoyButton,
    JoyAxis,
    InputMapAction,
    Virtual,
}

/// <summary>
/// Identifies one raw input channel: a key, a button, an axis, mouse motion, an InputMap action or a
/// virtual control. Sources read raw input through these keys, and consumption between contexts is
/// tracked per key.
/// </summary>
public readonly record struct SourceKey(RawKind Kind, long Code, int Device, string Name)
{
    /// <summary>Device value meaning "any device the player owns".</summary>
    public const int AnyDevice = -1;

    public static SourceKey ForKey(Key key, KeyMatch match = KeyMatch.Physical) => match switch
    {
        KeyMatch.Logical => new SourceKey(RawKind.LogicalKey, (long)key, AnyDevice, null),
        KeyMatch.Label => new SourceKey(RawKind.KeyLabel, (long)key, AnyDevice, null),
        _ => new SourceKey(RawKind.PhysicalKey, (long)key, AnyDevice, null),
    };

    public static SourceKey ForMouseButton(MouseButton button) => new(RawKind.MouseButton, (long)button, AnyDevice, null);
    public static SourceKey MouseMotion => new(RawKind.MouseMotion, 0, AnyDevice, null);
    public static SourceKey MousePosition => new(RawKind.MousePosition, 0, AnyDevice, null);
    public static SourceKey ForJoyButton(JoyButton button, int device = AnyDevice) => new(RawKind.JoyButton, (long)button, device, null);
    public static SourceKey ForJoyAxis(JoyAxis axis, int device = AnyDevice) => new(RawKind.JoyAxis, (long)axis, device, null);
    public static SourceKey ForInputMapAction(string action) => new(RawKind.InputMapAction, 0, AnyDevice, action ?? string.Empty);
    public static SourceKey ForVirtual(string name) => new(RawKind.Virtual, 0, AnyDevice, name ?? string.Empty);

    /// <summary>True for joypad channels, which are stored per device.</summary>
    public bool IsJoypad => Kind is RawKind.JoyButton or RawKind.JoyAxis;

    /// <summary>The same channel with the device cleared, as used for consumption between contexts.</summary>
    public SourceKey WithoutDevice() => Device == AnyDevice ? this : this with { Device = AnyDevice };

    public override string ToString() => Kind switch
    {
        RawKind.PhysicalKey or RawKind.LogicalKey or RawKind.KeyLabel => $"{Kind}:{(Key)Code}",
        RawKind.MouseButton => $"Mouse:{(MouseButton)Code}",
        RawKind.JoyButton => $"Joy{(Device < 0 ? "*" : Device.ToString())}:{(JoyButton)Code}",
        RawKind.JoyAxis => $"Joy{(Device < 0 ? "*" : Device.ToString())}:{(JoyAxis)Code}",
        RawKind.InputMapAction => $"InputMap:{Name}",
        RawKind.Virtual => $"Virtual:{Name}",
        _ => Kind.ToString(),
    };
}

/// <summary>Reads raw input channels. Implemented by the player, which applies consumption and blocking.</summary>
public interface ISourceReader
{
    /// <summary>The current value of a channel (scalar channels use X).</summary>
    Vector3 Read(in SourceKey key);
}
