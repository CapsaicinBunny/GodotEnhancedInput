using System.Collections.Generic;
using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Where vibration and light requests go. The default sends them to Godot; tests, native controller
/// libraries (adaptive triggers, trigger motors, HD haptics) and custom hardware can replace it.
/// </summary>
public interface IHapticsOutput
{
    void StartJoypad(int device, float weak, float strong, float seconds);
    void StopJoypad(int device);
    void SetJoypadLight(int device, Color color);
    void VibrateHandheld(int milliseconds, float amplitude);

    /// <summary>Trigger motors (impulse triggers). Godot's joypad API has none, so the default does nothing.</summary>
    void StartTriggers(int device, float left, float right, float seconds) { }
}

/// <summary>Sends haptics to Godot's <see cref="Input"/>.</summary>
/// <summary>Which trigger.</summary>
public enum TriggerSide
{
    Left,
    Right,
}

/// <summary>How an adaptive trigger resists.</summary>
public enum TriggerResistanceMode
{
    /// <summary>No resistance.</summary>
    Off,
    /// <summary>Steady resistance from <see cref="TriggerResistance.Start"/> on.</summary>
    Feedback,
    /// <summary>Resistance between start and end that gives way with a click (a gun trigger).</summary>
    Weapon,
    /// <summary>The trigger vibrates from <see cref="TriggerResistance.Start"/> on, at <see cref="TriggerResistance.Frequency"/>.</summary>
    Vibration,
}

/// <summary>An adaptive trigger's resistance (positions and strength 0…1).</summary>
public struct TriggerResistance
{
    public TriggerResistanceMode Mode;
    public float Start;
    public float End;
    public float Strength;
    /// <summary>For <see cref="TriggerResistanceMode.Vibration"/>, in Hz.</summary>
    public float Frequency;

    public static TriggerResistance Off => default;
    public static TriggerResistance Feedback(float start, float strength) => new() { Mode = TriggerResistanceMode.Feedback, Start = start, End = 1f, Strength = strength };
    public static TriggerResistance Weapon(float start, float end, float strength) => new() { Mode = TriggerResistanceMode.Weapon, Start = start, End = end, Strength = strength };
    public static TriggerResistance Vibration(float start, float strength, float frequency) => new() { Mode = TriggerResistanceMode.Vibration, Start = start, End = 1f, Strength = strength, Frequency = frequency };

    /// <summary>The same resistance with its strength scaled (0 turns it off).</summary>
    public TriggerResistance Scaled(float k)
    {
        var r = this;
        r.Strength *= k;
        if (r.Strength <= 0f) r.Mode = TriggerResistanceMode.Off;
        return r;
    }
}

/// <summary>Optional extra for an <see cref="IHapticsOutput"/> that can drive adaptive triggers (no Godot API exists).</summary>
public interface IAdaptiveTriggerOutput
{
    void SetTriggerResistance(int device, TriggerSide side, TriggerResistance resistance);
}

public sealed class GodotHapticsOutput : IHapticsOutput
{
    public void StartJoypad(int device, float weak, float strong, float seconds) => Input.StartJoyVibration(device, weak, strong, seconds);
    public void StopJoypad(int device) => Input.StopJoyVibration(device);
    public void SetJoypadLight(int device, Color color) => Input.SetJoyLight(device, color);
    public void VibrateHandheld(int milliseconds, float amplitude) => Input.VibrateHandheld(milliseconds, amplitude);
}

/// <summary>A playing <see cref="HapticEffect"/>; stop it with <see cref="InputPlayer.StopHaptic"/>.</summary>
public sealed class HapticHandle
{
    private HashSet<int> _excludedPads;
    private HashSet<string> _excludedIds;

    internal void Exclude(int pad) => (_excludedPads ??= new HashSet<int>()).Add(pad);
    internal void Exclude(string id) => (_excludedIds ??= new HashSet<string>()).Add(id);
    internal bool Excludes(int pad) => _excludedPads != null && _excludedPads.Contains(pad);
    internal bool Excludes(string id) => _excludedIds != null && _excludedIds.Contains(id);

    internal HapticHandle(HapticEffect effect, int device, string deviceId, float strength, bool loop)
    {
        Effect = effect;
        Device = device;
        DeviceId = deviceId;
        Strength = strength;
        Loop = loop;
    }

    public HapticEffect Effect { get; }

    /// <summary>The joypad it plays on, or −1 for all of the player's devices.</summary>
    public int Device { get; }

    /// <summary>The backend device it plays on (e.g. "xr:1" for one hand), or null.</summary>
    public string DeviceId { get; }

    /// <summary>Multiplier for the effect's strengths (0…1); can be changed while playing (engine revs).</summary>
    public float Strength { get; set; }

    /// <summary>Whether it repeats until stopped.</summary>
    public bool Loop { get; }

    /// <summary>Seconds since it started.</summary>
    public double Time { get; internal set; }

    /// <summary>Whether it is still playing.</summary>
    public bool IsPlaying { get; internal set; } = true;

    internal float Intensity;
}

/// <summary>A player's haptics: <c>player.Haptics.Play(effect)</c>, <c>Stop(effect)</c>, <c>StopAll()</c>.</summary>
public readonly struct PlayerHaptics
{
    private readonly InputPlayer _player;
    internal PlayerHaptics(InputPlayer player) => _player = player;

    /// <summary>Plays an effect on all of the player's devices (or one joypad); <paramref name="loop"/> repeats it until stopped.</summary>
    public HapticHandle Play(HapticEffect effect, bool loop = false, float strength = 1f, int device = SourceKey.AnyDevice)
        => _player.PlayHaptic(effect, strength, device, loop);

    /// <summary>Plays an effect on one backend device (e.g. one XR hand: <c>"xr:1"</c>).</summary>
    public HapticHandle PlayOn(string deviceId, HapticEffect effect, bool loop = false, float strength = 1f)
        => _player.PlayHapticOn(deviceId, effect, strength, loop);

    /// <summary>Stops one playing effect.</summary>
    public void Stop(HapticHandle handle) => _player.StopHaptic(handle);

    /// <summary>Stops every playing instance of an effect.</summary>
    public void Stop(HapticEffect effect) => _player.StopHaptic(effect);

    /// <summary>Stops everything.</summary>
    public void StopAll() => _player.StopAllHaptics();

    /// <summary>The effects playing now.</summary>
    public IReadOnlyList<HapticHandle> Playing => _player.PlayingHaptics;
}

public partial class InputPlayer
{
    /// <summary>Backend devices this player owns (see <see cref="EnhancedInput.AssignDevice"/>).</summary>
    public HashSet<string> OwnedCustomDevices { get; } = new();

    /// <summary>Where this player's haptics go (Godot by default).</summary>
    public IHapticsOutput HapticsOutput { get; set; } = new GodotHapticsOutput();

    /// <summary>This player's haptics.</summary>
    public PlayerHaptics Haptics => new(this);

    /// <summary>
    /// Profile setting that scales all of this player's vibration (0 turns it off, also for effects that
    /// are already playing), for accessibility and comfort settings. Default "vibration_strength".
    /// </summary>
    public string VibrationStrengthSetting { get; set; } = "vibration_strength";

    /// <summary>
    /// How often motor commands are repeated while an effect plays (each lasts a little longer, so the
    /// motors stop on their own if the game stalls).
    /// </summary>
    public float HapticRefreshSeconds { get; set; } = 0.2f;

    private float VibrationScale => Mathf.Clamp((float)Profile.GetSetting(VibrationStrengthSetting, 1.0), 0f, 1f);

    private readonly List<HapticHandle> _haptics = new();
    private readonly Dictionary<int, Vector4> _hapticMix = new();
    private readonly Dictionary<int, Vector4> _hapticSent = new();
    private readonly Dictionary<int, int> _hapticTop = new();
    private readonly Dictionary<string, Vector3> _customMix = new();
    private readonly Dictionary<string, Vector3> _customSent = new();
    private readonly Dictionary<string, int> _customTop = new();
    private readonly List<int> _hapticPads = new();
    private readonly List<int> _hapticStops = new();
    private readonly List<string> _customStops = new();
    private double _hapticSinceRefresh;

    /// <summary>The effects playing now.</summary>
    public IReadOnlyList<HapticHandle> PlayingHaptics => _haptics;

    /// <summary>
    /// Fills a reused list with the joypads haptics go to: one device (only if this player may use it),
    /// the player's own, or the unowned connected ones it reads. Only connected pads that can vibrate.
    /// </summary>
    private List<int> HapticJoypads(int device)
    {
        _hapticPads.Clear();
        if (JoypadDevices.Count > 0) _hapticPads.AddRange(JoypadDevices);
        else foreach (var d in JoypadsToRead(SourceKey.AnyDevice)) _hapticPads.Add(d);
        if (device >= 0)
        {
            bool mine = _hapticPads.Contains(device);
            _hapticPads.Clear();
            if (mine) _hapticPads.Add(device);
        }
        if (GetParent() is EnhancedInput system)
            for (int i = _hapticPads.Count - 1; i >= 0; i--)
            {
                var info = system.GetJoypadDevice(_hapticPads[i]);
                if (info != null && (!info.IsConnected || !info.CanVibrate)) _hapticPads.RemoveAt(i);
            }
        return _hapticPads;
    }

    /// <summary>
    /// Starts a vibration effect on this player's joypads (or one of them) and backend devices. Motors
    /// are commanded at once, not on the next tick. Returns a handle to stop or adjust it.
    /// </summary>
    public HapticHandle PlayHaptic(HapticEffect effect, float strength = 1f, int device = SourceKey.AnyDevice, bool loop = false)
    {
        if (effect == null) return null;
        var handle = new HapticHandle(effect, device, null, Mathf.Clamp(strength, 0f, 1f), loop || effect.Loop);
        _haptics.Add(handle);
        if (effect.Handheld && AcceptTouch && device < 0 && effect.Duration > 0f && !handle.Loop)
            VibrateHandheld(Mathf.RoundToInt((float)(effect.TotalSeconds * 1000)), Mathf.Max(effect.Weak, effect.Strong) * handle.Strength);
        UpdateHaptics(0);
        return handle;
    }

    /// <summary>Plays an effect on one backend device this player owns (e.g. one XR hand).</summary>
    public HapticHandle PlayHapticOn(string deviceId, HapticEffect effect, float strength = 1f, bool loop = false)
    {
        if (effect == null || string.IsNullOrEmpty(deviceId) || !OwnedCustomDevices.Contains(deviceId)) return null;
        var handle = new HapticHandle(effect, SourceKey.AnyDevice, deviceId, Mathf.Clamp(strength, 0f, 1f), loop || effect.Loop);
        _haptics.Add(handle);
        UpdateHaptics(0);
        return handle;
    }

    /// <summary>Plays an effect every time an action raises one of the given events (e.g. a click on each press).</summary>
    public ActionSubscription BindHaptic(GameAction action, ActionEvents events, HapticEffect effect, float strength = 1f)
        => Bind(action, events, _ => PlayHaptic(effect, strength));

    /// <summary>Stops one effect.</summary>
    public void StopHaptic(HapticHandle handle)
    {
        if (handle == null || !_haptics.Remove(handle)) return;
        handle.IsPlaying = false;
        UpdateHaptics(0);
    }

    /// <summary>Stops every playing instance of an effect.</summary>
    public void StopHaptic(HapticEffect effect)
    {
        bool any = false;
        for (int i = _haptics.Count - 1; i >= 0; i--)
        {
            if (_haptics[i].Effect != effect) continue;
            _haptics[i].IsPlaying = false;
            _haptics.RemoveAt(i);
            any = true;
        }
        if (any) UpdateHaptics(0);
    }

    /// <summary>Stops every effect of this player.</summary>
    public void StopAllHaptics()
    {
        foreach (var h in _haptics) h.IsPlaying = false;
        _haptics.Clear();
        UpdateHaptics(0);
    }

    /// <summary>
    /// Advances playing effects and sends motor commands when they change (called every tick; call it
    /// yourself only for players you tick by hand without <see cref="Tick"/>). Does nothing while no
    /// effect plays and no motor is running.
    /// </summary>
    public void UpdateHaptics(double delta)
    {
        if (_haptics.Count == 0 && _hapticSent.Count == 0 && _customSent.Count == 0) return;
        float scale = VibrationScale;
        var system = GetParent() as EnhancedInput;
        _hapticMix.Clear();
        _customMix.Clear();
        _hapticTop.Clear();
        _customTop.Clear();

        // Pass 1: advance, drop finished effects and effects whose device is no longer this player's,
        // find the top override priority per device.
        for (int i = _haptics.Count - 1; i >= 0; i--)
        {
            var h = _haptics[i];
            bool gone = h.DeviceId != null ? !OwnedCustomDevices.Contains(h.DeviceId) : h.Device >= 0 && !MayUseJoypad(h.Device, system);
            if (gone) { h.IsPlaying = false; _haptics.RemoveAt(i); continue; }
            h.Time += delta;
            h.Intensity = h.Effect.Sample(h.Time, h.Loop, out bool done) * h.Strength * scale;
            if (done) { h.IsPlaying = false; _haptics.RemoveAt(i); continue; }
            if (h.Effect.Blend != HapticBlend.Override || h.Intensity <= 0f) continue;
            if (h.DeviceId != null) Raise(_customTop, h.DeviceId, h.Effect.Priority);
            else
            {
                foreach (var d in HapticJoypads(h.Device))
                    if (!h.Excludes(d)) Raise(_hapticTop, d, h.Effect.Priority);
                if (h.Device < 0)
                    foreach (var id in OwnedCustomDevices)
                        if (!h.Excludes(id)) Raise(_customTop, id, h.Effect.Priority);
            }
        }

        // Pass 2: mix.
        foreach (var h in _haptics)
        {
            if (h.Intensity <= 0f) continue;
            var e = h.Effect;
            float k = h.Intensity;
            var v = new Vector4(e.Weak * k, e.Strong * k, e.TriggerLeft * k, e.TriggerRight * k);
            if (h.DeviceId != null) MixCustom(h.DeviceId, e, v);
            else
            {
                foreach (var d in HapticJoypads(h.Device))
                {
                    if (h.Excludes(d)) continue;
                    if (_hapticTop.TryGetValue(d, out var top) && e.Priority < top) continue;
                    var dv = SupportsTriggerRumble(d, system) ? v : new Vector4(v.X, v.Y, 0f, 0f);
                    _hapticMix[d] = _hapticMix.TryGetValue(d, out var m) ? Combine(m, dv, e.Blend) : dv;
                }
                if (h.Device < 0)
                    foreach (var id in OwnedCustomDevices)
                        if (!h.Excludes(id)) MixCustom(id, e, v);
            }
        }

        _hapticSinceRefresh += delta;
        bool refresh = _hapticSinceRefresh >= HapticRefreshSeconds;
        if (refresh) _hapticSinceRefresh = 0;
        float hold = HapticRefreshSeconds * 2.5f;
        var output = HapticsOutput;

        foreach (var pair in _hapticMix)
        {
            var v = pair.Value;
            if (v == Vector4.Zero) continue;
            bool known = _hapticSent.TryGetValue(pair.Key, out var sent);
            // Compared with what was last transmitted, so a slow ramp is sent once it has moved enough.
            if (known && !refresh && !Changed(sent, v)) continue;
            output?.StartJoypad(pair.Key, Mathf.Clamp(v.X, 0f, 1f), Mathf.Clamp(v.Y, 0f, 1f), hold);
            if (v.Z > 0f || v.W > 0f || sent.Z > 0f || sent.W > 0f) output?.StartTriggers(pair.Key, Mathf.Clamp(v.Z, 0f, 1f), Mathf.Clamp(v.W, 0f, 1f), hold);
            _transmitted.Add((pair.Key, v));
        }
        foreach (var (d, v) in _transmitted) _hapticSent[d] = v;
        _transmitted.Clear();
        _hapticStops.Clear();
        foreach (var d in _hapticSent.Keys)
            if (!_hapticMix.TryGetValue(d, out var v) || v == Vector4.Zero) _hapticStops.Add(d);
        foreach (var d in _hapticStops)
        {
            // A pad that went to another player is theirs to command now: forget it without a stop.
            if (MayUseJoypad(d, system))
            {
                output?.StopJoypad(d);
                if (_hapticSent[d].Z > 0f || _hapticSent[d].W > 0f) output?.StartTriggers(d, 0f, 0f, 0f);
            }
            _hapticSent.Remove(d);
        }

        if (system == null) return;
        foreach (var pair in _customMix)
        {
            if (pair.Value.X <= 0f && pair.Value.Y <= 0f) continue;
            bool known = _customSent.TryGetValue(pair.Key, out var sent);
            if (known && !refresh && !Changed(new Vector4(sent.X, sent.Y, sent.Z * 0.01f, 0f), new Vector4(pair.Value.X, pair.Value.Y, pair.Value.Z * 0.01f, 0f))) continue;
            var info = system.GetDevice(pair.Key);
            if (info?.Backend != null && info.IsConnected) info.Backend.Rumble(info, pair.Value.X, pair.Value.Y, hold, pair.Value.Z);
            _customTransmitted.Add((pair.Key, pair.Value));
        }
        foreach (var (id, v) in _customTransmitted) _customSent[id] = v;
        _customTransmitted.Clear();
        _customStops.Clear();
        foreach (var id in _customSent.Keys)
            if (!_customMix.TryGetValue(id, out var v) || (v.X <= 0f && v.Y <= 0f)) _customStops.Add(id);
        foreach (var id in _customStops)
        {
            var info = system.GetDevice(id);
            if (OwnedCustomDevices.Contains(id) && info?.Backend != null && info.IsConnected) info.Backend.Rumble(info, 0f, 0f, 0f, 0f);
            _customSent.Remove(id);
        }
    }

    private readonly List<(int, Vector4)> _transmitted = new();
    private readonly List<(string, Vector3)> _customTransmitted = new();

    /// <summary>Whether a pad has trigger motors (impulse triggers); unknown pads are given the benefit of the doubt.</summary>
    private static bool SupportsTriggerRumble(int device, EnhancedInput system)
    {
        var info = system?.GetJoypadDevice(device);
        return info == null || info.Family is ControllerFamily.Unknown or ControllerFamily.Generic
            || (info.HardwareFeatures & ControllerFeatures.TriggerRumble) != 0;
    }

    /// <summary>
    /// Called when a device leaves this player (given to another player or unassigned): its motors are
    /// stopped now — before the new owner commands them — and this player never commands it again.
    /// </summary>
    internal void ReleaseHapticDevice(int joypad)
    {
        if (_hapticSent.Remove(joypad, out var sent))
        {
            HapticsOutput?.StopJoypad(joypad);
            if (sent.Z > 0f || sent.W > 0f) HapticsOutput?.StartTriggers(joypad, 0f, 0f, 0f);
        }
        for (int i = _haptics.Count - 1; i >= 0; i--)
            if (_haptics[i].Device == joypad) { _haptics[i].IsPlaying = false; _haptics.RemoveAt(i); }
        _triggerResistance.Remove(joypad);
    }

    /// <inheritdoc cref="ReleaseHapticDevice(int)"/>
    internal void ReleaseHapticDevice(InputDeviceInfo device)
    {
        if (device == null) return;
        if (device.Kind == InputDeviceKind.Joypad) { ReleaseHapticDevice(device.Index); return; }
        if (_customSent.Remove(device.Id) && device.Backend != null && device.IsConnected) device.Backend.Rumble(device, 0f, 0f, 0f, 0f);
        for (int i = _haptics.Count - 1; i >= 0; i--)
            if (_haptics[i].DeviceId == device.Id) { _haptics[i].IsPlaying = false; _haptics.RemoveAt(i); }
    }

    private void MixCustom(string id, HapticEffect e, Vector4 v)
    {
        if (_customTop.TryGetValue(id, out var top) && e.Priority < top) return;
        var c = new Vector3(v.X, v.Y, e.Frequency);
        if (!_customMix.TryGetValue(id, out var m)) { _customMix[id] = c; return; }
        var combined = Combine(new Vector4(m.X, m.Y, 0f, 0f), new Vector4(c.X, c.Y, 0f, 0f), e.Blend);
        _customMix[id] = new Vector3(combined.X, combined.Y, Mathf.Max(m.Z, c.Z));
    }

    private static void Raise<TKey>(Dictionary<TKey, int> top, TKey key, int priority)
    {
        if (!top.TryGetValue(key, out var p) || priority > p) top[key] = priority;
    }

    private static Vector4 Combine(Vector4 a, Vector4 b, HapticBlend blend) => blend == HapticBlend.Add
        ? new Vector4(Mathf.Min(1f, a.X + b.X), Mathf.Min(1f, a.Y + b.Y), Mathf.Min(1f, a.Z + b.Z), Mathf.Min(1f, a.W + b.W))
        : new Vector4(Mathf.Max(a.X, b.X), Mathf.Max(a.Y, b.Y), Mathf.Max(a.Z, b.Z), Mathf.Max(a.W, b.W));

    private static bool Changed(Vector4 a, Vector4 b)
    {
        var d = (a - b).Abs();
        return d.X > 0.02f || d.Y > 0.02f || d.Z > 0.02f || d.W > 0.02f;
    }

    /// <summary>
    /// Vibrates this player's joypads (or one of them; backend devices only when no joypad is given):
    /// <paramref name="weak"/> and <paramref name="strong"/> motors 0…1 for <paramref name="seconds"/>
    /// (0: until stopped), scaled by the player's vibration strength setting. Mixes with playing effects.
    /// </summary>
    public HapticHandle Rumble(float weak, float strong, float seconds, int device = SourceKey.AnyDevice)
        => PlayHaptic(HapticEffect.Constant(Mathf.Clamp(weak, 0f, 1f), Mathf.Clamp(strong, 0f, 1f), Mathf.Max(0f, seconds)), 1f, device);

    /// <summary>
    /// Stops vibration on one joypad, or everywhere. For one joypad: effects aimed at it stop, and effects
    /// playing on all of the player's devices leave it out from now on (they go on elsewhere).
    /// </summary>
    public void StopRumble(int device = SourceKey.AnyDevice)
    {
        for (int i = _haptics.Count - 1; i >= 0; i--)
        {
            var h = _haptics[i];
            if (device >= 0 && h.Device != device)
            {
                if (h.Device < 0 && h.DeviceId == null) h.Exclude(device);
                continue;
            }
            h.IsPlaying = false;
            _haptics.RemoveAt(i);
        }
        UpdateHaptics(0);
        foreach (var d in HapticJoypads(device))
            if (!_hapticSent.ContainsKey(d)) HapticsOutput?.StopJoypad(d);
    }

    /// <summary>Stops vibration on one backend device (e.g. one XR hand); other devices keep their effects.</summary>
    public void StopRumbleOn(string deviceId)
    {
        if (string.IsNullOrEmpty(deviceId)) return;
        for (int i = _haptics.Count - 1; i >= 0; i--)
        {
            var h = _haptics[i];
            if (h.DeviceId == deviceId) { h.IsPlaying = false; _haptics.RemoveAt(i); }
            else if (h.DeviceId == null && h.Device < 0) h.Exclude(deviceId);
        }
        UpdateHaptics(0);
    }

    /// <summary>
    /// Colours the light of this player's joypads (or one of them) — only connected pads the player may
    /// use that report a light (unknown pads are tried).
    /// </summary>
    public void SetJoypadLight(Color color, int device = SourceKey.AnyDevice)
    {
        var system = GetParent() as EnhancedInput;
        _hapticPads.Clear();
        if (JoypadDevices.Count > 0) _hapticPads.AddRange(JoypadDevices);
        else foreach (var d in JoypadsToRead(SourceKey.AnyDevice)) _hapticPads.Add(d);
        if (device >= 0) { bool mine = _hapticPads.Contains(device); _hapticPads.Clear(); if (mine) _hapticPads.Add(device); }
        foreach (var d in _hapticPads)
        {
            var info = system?.GetJoypadDevice(d);
            if (info != null && (!info.IsConnected || !(info.HasLight || (info.HardwareFeatures & ControllerFeatures.Light) != 0))) continue;
            HapticsOutput?.SetJoypadLight(d, color);
        }
    }

    // ------------------------------------------------------------------
    //  Adaptive triggers (resistance) — separate from trigger rumble
    // ------------------------------------------------------------------

    private readonly Dictionary<int, (TriggerResistance Left, TriggerResistance Right)> _triggerResistance = new();

    /// <summary>
    /// Sets the resistance of a pad's adaptive triggers (DualSense-style: a stiff section, a weapon-like
    /// click, a vibrating trigger). This is not trigger rumble (<see cref="HapticEffect.TriggerLeft"/>,
    /// impulse triggers): it goes only to pads whose hardware has <see cref="ControllerFeatures.AdaptiveTriggers"/>,
    /// through a <see cref="HapticsOutput"/> that implements <see cref="IAdaptiveTriggerOutput"/> (Godot has
    /// no API for it). Scaled by the vibration strength setting; <see cref="TriggerResistance.Off"/> releases it.
    /// </summary>
    public void SetTriggerResistance(TriggerSide side, TriggerResistance resistance, int device = SourceKey.AnyDevice)
    {
        if (HapticsOutput is not IAdaptiveTriggerOutput output) return;
        var system = GetParent() as EnhancedInput;
        var scaled = resistance.Scaled(VibrationScale);
        _hapticPads.Clear();
        if (JoypadDevices.Count > 0) _hapticPads.AddRange(JoypadDevices);
        else foreach (var d in JoypadsToRead(SourceKey.AnyDevice)) _hapticPads.Add(d);
        foreach (var d in _hapticPads)
        {
            if (device >= 0 && d != device) continue;
            var info = system?.GetJoypadDevice(d);
            if (info != null && (!info.IsConnected || (info.HardwareFeatures & ControllerFeatures.AdaptiveTriggers) == 0)) continue;
            _triggerResistance.TryGetValue(d, out var current);
            _triggerResistance[d] = side == TriggerSide.Left ? (scaled, current.Right) : (current.Left, scaled);
            output.SetTriggerResistance(d, side, scaled);
        }
    }

    /// <summary>Vibrates the handheld device itself (phones), if this player uses the touchscreen.</summary>
    public void VibrateHandheld(int milliseconds = 200, float amplitude = -1f)
    {
        if (!AcceptTouch) return;
        float scale = VibrationScale;
        if (scale <= 0f) return;
        // −1 is the platform's default strength; scale only when a strength is given or the setting lowers it.
        float a = amplitude < 0f ? (scale < 1f ? scale : -1f) : Mathf.Clamp(amplitude * scale, 0f, 1f);
        HapticsOutput?.VibrateHandheld(milliseconds, a);
    }
}
