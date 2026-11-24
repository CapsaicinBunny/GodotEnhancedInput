using System;
using System.Collections.Generic;
using Godot;

namespace GodotEnhancedInput;

public partial class EnhancedInput
{
    /// <summary>Raised when a device appears or reconnects.</summary>
    public event Action<InputDeviceInfo> DeviceConnected;

    /// <summary>Raised when a device disconnects.</summary>
    public event Action<InputDeviceInfo> DeviceDisconnected;

    /// <summary>Raised when a device connects or disconnects (device id, connected).</summary>
    [Signal] public delegate void DeviceConnectionChangedEventHandler(string deviceId, bool connected);

    /// <summary>
    /// Raised when a player loses the device it was using (its pad disconnected) and has no other device
    /// of that kind — the moment to pause and show "reconnect your controller".
    /// </summary>
    [Signal] public delegate void PlayerDeviceLostEventHandler(InputPlayer player, string deviceId);

    /// <summary>Raised when a player's lost device comes back (also under another index).</summary>
    [Signal] public delegate void PlayerDeviceRegainedEventHandler(InputPlayer player, string deviceId);

    /// <summary>Profiles for wheels, pedals, flight sticks and other devices (first match wins).</summary>
    [Export] public Godot.Collections.Array<DeviceProfile> DeviceProfiles { get; set; } = new();

    private readonly List<InputDeviceInfo> _devices = new();
    private readonly List<InputBackend> _backends = new();
    private bool _builtinDevicesAdded;

    /// <summary>Every device known so far, connected or not: keyboard and mouse, touchscreen, pen, joypads, backend devices.</summary>
    public IReadOnlyList<InputDeviceInfo> Devices
    {
        get
        {
            EnsureBuiltinDevices();
            return _devices;
        }
    }

    private readonly List<int> _connectedJoypads = new();
    private bool _connectedJoypadsValid;
    private double _joypadCheckTimer;

    /// <summary>The joypad indices connected now (cached; refreshed on connection changes and checked every few seconds).</summary>
    public List<int> ConnectedJoypads
    {
        get
        {
            if (!_connectedJoypadsValid)
            {
                _connectedJoypads.Clear();
                foreach (var d in Input.GetConnectedJoypads()) _connectedJoypads.Add(d);
                _connectedJoypadsValid = true;
            }
            return _connectedJoypads;
        }
    }

    /// <summary>The backends added with <see cref="AddBackend"/>.</summary>
    public IReadOnlyList<InputBackend> Backends => _backends;

    private void EnsureBuiltinDevices()
    {
        if (_builtinDevicesAdded) return;
        _builtinDevicesAdded = true;
        _devices.Add(new InputDeviceInfo("keyboard_mouse", InputDeviceKind.KeyboardMouse, -1, "Keyboard and mouse") { Role = DeviceRole.KeyboardMouse });
        _devices.Add(new InputDeviceInfo("touch", InputDeviceKind.Touch, -1, "Touchscreen") { Role = DeviceRole.Touchscreen, IsConnected = DisplayServer.IsTouchscreenAvailable() });
        _devices.Add(new InputDeviceInfo("pen", InputDeviceKind.Pen, -1, "Pen") { Role = DeviceRole.Pen });
        foreach (var index in Input.GetConnectedJoypads()) ConnectJoypadRecord(index, ReadJoypad(index, GuidOf(index), null, null), out _, out _);
    }

    /// <summary>The device with an id, or null.</summary>
    public InputDeviceInfo GetDevice(string id)
    {
        EnsureBuiltinDevices();
        foreach (var device in _devices)
            if (device.Id == id) return device;
        return null;
    }

    /// <summary>The joypad device currently at a Godot device index, or null.</summary>
    public InputDeviceInfo GetJoypadDevice(int index)
    {
        EnsureBuiltinDevices();
        InputDeviceInfo found = null;
        foreach (var device in _devices)
            if (device.Kind == InputDeviceKind.Joypad && device.Index == index && (found == null || device.IsConnected)) found = device;
        return found;
    }

    private DeviceProfile FindProfile(string name, string guid)
    {
        foreach (var profile in DeviceProfiles)
            if (profile != null && profile.Matches(name, guid)) return profile;
        return null;
    }

    // ------------------------------------------------------------------
    //  Pairing any device with a player
    // ------------------------------------------------------------------

    /// <summary>
    /// Gives a device to one player: a joypad (see <see cref="AssignJoypad(InputPlayer, int)"/>), the
    /// keyboard and mouse or the touchscreen (only this player listens to them afterwards), or a backend
    /// device (only this player receives it).
    /// </summary>
    public void AssignDevice(InputPlayer player, InputDeviceInfo device)
    {
        if (player == null || device == null) return;
        EnsureDefaultPlayer();
        switch (device.Kind)
        {
            case InputDeviceKind.Joypad:
                AssignJoypad(player, device.Index, device.Guid);
                break;
            case InputDeviceKind.KeyboardMouse:
            case InputDeviceKind.Pen:
                foreach (var p in _players)
                {
                    if (p != player && p.AcceptKeyboardMouse) p.Raw.ClearKeyboardMouse(InputEndReason.DeviceReassigned);
                    p.AcceptKeyboardMouse = p == player;
                }
                break;
            case InputDeviceKind.Touch:
                foreach (var p in _players)
                {
                    // Fingers held now belong to the previous player; their releases would go elsewhere.
                    if (p != player && p.AcceptTouch) p.Raw.ClearTouch(InputEndReason.DeviceReassigned);
                    p.AcceptTouch = p == player;
                }
                break;
            case InputDeviceKind.Custom:
                // Everyone else lets go: its owner, and players that received it while nobody owned it.
                foreach (var p in _players)
                {
                    if (p == player) continue;
                    p.OwnedCustomDevices.Remove(device.Id);
                    p.ReleaseHapticDevice(device);
                    p.Raw.ClearCustomDevice(device.DeviceType, device.Index, InputEndReason.DeviceReassigned);
                }
                player.OwnedCustomDevices.Add(device.Id);
                device.Owner = player;
                break;
        }
    }

    /// <summary>The player a device belongs to, or null (for keyboard/mouse and touch: the single player listening, if exactly one).</summary>
    public InputPlayer GetDeviceOwner(InputDeviceInfo device)
    {
        if (device == null) return null;
        EnsureDefaultPlayer();
        switch (device.Kind)
        {
            case InputDeviceKind.Joypad: return GetJoypadOwner(device.Index);
            case InputDeviceKind.Custom:
                foreach (var p in _players)
                    if (p.OwnedCustomDevices.Contains(device.Id)) return p;
                return null;
            default:
            {
                InputPlayer only = null;
                foreach (var p in _players)
                {
                    bool listens = device.Kind == InputDeviceKind.Touch ? p.AcceptTouch : p.AcceptKeyboardMouse;
                    if (!listens) continue;
                    if (only != null) return null;
                    only = p;
                }
                return only;
            }
        }
    }

    // ------------------------------------------------------------------
    //  Custom backends
    // ------------------------------------------------------------------

    /// <summary>A copy of a joypad axis event with the device profile's calibration and inversion applied, or the event itself.</summary>
    private InputEvent ApplyDeviceProfile(InputEvent @event)
    {
        if (@event is not InputEventJoypadMotion motion || DeviceProfiles.Count == 0) return @event;
        var profile = GetJoypadDevice(motion.Device)?.Profile;
        if (profile == null) return @event;
        var adjusted = (InputEventJoypadMotion)motion.Duplicate();
        adjusted.AxisValue = profile.AdjustAxis((int)motion.Axis, motion.AxisValue);
        return adjusted;
    }
}
