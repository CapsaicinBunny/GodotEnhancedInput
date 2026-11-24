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

        // Pass 1: advance, drop finished effects, find the top override priority per device.
        for (int i = _haptics.Count - 1; i >= 0; i--)
        {
            var h = _haptics[i];
            h.Time += delta;
            h.Intensity = h.Effect.Sample(h.Time, h.Loop, out bool done) * h.Strength * scale;
            if (done) { h.IsPlaying = false; _haptics.RemoveAt(i); continue; }
            if (h.Effect.Blend != HapticBlend.Override || h.Intensity <= 0f) continue;
            if (h.DeviceId != null) Raise(_customTop, h.DeviceId, h.Effect.Priority);
            else
            {
                foreach (var d in HapticJoypads(h.Device)) Raise(_hapticTop, d, h.Effect.Priority);
                if (h.Device < 0) foreach (var id in OwnedCustomDevices) Raise(_customTop, id, h.Effect.Priority);
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
                    if (_hapticTop.TryGetValue(d, out var top) && e.Priority < top) continue;
                    _hapticMix[d] = _hapticMix.TryGetValue(d, out var m) ? Combine(m, v, e.Blend) : v;
                }
                if (h.Device < 0) foreach (var id in OwnedCustomDevices) MixCustom(id, e, v);
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
            if (known && !refresh && !Changed(sent, v)) continue;
            output?.StartJoypad(pair.Key, Mathf.Clamp(v.X, 0f, 1f), Mathf.Clamp(v.Y, 0f, 1f), hold);
            if (v.Z > 0f || v.W > 0f || sent.Z > 0f || sent.W > 0f) output?.StartTriggers(pair.Key, Mathf.Clamp(v.Z, 0f, 1f), Mathf.Clamp(v.W, 0f, 1f), hold);
        }
        _hapticStops.Clear();
        foreach (var d in _hapticSent.Keys)
            if (!_hapticMix.TryGetValue(d, out var v) || v == Vector4.Zero) _hapticStops.Add(d);
        foreach (var d in _hapticStops)
        {
            output?.StopJoypad(d);
            if (_hapticSent[d].Z > 0f || _hapticSent[d].W > 0f) output?.StartTriggers(d, 0f, 0f, 0f);
            _hapticSent.Remove(d);
        }
        foreach (var pair in _hapticMix)
            if (pair.Value != Vector4.Zero) _hapticSent[pair.Key] = pair.Value;

        if (system == null) return;
        foreach (var pair in _customMix)
        {
            if (pair.Value.X <= 0f && pair.Value.Y <= 0f) continue;
            bool known = _customSent.TryGetValue(pair.Key, out var sent);
            if (known && !refresh && !Changed(new Vector4(sent.X, sent.Y, sent.Z * 0.01f, 0f), new Vector4(pair.Value.X, pair.Value.Y, pair.Value.Z * 0.01f, 0f))) continue;
            var info = system.GetDevice(pair.Key);
            if (info?.Backend != null && info.IsConnected) info.Backend.Rumble(info, pair.Value.X, pair.Value.Y, hold, pair.Value.Z);
        }
        _customStops.Clear();
        foreach (var id in _customSent.Keys)
            if (!_customMix.TryGetValue(id, out var v) || (v.X <= 0f && v.Y <= 0f)) _customStops.Add(id);
        foreach (var id in _customStops)
        {
            var info = system.GetDevice(id);
            if (info?.Backend != null && info.IsConnected) info.Backend.Rumble(info, 0f, 0f, 0f, 0f);
            _customSent.Remove(id);
        }
        foreach (var pair in _customMix)
            if (pair.Value.X > 0f || pair.Value.Y > 0f) _customSent[pair.Key] = pair.Value;
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

    /// <summary>Stops vibration: every effect on one joypad (or everything), including backend devices.</summary>
    public void StopRumble(int device = SourceKey.AnyDevice)
    {
        for (int i = _haptics.Count - 1; i >= 0; i--)
        {
            if (device >= 0 && _haptics[i].Device != device) continue;
            _haptics[i].IsPlaying = false;
            _haptics.RemoveAt(i);
        }
        UpdateHaptics(0);
        foreach (var d in HapticJoypads(device))
            if (!_hapticSent.ContainsKey(d)) HapticsOutput?.StopJoypad(d);
    }

    /// <summary>Colours the light of this player's joypads that have one (e.g. the player's colour).</summary>
    public void SetJoypadLight(Color color, int device = SourceKey.AnyDevice)
    {
        _hapticPads.Clear();
        if (JoypadDevices.Count > 0) _hapticPads.AddRange(JoypadDevices);
        else foreach (var d in JoypadsToRead(SourceKey.AnyDevice)) _hapticPads.Add(d);
        if (device >= 0) { bool mine = _hapticPads.Contains(device); _hapticPads.Clear(); if (mine) _hapticPads.Add(device); }
        foreach (var d in _hapticPads) HapticsOutput?.SetJoypadLight(d, color);
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
