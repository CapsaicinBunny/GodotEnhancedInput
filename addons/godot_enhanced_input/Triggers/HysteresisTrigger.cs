using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Like <see cref="DownTrigger"/>, but turns on at <see cref="PressThreshold"/> and off only below
/// <see cref="ReleaseThreshold"/>, so an analog trigger or stick hovering near one threshold does not
/// flicker between pressed and released.
/// </summary>
[Tool]
[GlobalClass]
public partial class HysteresisTrigger : InputTrigger
{
    [Export(PropertyHint.Range, "0,1,0.01")] public float PressThreshold { get; set; } = 0.6f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float ReleaseThreshold { get; set; } = 0.4f;

    protected override TriggerPhase Update(in TriggerContext context, bool actuated, bool wasActuated)
    {
        var m = context.Memory;
        float magnitude = context.Value.Length();
        if (!m.Flag && magnitude > 0f && magnitude >= PressThreshold) m.Flag = true;
        else if (m.Flag && (magnitude == 0f || magnitude < ReleaseThreshold)) m.Flag = false;
        return m.Flag ? TriggerPhase.Triggered : TriggerPhase.Idle;
    }
}
