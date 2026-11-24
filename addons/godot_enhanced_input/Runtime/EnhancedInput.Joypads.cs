using System.Collections.Generic;
using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Joypads: assignment to players, connection and reconnection, and identity — a pad is recognised by its
/// serial number or model and keeps its owner under any index.
/// </summary>
public partial class EnhancedInput
{
    /// <summary>A connected joypad's identity, or null (never asks Godot about a device that is not connected).</summary>
    private static string GuidOf(int device)
        => device >= 0 && Input.GetConnectedJoypads().Contains(device) ? Input.GetJoyGuid(device) : null;

    /// <summary>Gives a joypad to a player, taking it from any other player. The joypad's identity is remembered.</summary>
    public void AssignJoypad(InputPlayer player, int device) => AssignJoypad(player, device, GuidOf(device));

    /// <summary>
    /// Gives a joypad to a player and remembers its identity (<paramref name="guid"/>, as from
    /// <c>Input.GetJoyGuid</c>), so a different pad that later receives the same device index does not
    /// inherit the assignment.
    /// </summary>
    public void AssignJoypad(InputPlayer player, int device, string guid)
    {
        if (player == null || device < 0) return;
        if (_ambiguous.TryGetValue(device, out var doubt))
        {
            // Assigning a doubtful pad to one of its candidates settles whose it is.
            if (doubt.Entries.Exists(x => x.Owner == player)) ResolveJoypadReconnect(device, player);
            _ambiguous.Remove(device);
        }
        var record = GetJoypadDevice(device);
        if (record != null) record.Owner = player;   // ownership is kept by the pad's identity
        foreach (var other in _players)
            if (other != player && (other.JoypadDevices.Remove(device) || other.MayUseJoypad(device, this))) other.ReleaseHapticDevice(device);
        if (!player.JoypadDevices.Contains(device)) player.JoypadDevices.Add(device);
        if (string.IsNullOrEmpty(guid)) _joypadGuids.Remove(device);
        else _joypadGuids[device] = guid;
        // Whatever the joypad was doing belongs to its previous route: cut it off there.
        foreach (var other in _players) other.Raw.ClearDevice(device, InputEndReason.DeviceReassigned);
        EmitSignal(SignalName.JoypadAssigned, player, device);
    }

    /// <summary>Takes a joypad from whichever player owns it. Returns false if nobody did.</summary>
    public bool UnassignJoypad(int device)
    {
        bool removed = false;
        _joypadGuids.Remove(device);
        _ambiguous.Remove(device);
        var record = GetJoypadDevice(device);
        if (record != null) record.Owner = null;   // a missing pad no longer returns to anyone either
        foreach (var player in _players)
            if (player.JoypadDevices.Remove(device))
            {
                removed = true;
                player.ReleaseHapticDevice(device);
                player.Raw.ClearDevice(device, InputEndReason.DeviceReassigned);
                EmitSignal(SignalName.JoypadUnassigned, player, device);
            }
        return removed;
    }

    /// <summary>The player that owns a joypad, or null.</summary>
    public InputPlayer GetJoypadOwner(int device)
    {
        foreach (var player in _players)
            if (player.JoypadDevices.Contains(device)) return player;
        return null;
    }

    private InputPlayer Join(int device)
    {
        InputPlayer target = null;
        foreach (var player in _players)
            if (player.JoypadDevices.Count == 0) { target = player; break; }
        if (target == null && _players.Count < MaxPlayers) target = CreatePlayer();
        if (target == null) return null;

        AssignJoypad(target, device);
        EmitSignal(SignalName.PlayerJoined, target, device);
        return target;
    }

    private void OnJoyConnectionChanged(long device, bool connected)
        => NotifyJoypadConnection((int)device, connected, connected ? GuidOf((int)device) : null);

    /// <summary>
    /// Handles a joypad connecting or disconnecting (called from Godot's signal; public for tests and
    /// custom backends). A disconnected pad keeps its owner. When a pad connects under a device index
    /// whose remembered identity differs (<paramref name="guid"/>), the old assignment is dropped instead
    /// of being inherited by the new pad.
    /// </summary>
    public void NotifyJoypadConnection(int device, bool connected, string guid = null) => NotifyJoypadConnection(device, connected, guid, null, null);

    /// <summary>
    /// Handles a joypad connecting or disconnecting, with its serial number and name when known (tests and
    /// backends can pass them). A pad that comes back — matched by serial, else identity — keeps its
    /// owner even under a different device index; a player whose current device disconnects falls back
    /// to another one it can use, and <see cref="PlayerDeviceLost"/> tells the game when none is left.
    /// </summary>
    public void NotifyJoypadConnection(int device, bool connected, string guid, string serial, string name)
    {
        _connectedJoypadsValid = false;
        EnsureBuiltinDevices();
        if (!connected)
        {
            foreach (var player in _players) player.Raw.ClearDevice(device);
            var gone = DisconnectJoypadRecord(device);
            bool wasPending = _ambiguous.Remove(device, out var pending) && pending.Pending == gone && gone != null;
            if (gone != null && !wasPending)
                foreach (var player in _players)
                {
                    bool owned = player.JoypadDevices.Contains(device);
                    bool current = player.CurrentDevice == gone;
                    if (!owned && !current) continue;
                    player.OnDeviceLost(gone);
                    if (owned && !player.HasOtherJoypad(device)) EmitSignal(SignalName.PlayerDeviceLost, player, gone.Id);
                }
            if (wasPending) _devices.Remove(gone);   // it never became anyone's
        }
        else
        {
            var reading = ReadJoypad(device, guid, serial, name);
            bool refresh = ConnectedJoypadAt(device) is { } same && same.ModelKey == reading.Model;
            var info = ConnectJoypadRecord(device, reading, out bool returning, out bool bySerial);
            if (!refresh)
            {
                var owner = returning ? AliveOwner(info) : null;
                int oldIndex = info.PreviousIndex >= 0 ? info.PreviousIndex : device;
                info.Owner = owner;
                bool ambiguous = !bySerial && (_owned.Count >= 2 || (ReconnectWithoutSerial == ReconnectPolicy.Ask && _owned.Count >= 1));
                var candidates = ambiguous ? new List<(InputDeviceInfo, InputPlayer, int)>(_owned) : null;

                // Nobody keeps a claim on this index for some other pad that is still missing, and a pad
                // seen for the first time never inherits an assignment meant for a different one.
                foreach (var player in _players.ToArray())
                {
                    if (player == owner || !player.JoypadDevices.Contains(device)) continue;
                    bool stale = IsStaleClaim(player, device, info)
                        || (!returning && !HasJoypadRecordAt(device, info) && !string.IsNullOrEmpty(reading.Guid)
                            && _joypadGuids.TryGetValue(device, out var known) && known != reading.Guid);
                    if (!stale) continue;
                    player.JoypadDevices.Remove(device);
                    _joypadGuids.Remove(device);
                    EmitSignal(SignalName.JoypadUnassigned, player, device);
                }

                if (owner != null)
                {
                    GiveJoypadIndex(owner, oldIndex, device);
                    EmitSignal(SignalName.PlayerDeviceRegained, owner, info.Id);
                }
                if (ambiguous)
                {
                    var a = new Ambiguity { Pending = returning ? null : info };
                    var list = new Godot.Collections.Array<InputPlayer>();
                    foreach (var c in candidates)
                    {
                        a.Entries.Add(c);
                        list.Add(c.Item2);
                    }
                    _ambiguous[device] = a;
                    EmitSignal(SignalName.JoypadReconnectAmbiguous, device, list, owner);
                }
                else _ambiguous.Remove(device);
            }
        }
        EmitSignal(SignalName.JoypadConnectionChanged, device, connected);
    }

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
                current.Owner = back.Owner;
                current.LostOrder = ++_lostCounter;
                if (back.Owner != null)
                {
                    int at = back.Owner.JoypadDevices.IndexOf(device);
                    if (at >= 0) back.Owner.JoypadDevices[at] = back.OldIndex;
                }
            }
            DeviceDisconnected?.Invoke(current);
            EmitSignal(SignalName.DeviceConnectionChanged, current.Id, false);
            if (current.Owner != null && IsPlayer(current.Owner))
            {
                current.Owner.OnDeviceLost(current);
                EmitSignal(SignalName.PlayerDeviceLost, current.Owner, current.Id);
            }
        }

        var identity = target.Identity;
        ActivateJoypad(identity, device, reading);
        identity.Owner = player;
        GiveJoypadIndex(player, target.OldIndex, device);
        EmitSignal(SignalName.PlayerDeviceRegained, player, identity.Id);
        return true;
    }

    private bool IsPlayer(InputPlayer player) => player != null && _players.Contains(player) && GodotObject.IsInstanceValid(player);

    private InputPlayer AliveOwner(InputDeviceInfo device) => IsPlayer(device.Owner) ? device.Owner : null;

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
        gone.Owner = GetJoypadOwner(index) ?? gone.Owner;
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
        var record = ConnectedJoypadAt(index);
        if (record != null) record.Owner = player;
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
            if (d != arriving && d.Kind == InputDeviceKind.Joypad && !d.IsConnected && d.Index == index && d.Owner == player) return true;
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
}
