using Godot;

namespace GodotEnhancedInput;

/// <summary>How a haptic effect combines with others on the same device.</summary>
public enum HapticBlend
{
    /// <summary>The strongest value per motor wins (default): an explosion over an engine rumble feels like the explosion, and the engine continues after.</summary>
    Max,
    /// <summary>Values add up (clamped to 1).</summary>
    Add,
    /// <summary>While playing, effects of lower priority on the same device are silenced.</summary>
    Override,
}

/// <summary>
/// A vibration pattern: motor strengths, a length, an attack and release, an optional intensity
/// <see cref="Envelope"/>, repeats, a frequency for XR controllers, trigger motors for outputs that
/// drive them, and a buzz on phones. Play it with <see cref="InputPlayer.PlayHaptic"/>; several
/// effects playing at once mix (the strongest wins per motor), scaled by the player's vibration
/// strength setting. Ready-made patterns: <see cref="Tap"/>, <see cref="Click"/>, <see cref="Bump"/>,
/// <see cref="Thud"/>, <see cref="Heartbeat"/>, <see cref="Explosion"/>, <see cref="Engine"/>,
/// <see cref="Constant"/>.
/// </summary>
[Tool]
[GlobalClass]
public partial class HapticEffect : Resource
{
    /// <summary>The light, fast (high-frequency) motor, 0…1.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float Weak { get; set; } = 0.5f;

    /// <summary>The heavy, slow (low-frequency) motor, 0…1.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float Strong { get; set; } = 0.5f;

    /// <summary>Length of one pulse in seconds; 0 plays until stopped.</summary>
    [Export(PropertyHint.Range, "0,10,0.01,or_greater,suffix:s")] public float Duration { get; set; } = 0.2f;

    /// <summary>Time to rise from nothing to full strength.</summary>
    [Export(PropertyHint.Range, "0,2,0.01,suffix:s")] public float Attack { get; set; }

    /// <summary>Time to fade out at the end of each pulse.</summary>
    [Export(PropertyHint.Range, "0,2,0.01,suffix:s")] public float Release { get; set; }

    /// <summary>Optional intensity over one pulse (x 0…1 across <see cref="Duration"/>, y 0…1), multiplied in.</summary>
    [Export] public Curve Envelope { get; set; }

    /// <summary>Extra pulses after the first.</summary>
    [Export(PropertyHint.Range, "0,100,1,or_greater")] public int Repeats { get; set; }

    /// <summary>Pause between pulses.</summary>
    [Export(PropertyHint.Range, "0,5,0.01,suffix:s")] public float Gap { get; set; } = 0.1f;

    /// <summary>Vibration frequency for devices that take one (XR controllers); 0 uses the device's default.</summary>
    [Export(PropertyHint.Range, "0,1000,1,suffix:Hz")] public float Frequency { get; set; }

    /// <summary>Left and right trigger motors (outputs that support them; Godot's joypad API does not).</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float TriggerLeft { get; set; }

    /// <inheritdoc cref="TriggerLeft"/>
    [Export(PropertyHint.Range, "0,1,0.01")] public float TriggerRight { get; set; }

    /// <summary>Also buzz the phone or tablet itself (when the player uses the touchscreen).</summary>
    [Export] public bool Handheld { get; set; } = true;

    /// <summary>Repeat forever until stopped (ignores <see cref="Repeats"/>).</summary>
    [Export] public bool Loop { get; set; }

    /// <summary>Higher priority effects win over lower ones that use <see cref="HapticBlend.Override"/>.</summary>
    [Export(PropertyHint.Range, "-100,100,1")] public int Priority { get; set; }

    /// <summary>How this effect combines with others playing on the same device.</summary>
    [Export] public HapticBlend Blend { get; set; } = HapticBlend.Max;

    /// <summary>Total length in seconds (0: until stopped).</summary>
    public double TotalSeconds => Duration <= 0f ? 0.0 : Duration * (Repeats + 1) + Gap * Repeats;

    /// <summary>
    /// The intensity (0…1) at <paramref name="time"/> seconds after the start; <paramref name="done"/>
    /// becomes true once the effect is over.
    /// </summary>
    public float Sample(double time, out bool done) => Sample(time, Loop, out done);

    /// <summary>The intensity at a time, looping when <paramref name="loop"/> is set.</summary>
    public float Sample(double time, bool loop, out bool done)
    {
        done = false;
        if (time < 0) return 0f;
        double local = time;
        if (Duration > 0f)
        {
            double cycle = Duration + Gap;
            int pulse = cycle > 0 ? (int)(time / cycle) : 0;
            if (loop) pulse = 0;
            if (loop && cycle > 0) local = time % cycle;
            else
            {
                if (pulse > Repeats) { done = true; return 0f; }
                local = time - pulse * cycle;
            }
            if (local >= Duration) return !loop && pulse >= Repeats ? Finish(out done) : 0f;
        }
        float f = 1f;
        if (Attack > 0f && local < Attack) f *= (float)(local / Attack);
        if (Duration > 0f && Release > 0f && local > Duration - Release) f *= Mathf.Clamp((float)((Duration - local) / Release), 0f, 1f);
        if (Envelope != null && Duration > 0f) f *= Mathf.Clamp(Envelope.Sample((float)(local / Duration)), 0f, 1f);
        return f;
    }

    private static float Finish(out bool done)
    {
        done = true;
        return 0f;
    }

    // ------------------------------------------------------------------
    //  Ready-made patterns
    // ------------------------------------------------------------------

    /// <summary>A very light, very short tick (menu navigation).</summary>
    public static HapticEffect Tap() => new() { Weak = 0.25f, Strong = 0f, Duration = 0.03f, Handheld = false };

    /// <summary>A crisp click (confirm, pick up).</summary>
    public static HapticEffect Click() => new() { Weak = 0.6f, Strong = 0.1f, Duration = 0.05f };

    /// <summary>A soft bump (landing, light hit).</summary>
    public static HapticEffect Bump() => new() { Weak = 0.3f, Strong = 0.5f, Duration = 0.12f, Release = 0.06f };

    /// <summary>A heavy thud (big hit, door slam).</summary>
    public static HapticEffect Thud() => new() { Weak = 0.4f, Strong = 1f, Duration = 0.25f, Release = 0.15f };

    /// <summary>Two beats, like a heartbeat (low health).</summary>
    public static HapticEffect Heartbeat() => new() { Weak = 0.1f, Strong = 0.7f, Duration = 0.1f, Release = 0.05f, Repeats = 1, Gap = 0.12f };

    /// <summary>A strong hit that fades out (explosion).</summary>
    public static HapticEffect Explosion() => new() { Weak = 0.8f, Strong = 1f, Duration = 0.8f, Release = 0.7f };

    /// <summary>A continuous rumble until stopped (engine, drill), with a short fade in.</summary>
    public static HapticEffect Engine(float strength = 0.4f) => new() { Weak = strength * 0.6f, Strong = strength, Duration = 0f, Attack = 0.1f, Handheld = false };

    /// <summary>Constant motor strengths for a time (0: until stopped).</summary>
    public static HapticEffect Constant(float weak, float strong, float seconds) => new() { Weak = weak, Strong = strong, Duration = seconds, Handheld = false };
}
