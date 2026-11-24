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
    //  Joypad identity
    // ------------------------------------------------------------------

    /// <summary>What Godot (or the caller) tells about a pad that just connected.</summary>
    private struct JoypadReading
    {
        public string Guid, Serial, Name, Model;
        public int Vendor, Product;
        public bool Known;
    }

    /// <summary>The candidates for a pad without a serial number whose owner is unclear.</summary>
    private sealed class Ambiguity
    {
        public readonly List<(InputDeviceInfo Identity, InputPlayer Owner, int OldIndex)> Entries = new();
        /// <summary>The record standing in for the pad until it is resolved (<see cref="ReconnectPolicy.Ask"/>).</summary>
        public InputDeviceInfo Pending;
    }

    private readonly Dictionary<int, Ambiguity> _ambiguous = new();
    private readonly Dictionary<int, JoypadReading> _readings = new();
    private readonly List<(InputDeviceInfo Identity, InputPlayer Owner, int OldIndex)> _owned = new();
    private long _lostCounter;

    /// <summary>
    /// What to do when a pad without a serial number connects and could be the missing pad of more than
    /// one player (pads of one model cannot be told apart), or — with <see cref="ReconnectPolicy.Ask"/> —
    /// of any player.
    /// </summary>
    [Export] public ReconnectPolicy ReconnectWithoutSerial { get; set; } = ReconnectPolicy.Automatic;

    /// <summary>
    /// A pad without a serial number connected that could belong to several players (or, with
    /// <see cref="ReconnectPolicy.Ask"/>, to any player who lost one of that model): the joypad index,
    /// the candidates (who lost theirs first comes first), and the player it was given to (null while
    /// it waits for <see cref="ResolveJoypadReconnect"/>).
    /// </summary>
    [Signal] public delegate void JoypadReconnectAmbiguousEventHandler(int device, Godot.Collections.Array<InputPlayer> candidates, InputPlayer chosen);

    /// <summary>The players a just-connected pad could belong to (empty when there is no doubt).</summary>
    public List<InputPlayer> GetReconnectCandidates(int device)
    {
        var list = new List<InputPlayer>();
        if (_ambiguous.TryGetValue(device, out var a))
            foreach (var e in a.Entries) list.Add(e.Owner);
        return list;
    }

    /// <summary>True while a pad waits for <see cref="ResolveJoypadReconnect"/>; its input reaches nobody.</summary>
    public bool IsJoypadReconnectPending(int device)
        => _ambiguous.TryGetValue(device, out var a) && a.Pending != null && a.Pending.IsConnected;

    /// <summary>
    /// Says whose pad a doubtful reconnection is (see <see cref="JoypadReconnectAmbiguous"/>): the pad
    /// takes that player's identity and goes to it; a pad given to someone else by
    /// <see cref="ReconnectPolicy.Automatic"/> goes back to being missing for its previous owner (who gets
    /// <see cref="PlayerDeviceLost"/> again). Works until the pad disconnects. False when the player is
    /// not a candidate.
    /// </summary>
    public bool ResolveJoypadReconnect(int device, InputPlayer player)
    {
        if (player == null || !_ambiguous.TryGetValue(device, out var a)) return false;
        int e = a.Entries.FindIndex(x => x.Owner == player);
        if (e < 0 || !_readings.TryGetValue(device, out var reading)) return false;
        var target = a.Entries[e];
        var current = ConnectedJoypadAt(device);
        if (current == target.Identity) return true;

        foreach (var p in _players) p.Raw.ClearDevice(device, InputEndReason.DeviceReassigned);
        if (current != null)
        {
            current.IsConnected = false;
            if (current == a.Pending)
            {
                _devices.Remove(current);
                a.Pending = null;
            }
            else
            {
                var back = a.Entries.Find(x => x.Identity == current);
                current.Index = back.OldIndex;
                current.LastOwner = back.Owner;
                current.LostOrder = ++_lostCounter;
                if (back.Owner != null)
                {
                    int at = back.Owner.JoypadDevices.IndexOf(device);
                    if (at >= 0) back.Owner.JoypadDevices[at] = back.OldIndex;
                }
            }
            DeviceDisconnected?.Invoke(current);
            EmitSignal(SignalName.DeviceConnectionChanged, current.Id, false);
            if (current.LastOwner != null && IsPlayer(current.LastOwner))
            {
                current.LastOwner.OnDeviceLost(current);
                EmitSignal(SignalName.PlayerDeviceLost, current.LastOwner, current.Id);
            }
        }

        var identity = target.Identity;
        ActivateJoypad(identity, device, reading);
        identity.LastOwner = null;
        GiveJoypadIndex(player, target.OldIndex, device);
        EmitSignal(SignalName.PlayerDeviceRegained, player, identity.Id);
        return true;
    }

    private bool IsPlayer(InputPlayer player) => player != null && _players.Contains(player) && GodotObject.IsInstanceValid(player);

    private InputPlayer AliveOwner(InputDeviceInfo device) => IsPlayer(device.LastOwner) ? device.LastOwner : null;

    private InputDeviceInfo ConnectedJoypadAt(int index)
    {
        foreach (var d in _devices)
            if (d.Kind == InputDeviceKind.Joypad && d.IsConnected && d.Index == index) return d;
        return null;
    }

    private JoypadReading ReadJoypad(int index, string guid, string serial, string name)
    {
        bool known = Input.GetConnectedJoypads().Contains(index);
        var r = new JoypadReading { Known = known, Guid = guid ?? string.Empty };
        r.Name = name ?? (known ? Input.GetJoyName(index) : $"Joypad {index}");
        if (known)
        {
            if (serial == null)
            {
                var info = Input.GetJoyInfo(index);
                serial = info.TryGetValue("serial_number", out var s) ? s.AsString() : string.Empty;
            }
            (r.Vendor, r.Product) = ControllerDetection.ReadIds(index);
        }
        if (r.Vendor == 0 && ControllerDetection.TryParseGuid(r.Guid, out _, out var vendor, out var product))
        {
            r.Vendor = vendor;
            r.Product = product;
        }
        r.Serial = serial ?? string.Empty;
        r.Model = ControllerDetection.ModelKey(r.Guid, r.Vendor, r.Product);
        return r;
    }

    /// <summary>
    /// The missing pad this one is: the same serial number and model if both have serials; otherwise a
    /// missing pad of the same model. Fills <see cref="_owned"/> with the serial-less candidates that
    /// had an owner (lost first, first). Returns null for a new pad — or, with
    /// <see cref="ReconnectPolicy.Ask"/>, when only a guess would do.
    /// </summary>
    private InputDeviceInfo FindReturningJoypad(int index, in JoypadReading r, out bool bySerial)
    {
        bySerial = false;
        _owned.Clear();
        InputDeviceInfo unowned = null;
        foreach (var d in _devices)
        {
            if (d.Kind != InputDeviceKind.Joypad || d.IsConnected || d.ModelKey != r.Model) continue;
            if (r.Serial.Length > 0 && d.Serial.Length > 0)
            {
                if (d.Serial != r.Serial) continue;
                bySerial = true;
                _owned.Clear();
                return d;
            }
            if (r.Model.Length == 0 && d.Index != index) continue;   // nothing to go by but the index
            var owner = AliveOwner(d);
            if (owner != null) _owned.Add((d, owner, d.Index));
            else if (unowned == null || d.Index == index) unowned = d;
        }
        if (_owned.Count == 0) return unowned;
        _owned.Sort((x, y) => x.Identity.LostOrder.CompareTo(y.Identity.LostOrder));
        return ReconnectWithoutSerial == ReconnectPolicy.Automatic ? _owned[0].Identity : null;
    }

    /// <summary>
    /// Finds or creates the record for a pad that connected and marks it connected. Returns the record;
    /// <paramref name="returning"/> says whether it is a pad seen before, <paramref name="bySerial"/>
    /// whether that was certain.
    /// </summary>
    private InputDeviceInfo ConnectJoypadRecord(int index, JoypadReading r, out bool returning, out bool bySerial)
    {
        _readings[index] = r;
        var existing = ConnectedJoypadAt(index);
        if (existing != null && existing.ModelKey == r.Model)
        {
            // Told twice about the same pad: refresh, do not count a reconnection.
            returning = true;
            bySerial = true;
            _owned.Clear();
            RefreshJoypad(existing, index, r);
            return existing;
        }
        if (existing != null) existing.IsConnected = false;   // replaced without a disconnect notice

        var device = FindReturningJoypad(index, r, out bySerial);
        returning = device != null;
        if (device == null)
        {
            device = new InputDeviceInfo(NewJoypadId(index, r.Guid, r.Serial), InputDeviceKind.Joypad, index, r.Name);
            _devices.Add(device);
        }
        ActivateJoypad(device, index, r);
        return device;
    }

    private void ActivateJoypad(InputDeviceInfo device, int index, in JoypadReading r)
    {
        device.PreviousIndex = device.Index != index ? device.Index : -1;
        device.IsConnected = true;
        device.ConnectionCount++;
        device.ConnectedAtMs = Time.GetTicksMsec();
        RefreshJoypad(device, index, r);
        DeviceConnected?.Invoke(device);
        EmitSignal(SignalName.DeviceConnectionChanged, device.Id, true);
    }

    private void RefreshJoypad(InputDeviceInfo device, int index, in JoypadReading r)
    {
        device.Index = index;
        device.Name = r.Name;
        device.Guid = r.Guid;
        if (r.Serial.Length > 0) device.Serial = r.Serial;
        device.ModelKey = r.Model;
        device.VendorId = r.Vendor;
        device.ProductId = r.Product;
        device.Profile = FindProfile(r.Name, r.Guid);
        device.Role = device.Profile?.Role ?? InputDeviceInfo.GuessJoypadRole(r.Name);
        if (r.Known)
        {
            device.Family = ControllerDetection.Detect(index);
            device.CanVibrate = Input.HasJoyVibration(index);
            device.HasMotionSensors = Input.HasJoyMotionSensors(index);
            device.HasLight = Input.HasJoyLight(index);
            device.Features = ControllerDetection.AvailableFeatures(index, device.Family);
        }
        else
        {
            device.Family = ControllerDetection.Detect(r.Name, r.Vendor, r.Product);
            device.Features = ControllerDetection.HardwareFeatures(device.Family) & (ControllerFeatures.Touchpad | ControllerFeatures.BackButtons | ControllerFeatures.ShareButton);
        }
        device.HardwareFeatures = ControllerDetection.HardwareFeatures(device.Family);
    }

    /// <summary>Marks the pad at an index disconnected, remembering who owned it. Returns its record.</summary>
    private InputDeviceInfo DisconnectJoypadRecord(int index)
    {
        var gone = ConnectedJoypadAt(index);
        if (gone == null) return null;
        gone.LastOwner = GetJoypadOwner(index);
        gone.LostOrder = ++_lostCounter;
        gone.IsConnected = false;
        DeviceDisconnected?.Invoke(gone);
        EmitSignal(SignalName.DeviceConnectionChanged, gone.Id, false);
        return gone;
    }

    /// <summary>
    /// Gives a joypad index to a player for its returning pad: the pad's old index in the player's list
    /// becomes the new one (same position), and nobody else keeps the index.
    /// </summary>
    private void GiveJoypadIndex(InputPlayer player, int oldIndex, int index)
    {
        foreach (var other in _players)
            if (other != player) other.JoypadDevices.Remove(index);
        int at = oldIndex != index && ConnectedJoypadAt(oldIndex) == null ? player.JoypadDevices.IndexOf(oldIndex) : -1;
        if (at >= 0)
        {
            player.JoypadDevices.Remove(index);
            at = player.JoypadDevices.IndexOf(oldIndex);
            player.JoypadDevices[at] = index;
            if (_joypadGuids.Remove(oldIndex, out var g)) _joypadGuids[index] = g;
        }
        else if (!player.JoypadDevices.Contains(index)) player.JoypadDevices.Add(index);
    }

    /// <summary>
    /// A player's claim on an index that belongs to a different, missing pad (the player keeps its claim
    /// on that pad, which comes back to it wherever it reconnects).
    /// </summary>
    private bool IsStaleClaim(InputPlayer player, int index, InputDeviceInfo arriving)
    {
        foreach (var d in _devices)
            if (d != arriving && d.Kind == InputDeviceKind.Joypad && !d.IsConnected && d.Index == index && d.LastOwner == player) return true;
        return false;
    }

    private bool HasJoypadRecordAt(int index, InputDeviceInfo except)
    {
        foreach (var d in _devices)
            if (d != except && d.Kind == InputDeviceKind.Joypad && d.Index == index) return true;
        return false;
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
                    if (p != player && p.OwnedCustomDevices.Remove(device.Id))
                    {
                        p.ReleaseHapticDevice(device);
                        p.Raw.ClearCustomDevice(device.DeviceType, device.Index, InputEndReason.DeviceReassigned);
                    }
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

    private readonly Dictionary<(InputBackend Backend, string Type, int Local), int> _runtimeIndex = new();

    /// <summary>A backend device by its type and runtime index (the index bindings and channels use); the connected one first.</summary>
    public InputDeviceInfo GetCustomDevice(string deviceType, int index)
    {
        InputDeviceInfo found = null;
        foreach (var d in _devices)
            if (d.Kind == InputDeviceKind.Custom && d.DeviceType == deviceType && d.Index == index && (found == null || d.IsConnected)) found = d;
        return found;
    }

    private int RuntimeIndexOf(InputBackend backend, string deviceType, int local)
        => backend != null && _runtimeIndex.TryGetValue((backend, deviceType, local), out var runtime) ? runtime : local;

    private bool CustomIndexTaken(string deviceType, int index, InputDeviceInfo except)
    {
        foreach (var d in _devices)
            if (d != except && d.Kind == InputDeviceKind.Custom && d.IsConnected && d.DeviceType == deviceType && d.Index == index) return true;
        return false;
    }

    internal InputDeviceInfo RegisterCustomDevice(InputBackend backend, string deviceType, int index, string name, string guid)
    {
        EnsureBuiltinDevices();
        guid ??= string.Empty;
        // A persistent id: the hardware identity when given, else the backend's own index.
        string id = guid.Length > 0 ? $"{deviceType}:{guid}" : $"{deviceType}:{index}";
        var device = GetDevice(id);
        if (device != null && device.IsConnected && device.Backend != null && device.Backend != backend && _backends.Contains(device.Backend))
        {
            // Another live backend already has this id: keep them apart.
            id = guid.Length > 0 ? $"{deviceType}:{backend?.Namespace}:{guid}" : $"{deviceType}:{backend?.Namespace}:{index}";
            device = GetDevice(id);
        }

        // The runtime index: the one the device had, else the backend's own, else the next free one.
        int runtime = device?.Index ?? index;
        if (CustomIndexTaken(deviceType, runtime, device)) runtime = index;
        while (CustomIndexTaken(deviceType, runtime, device)) runtime++;
        if (backend != null) _runtimeIndex[(backend, deviceType, index)] = runtime;

        if (device == null)
        {
            device = new InputDeviceInfo(id, InputDeviceKind.Custom, runtime, name) { DeviceType = deviceType };
            _devices.Add(device);
        }
        device.Index = runtime;
        device.Backend = backend;   // a replacement backend takes the device over
        device.Name = name;
        device.Guid = guid;
        bool reconnect = !device.IsConnected || device.ConnectionCount == 0;
        device.IsConnected = true;
        if (reconnect) device.ConnectionCount++;
        device.Profile = FindProfile(name, guid);
        device.Role = device.Profile?.Role ?? GuessCustomRole(deviceType, name);
        DeviceConnected?.Invoke(device);
        EmitSignal(SignalName.DeviceConnectionChanged, device.Id, true);
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

    internal void UnregisterCustomDevice(InputBackend backend, string deviceType, int index)
    {
        int runtime = RuntimeIndexOf(backend, deviceType, index);
        var device = GetCustomDevice(deviceType, runtime);
        if (device == null || !device.IsConnected || (backend != null && device.Backend != backend)) return;
        device.IsConnected = false;
        foreach (var p in _players) p.Raw.ClearCustomDevice(deviceType, runtime);
        DeviceDisconnected?.Invoke(device);
        EmitSignal(SignalName.DeviceConnectionChanged, device.Id, false);
    }

    /// <summary>Marks a backend device disconnected by its type and runtime index.</summary>
    internal void UnregisterCustomDevice(string deviceType, int index) => UnregisterCustomDevice(null, deviceType, index);

    /// <summary>
    /// Writes a control value of a backend device (by its runtime index) and routes it to the device's
    /// owner, or — when nobody owns it — to players that take unowned devices.
    /// </summary>
    public void SetDeviceControl(string deviceType, int index, string control, Vector3 value)
        => SetDeviceControl(deviceType, index, control, new Vector4(value.X, value.Y, value.Z, 0f));

    /// <summary>Sets a control of a backend device (up to four components, e.g. a rotation as x, y, z, w).</summary>
    public void SetDeviceControl(string deviceType, int index, string control, Vector4 value)
        => RouteDeviceControl(deviceType, index, control, value, tracked: false, clear: false);

    internal void SetDeviceControl(InputBackend backend, string deviceType, int index, string control, Vector4 value, bool tracked)
        => RouteDeviceControl(deviceType, RuntimeIndexOf(backend, deviceType, index), control, value, tracked, clear: false);

    internal void ClearDeviceControl(InputBackend backend, string deviceType, int index, string control)
        => RouteDeviceControl(deviceType, RuntimeIndexOf(backend, deviceType, index), control, Vector4.Zero, tracked: false, clear: true);

    private void RouteDeviceControl(string deviceType, int runtime, string control, Vector4 value, bool tracked, bool clear)
    {
        EnsureDefaultPlayer();
        var key = SourceKey.ForDeviceControl(deviceType, control, runtime);
        var id = GetCustomDevice(deviceType, runtime)?.Id ?? $"{deviceType}:{runtime}";
        InputPlayer owner = null;
        foreach (var p in _players)
            if (p.OwnedCustomDevices.Contains(id)) { owner = p; break; }
        if (owner != null)
        {
            if (clear) owner.ClearDeviceControl(key);
            else owner.ReceiveDeviceControl(key, value, tracked);
            return;
        }
        foreach (var p in _players)
            if (p.ReceivesUnownedJoypads && p.OwnedCustomDevices.Count == 0)
            {
                if (clear) p.ClearDeviceControl(key);
                else p.ReceiveDeviceControl(key, value, tracked);
            }
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
