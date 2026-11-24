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

    private InputDeviceInfo UpdateJoypadDevice(int index, bool connected, string guid, string serial = null, string name = null)
    {
        if (!connected)
        {
            var gone = GetJoypadDevice(index);
            if (gone != null && gone.IsConnected)
            {
                gone.IsConnected = false;
                DeviceDisconnected?.Invoke(gone);
                EmitSignal(SignalName.DeviceConnectionChanged, gone.Id, false);
            }
            return gone;
        }

        bool known = Input.GetConnectedJoypads().Contains(index);
        name ??= known ? Input.GetJoyName(index) : $"Joypad {index}";
        guid ??= string.Empty;
        if (serial == null && known)
        {
            var info = Input.GetJoyInfo(index);
            serial = info.TryGetValue("serial_number", out var s) ? s.AsString() : string.Empty;
        }
        serial ??= string.Empty;

        var device = FindReturningJoypad(index, guid, serial);
        if (device == null)
        {
            device = new InputDeviceInfo(NewJoypadId(index, guid, serial), InputDeviceKind.Joypad, index, name) { Guid = guid, Serial = serial };
            _devices.Add(device);
        }
        device.PreviousIndex = device.Index != index ? device.Index : -1;
        device.Index = index;
        device.Name = name;
        device.IsConnected = true;
        device.ConnectionCount++;
        device.ConnectedAtMs = Time.GetTicksMsec();
        device.Profile = FindProfile(name, guid);
        device.Role = device.Profile?.Role ?? InputDeviceInfo.GuessJoypadRole(name);
        if (known)
        {
            var (vendor, product) = ControllerDetection.ReadIds(index);
            device.VendorId = vendor;
            device.ProductId = product;
            device.Family = ControllerDetection.Detect(index);
            device.CanVibrate = Input.HasJoyVibration(index);
            device.HasMotionSensors = Input.HasJoyMotionSensors(index);
            device.HasLight = Input.HasJoyLight(index);
            device.Features = ControllerDetection.AvailableFeatures(index, device.Family);
        }
        else
        {
            device.Family = ControllerDetection.Detect(name);
            device.Features = ControllerDetection.HardwareFeatures(device.Family) & (ControllerFeatures.Touchpad | ControllerFeatures.BackButtons | ControllerFeatures.ShareButton);
        }
        device.HardwareFeatures = ControllerDetection.HardwareFeatures(device.Family);
        DeviceConnected?.Invoke(device);
        EmitSignal(SignalName.DeviceConnectionChanged, device.Id, true);
        return device;
    }

    /// <summary>
    /// A disconnected pad that is this one coming back: same serial number if known; otherwise the same
    /// identity (GUID), preferring the one last seen at this index.
    /// </summary>
    private InputDeviceInfo FindReturningJoypad(int index, string guid, string serial)
    {
        InputDeviceInfo sameGuid = null;
        foreach (var d in _devices)
        {
            if (d.Kind != InputDeviceKind.Joypad || d.IsConnected) continue;
            if (!string.IsNullOrEmpty(serial) && d.Serial == serial && d.Guid == guid) return d;
            if (!string.IsNullOrEmpty(serial) && !string.IsNullOrEmpty(d.Serial)) continue;   // both have serials and they differ
            if (string.IsNullOrEmpty(guid) ? d.Guid.Length == 0 && d.Index == index : d.Guid == guid)
                if (sameGuid == null || d.Index == index) sameGuid = d;
        }
        return sameGuid;
    }

    private string NewJoypadId(int index, string guid, string serial)
    {
        if (string.IsNullOrEmpty(guid)) return $"joypad:index{index}";
        string baseId = string.IsNullOrEmpty(serial) ? $"joypad:{guid}" : $"joypad:{guid}:{serial}";
        string id = baseId;
        for (int n = 2; GetDevice(id) != null; n++) id = $"{baseId}#{n}";
        return id;
    }

    /// <summary>
    /// Compares the registry with the joypads Godot reports and fixes any connection change that was
    /// missed (live only; done every few seconds while ticking).
    /// </summary>
    public void VerifyConnectedDevices()
    {
        EnsureBuiltinDevices();
        var connected = Input.GetConnectedJoypads();
        foreach (var index in connected)
        {
            var device = GetJoypadDevice(index);
            if (device == null || !device.IsConnected) NotifyJoypadConnection(index, true, Input.GetJoyGuid(index));
        }
        foreach (var device in _devices.ToArray())
            if (device.Kind == InputDeviceKind.Joypad && device.IsConnected && !connected.Contains(device.Index))
                NotifyJoypadConnection(device.Index, false);
        var touch = GetDevice("touch");
        if (touch != null) touch.IsConnected = DisplayServer.IsTouchscreenAvailable();
        _connectedJoypadsValid = false;
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
                foreach (var p in _players)
                    if (p != player && p.OwnedCustomDevices.Remove(device.Id)) p.Raw.ClearCustomDevice(device.DeviceType, device.Index, InputEndReason.DeviceReassigned);
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
        device.Role = device.Profile?.Role ?? GuessCustomRole(deviceType, name);
        DeviceConnected?.Invoke(device);
        return device;
    }

    private static DeviceRole GuessCustomRole(string deviceType, string name)
    {
        var n = (name ?? string.Empty).ToLowerInvariant();
        if (deviceType == EyeTrackerBackend.DeviceType) return DeviceRole.EyeTracker;
        if (deviceType != XRInputBackend.DeviceType) return InputDeviceInfo.GuessJoypadRole(name) is var r && r != DeviceRole.Gamepad ? r : DeviceRole.Other;
        if (n.Contains("hand_tracker") || n.Contains("/hand/")) return DeviceRole.XRHand;
        if (n.Contains("head")) return DeviceRole.HeadTracker;
        if (n.Contains("eye") || n.Contains("face")) return DeviceRole.EyeTracker;
        return DeviceRole.XRController;
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
        if (owner != null) { owner.ReceiveDeviceControl(key, value); return; }
        foreach (var p in _players)
            if (p.ReceivesUnownedJoypads && p.OwnedCustomDevices.Count == 0) p.ReceiveDeviceControl(key, value);
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
