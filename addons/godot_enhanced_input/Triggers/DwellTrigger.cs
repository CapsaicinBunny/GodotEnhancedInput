using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Look (or point) and hold still to select: ongoing while a position stays within <see cref="Radius"/>
/// of where it settled, triggered once it has stayed there for <see cref="DwellSeconds"/> (once, until it
/// moves away). Made for eye-gaze control and head pointers; also works with any pointer or stick.
/// The value is a position (e.g. "gaze" from <see cref="EyeTrackerBackend"/>); zero counts as "no gaze".
/// Dwell time scales with the player's "hold_time_scale" setting.
/// </summary>
[Tool]
[GlobalClass]
public partial class DwellTrigger : InputTrigger
{
    /// <summary>How far the position may wander while dwelling (in the value's units: 0…1 screen, pixels…).</summary>
    [Export(PropertyHint.Range, "0,500,0.001,or_greater")] public float Radius { get; set; } = 0.04f;

    /// <summary>How long to dwell before triggering.</summary>
    [Export(PropertyHint.Range, "0.05,5,0.01,suffix:s")] public float DwellSeconds { get; set; } = 0.8f;

    /// <summary>Trigger again after another full dwell while still looking at the same spot.</summary>
    [Export] public bool Repeat { get; set; }

    protected override TriggerPhase Update(in TriggerContext context, bool actuated, bool wasActuated)
    {
        var m = context.Memory;
        var position = context.Value;
        if (position == Vector3.Zero)
        {
            m.Timer = 0;
            m.Flag = false;
            m.Count = 0;
            return TriggerPhase.Idle;
        }
        if (m.Count == 0 || position.DistanceTo(m.PreviousValue) > Radius)
        {
            // Settled somewhere new: start dwelling here.
            m.PreviousValue = position;
            m.Count = 1;
            m.Timer = 0;
            m.Flag = false;
            return TriggerPhase.Ongoing;
        }
        m.Timer += context.Delta;
        if (m.Timer + TimeEpsilon < Scaled(context, DwellSeconds, HoldTimeScaleSetting)) return TriggerPhase.Ongoing;
        if (m.Flag && !Repeat) return TriggerPhase.Idle;
        m.Flag = true;
        if (Repeat) m.Timer = 0;
        return TriggerPhase.Triggered;
    }
}
