using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Input buffering in data: a press stays triggered for <see cref="BufferSeconds"/>, held or not, so an
/// input that comes a little early is not lost — Jump pressed just before landing, an attack pressed
/// during the previous one's recovery. A new press restarts the window. Read the action with
/// <see cref="InputPlayer.ConsumeTriggered"/> (<c>jump.ConsumeTriggered()</c>) so one press is used once;
/// polling <c>IsTriggered</c> sees it for the whole window. The action completes when the window ends,
/// not on release (use another action, or <see cref="ActionState.InputValue"/>, to read the release).
/// </summary>
[Tool]
public partial class BufferTrigger : InputTrigger
{
    /// <summary>How long a press stays available.</summary>
    [Export(PropertyHint.Range, "0.01,2,0.01,suffix:s")] public float BufferSeconds { get; set; } = 0.15f;

    /// <inheritdoc/>
    protected override TriggerPhase Update(in TriggerContext context, bool actuated, bool wasActuated)
    {
        var m = context.Memory;   // Flag: window open · Timer: time since the press
        if (actuated && !wasActuated)
        {
            m.Flag = true;
            m.Timer = 0;
            return TriggerPhase.Triggered;
        }
        if (!m.Flag) return TriggerPhase.Idle;
        m.Timer += context.Delta;
        if (m.Timer > BufferSeconds + TimeEpsilon)
        {
            m.Flag = false;
            return TriggerPhase.Idle;
        }
        return TriggerPhase.Triggered;
    }
}
