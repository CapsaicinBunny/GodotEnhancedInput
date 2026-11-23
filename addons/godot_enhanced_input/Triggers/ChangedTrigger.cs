using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Triggered on every tick the value differs from the previous tick's (by more than
/// <see cref="Tolerance"/> on any component) — including the tick it returns to zero, so a "value
/// changed" callback also hears the stop. The actuation threshold is not used.
/// </summary>
[Tool]
[GlobalClass]
public partial class ChangedTrigger : InputTrigger
{
    /// <summary>Smallest change per component that counts.</summary>
    [Export(PropertyHint.Range, "0,1,0.0001,or_greater")] public float Tolerance { get; set; }

    protected override TriggerPhase Update(in TriggerContext context, bool actuated, bool wasActuated)
    {
        var memory = context.Memory;
        var delta = (context.Value - memory.PreviousValue).Abs();
        memory.PreviousValue = context.Value;
        bool changed = Tolerance <= 0f ? delta != Vector3.Zero : delta.X > Tolerance || delta.Y > Tolerance || delta.Z > Tolerance;
        return changed ? TriggerPhase.Triggered : TriggerPhase.Idle;
    }
}
