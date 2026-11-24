using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// A force-feedback state for wheels and force-feedback sticks: a constant force plus spring, damper
/// and friction conditions. Godot has no force-feedback API, so these reach hardware only through an
/// <see cref="InputBackend"/> or <see cref="IForceFeedbackOutput"/> that talks to a native library;
/// without one they are ignored.
/// </summary>
public struct ForceFeedback
{
    /// <summary>A steady push, −1 (left / back) … 1 (right / forward).</summary>
    public float Constant;

    /// <summary>Pull toward <see cref="Center"/>, 0…1.</summary>
    public float Spring;

    /// <summary>Resistance proportional to speed, 0…1.</summary>
    public float Damper;

    /// <summary>Constant resistance to movement, 0…1.</summary>
    public float Friction;

    /// <summary>Where the spring pulls to, −1…1.</summary>
    public float Center;

    /// <summary>A short jolt added on top (kerbs, collisions), −1…1, for one update.</summary>
    public float Jolt;

    public static ForceFeedback None => default;

    /// <summary>A typical self-centring wheel.</summary>
    public static ForceFeedback Centering(float spring = 0.5f, float damper = 0.2f) => new() { Spring = spring, Damper = damper };

    /// <summary>Scaled by a strength (the player's vibration/force setting).</summary>
    public ForceFeedback Scaled(float k) => new()
    {
        Constant = Constant * k, Spring = Spring * k, Damper = Damper * k, Friction = Friction * k, Center = Center, Jolt = Jolt * k,
    };
}

/// <summary>Optional extra for an <see cref="IHapticsOutput"/> that can drive force feedback on joypad devices.</summary>
public interface IForceFeedbackOutput
{
    void SetForceFeedback(int device, ForceFeedback force);
}

public partial class InputPlayer
{
    /// <summary>Profile setting scaling force feedback (1 when unset; 0 turns it off). Default "force_feedback_strength".</summary>
    public string ForceFeedbackStrengthSetting { get; set; } = "force_feedback_strength";

    /// <summary>
    /// Sets the force feedback of this player's wheels: backend devices it owns (through
    /// <see cref="InputBackend.SetForceFeedback"/>) and joypads (when <see cref="HapticsOutput"/> also
    /// implements <see cref="IForceFeedbackOutput"/>). Call it whenever the forces change (e.g. every
    /// physics step while driving); pass <see cref="ForceFeedback.None"/> to release the wheel.
    /// </summary>
    public void SetForceFeedback(ForceFeedback force, int device = SourceKey.AnyDevice)
    {
        float k = Mathf.Clamp((float)Profile.GetSetting(ForceFeedbackStrengthSetting, 1.0), 0f, 2f);
        var scaled = force.Scaled(k);
        if (HapticsOutput is IForceFeedbackOutput ff)
        {
            _hapticPads.Clear();
            if (JoypadDevices.Count > 0) _hapticPads.AddRange(JoypadDevices);
            else foreach (var d in JoypadsToRead(SourceKey.AnyDevice)) _hapticPads.Add(d);
            foreach (var d in _hapticPads)
                if (device < 0 || d == device) ff.SetForceFeedback(d, scaled);
        }
        if (device >= 0 || GetParent() is not EnhancedInput system) return;
        foreach (var id in OwnedCustomDevices)
        {
            var info = system.GetDevice(id);
            if (info?.Backend != null && info.IsConnected) info.Backend.SetForceFeedback(info, scaled);
        }
    }
}
