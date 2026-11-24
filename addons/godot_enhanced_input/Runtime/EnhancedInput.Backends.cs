using System;
using System.Collections.Generic;
using Godot;

namespace GodotEnhancedInput;

/// <summary>Backends: hardware Godot does not expose as joypads, with persistent device ids and runtime indices.</summary>
public partial class EnhancedInput
{
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
}
