using System.Collections.Generic;
using Godot;

namespace GodotEnhancedInput;

/// <summary>Where vibration and light requests go. The default sends them to Godot; tests and custom hardware can replace it.</summary>
public interface IHapticsOutput
{
    void StartJoypad(int device, float weak, float strong, float seconds);
    void StopJoypad(int device);
    void SetJoypadLight(int device, Color color);
    void VibrateHandheld(int milliseconds, float amplitude);
}

/// <summary>Sends haptics to Godot's <see cref="Input"/>.</summary>
public sealed class GodotHapticsOutput : IHapticsOutput
{
    public void StartJoypad(int device, float weak, float strong, float seconds) => Input.StartJoyVibration(device, weak, strong, seconds);
    public void StopJoypad(int device) => Input.StopJoyVibration(device);
    public void SetJoypadLight(int device, Color color) => Input.SetJoyLight(device, color);
    public void VibrateHandheld(int milliseconds, float amplitude) => Input.VibrateHandheld(milliseconds, amplitude);
}

public partial class InputPlayer
{
    /// <summary>Backend devices this player owns (see <see cref="EnhancedInput.AssignDevice"/>).</summary>
    public HashSet<string> OwnedCustomDevices { get; } = new();

    /// <summary>Where this player's haptics go (Godot by default).</summary>
    public IHapticsOutput HapticsOutput { get; set; } = new GodotHapticsOutput();

    /// <summary>
    /// Profile setting that scales all of this player's vibration (0 turns it off), for accessibility
    /// and comfort settings. Default "vibration_strength".
    /// </summary>
    public string VibrationStrengthSetting { get; set; } = "vibration_strength";

    private float VibrationScale => Mathf.Clamp((float)Profile.GetSetting(VibrationStrengthSetting, 1.0), 0f, 1f);

    /// <summary>The joypads haptics go to: one device, the player's own, or the unowned connected ones it reads.</summary>
    private IEnumerable<int> HapticJoypads(int device)
    {
        if (device >= 0) { yield return device; yield break; }
        if (JoypadDevices.Count > 0)
        {
            foreach (var d in JoypadDevices) yield return d;
            yield break;
        }
        foreach (var d in JoypadsToRead(SourceKey.AnyDevice)) yield return d;
    }

    /// <summary>
    /// Vibrates this player's joypads (or one of them) and its backend devices: <paramref name="weak"/>
    /// and <paramref name="strong"/> motors 0…1 for <paramref name="seconds"/> (0: until stopped),
    /// scaled by the player's vibration strength setting.
    /// </summary>
    public void Rumble(float weak, float strong, float seconds, int device = SourceKey.AnyDevice)
    {
        float scale = VibrationScale;
        if (scale <= 0f) return;
        weak = Mathf.Clamp(weak * scale, 0f, 1f);
        strong = Mathf.Clamp(strong * scale, 0f, 1f);
        foreach (var d in HapticJoypads(device)) HapticsOutput?.StartJoypad(d, weak, strong, seconds);

        var system = GetParent() as EnhancedInput;
        if (system == null) return;
        foreach (var id in OwnedCustomDevices)
        {
            var info = system.GetDevice(id);
            if (info?.Backend != null && info.IsConnected) info.Backend.Rumble(info, weak, strong, seconds);
        }
    }

    /// <summary>Stops vibration on this player's joypads.</summary>
    public void StopRumble(int device = SourceKey.AnyDevice)
    {
        foreach (var d in HapticJoypads(device)) HapticsOutput?.StopJoypad(d);
    }

    /// <summary>Colours the light of this player's joypads that have one (e.g. the player's colour).</summary>
    public void SetJoypadLight(Color color, int device = SourceKey.AnyDevice)
    {
        foreach (var d in HapticJoypads(device)) HapticsOutput?.SetJoypadLight(d, color);
    }

    /// <summary>Vibrates the handheld device itself (phones), if this player uses the touchscreen.</summary>
    public void VibrateHandheld(int milliseconds = 200, float amplitude = -1f)
    {
        if (!AcceptTouch) return;
        float scale = VibrationScale;
        if (scale <= 0f) return;
        HapticsOutput?.VibrateHandheld(milliseconds, amplitude < 0 ? amplitude : Mathf.Clamp(amplitude * scale, 0f, 1f));
    }
}
