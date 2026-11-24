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

    /// <summary>The backends added with <see cref="AddBackend"/>.</summary>
    public IReadOnlyList<InputBackend> Backends => _backends;

    private void EnsureBuiltinDevices()
    {
        if (_builtinDevicesAdded) return;
        _builtinDevicesAdded = true;
        _devices.Add(new InputDeviceInfo("keyboard_mouse", InputDeviceKind.KeyboardMouse, -1, "Keyboard and mouse") { Role = DeviceRole.KeyboardMouse });
        _devices.Add(new InputDeviceInfo("touch", InputDeviceKind.Touch, -1, "Touchscreen") { Role = DeviceRole.Touchscreen, IsConnected = DisplayServer.IsTouchscreenAvailable() });
        _devices.Add(new InputDeviceInfo("pen", InputDeviceKind.Pen, -1, "Pen") { Role = DeviceRole.Pen });
        foreach (var index in Input.GetConnectedJoypads()) UpdateJoypadDevice(index, true, GuidOf(index));
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

    private void UpdateJoypadDevice(int index, bool connected, string guid)
    {
        if (!connected)
        {
            var gone = GetJoypadDevice(index);
            if (gone != null && gone.IsConnected)
            {
                gone.IsConnected = false;
                DeviceDisconnected?.Invoke(gone);
            }
            return;
        }

        bool known = Input.GetConnectedJoypads().Contains(index);
        string name = known ? Input.GetJoyName(index) : $"Joypad {index}";
        string id = !string.IsNullOrEmpty(guid) ? $"joypad:{guid}:{index}" : $"joypad:{index}";
        InputDeviceInfo device = null;
        foreach (var d in _devices)
            if (d.Id == id) { device = d; break; }
        if (device == null)
        {
            device = new InputDeviceInfo(id, InputDeviceKind.Joypad, index, name) { Guid = guid ?? string.Empty };
            _devices.Add(device);
        }
        device.Name = name;
        device.IsConnected = true;
        device.Profile = FindProfile(name, guid);
        device.Role = device.Profile?.Role ?? InputDeviceInfo.GuessJoypadRole(name);
        if (known)
        {
            device.CanVibrate = Input.HasJoyVibration(index);
            device.HasMotionSensors = Input.HasJoyMotionSensors(index);
            device.HasLight = Input.HasJoyLight(index);
        }
        DeviceConnected?.Invoke(device);
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
                foreach (var p in _players) { p.AcceptKeyboardMouse = p == player; if (p != player) p.ClearRawInput(); }
                break;
            case InputDeviceKind.Touch:
                foreach (var p in _players) p.AcceptTouch = p == player;
                break;
            case InputDeviceKind.Custom:
                foreach (var p in _players)
                    if (p != player && p.OwnedCustomDevices.Remove(device.Id)) p.Raw.ClearCustomDevice(device.DeviceType, device.Index);
                player.OwnedCustomDevices.Add(device.Id);
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

    /// <summary>Adds a backend; it is polled every tick before the players.</summary>
    public void AddBackend(InputBackend backend)
    {
        if (backend == null || _backends.Contains(backend)) return;
        backend.System = this;
        _backends.Add(backend);
        backend.Attached();
    }

    /// <summary>Removes a backend and disconnects its devices.</summary>
    public void RemoveBackend(InputBackend backend)
    {
        if (backend == null || !_backends.Remove(backend)) return;
        foreach (var device in _devices.ToArray())
            if (device.Backend == backend && device.IsConnected) UnregisterCustomDevice(device.DeviceType, device.Index);
        backend.Detached();
        backend.System = null;
    }

    internal InputDeviceInfo RegisterCustomDevice(InputBackend backend, string deviceType, int index, string name, string guid)
    {
        EnsureBuiltinDevices();
        string id = $"{deviceType}:{index}";
        var device = GetDevice(id);
        if (device == null)
        {
            device = new InputDeviceInfo(id, InputDeviceKind.Custom, index, name) { DeviceType = deviceType, Backend = backend };
            _devices.Add(device);
        }
        device.Name = name;
        device.Guid = guid ?? string.Empty;
        device.IsConnected = true;
        device.Profile = FindProfile(name, guid);
        device.Role = device.Profile?.Role ?? (deviceType == "xr" ? DeviceRole.XRController : DeviceRole.Other);
        DeviceConnected?.Invoke(device);
        return device;
    }

    internal void UnregisterCustomDevice(string deviceType, int index)
    {
        var device = GetDevice($"{deviceType}:{index}");
        if (device == null || !device.IsConnected) return;
        device.IsConnected = false;
        foreach (var p in _players) p.Raw.ClearCustomDevice(deviceType, index);
        DeviceDisconnected?.Invoke(device);
    }

    /// <summary>
    /// Writes a control value of a backend device and routes it to the device's owner, or — when nobody
    /// owns it — to players that take unowned devices.
    /// </summary>
    public void SetDeviceControl(string deviceType, int index, string control, Vector3 value)
        => SetDeviceControl(deviceType, index, control, new Vector4(value.X, value.Y, value.Z, 0f));

    /// <summary>Sets a control of a backend device (up to four components, e.g. a rotation as x, y, z, w).</summary>
    public void SetDeviceControl(string deviceType, int index, string control, Vector4 value)
    {
        EnsureDefaultPlayer();
        string id = $"{deviceType}:{index}";
        var key = SourceKey.ForDeviceControl(deviceType, control, index);
        InputPlayer owner = null;
        foreach (var p in _players)
            if (p.OwnedCustomDevices.Contains(id)) { owner = p; break; }
        if (owner != null) { owner.Raw.Set(key, value); return; }
        foreach (var p in _players)
            if (p.ReceivesUnownedJoypads) p.Raw.Set(key, value);
    }

    private void PollBackends(double delta)
    {
        for (int i = 0; i < _backends.Count; i++)
        {
            try { _backends[i].Poll(delta); }
            catch (Exception e) { GD.PushError($"[GodotEnhancedInput] backend {_backends[i].Name} failed: {e}"); }
        }
    }

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
