using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Per-binding, per-player memory a trigger may keep between ticks. Trigger resources are shared,
/// so timers and counters belong here, never in the resource.
/// </summary>
public sealed class TriggerMemory
{
    /// <summary>A general-purpose timer in seconds.</summary>
    public double Timer;

    /// <summary>A general-purpose counter.</summary>
    public int Count;

    /// <summary>A general-purpose flag.</summary>
    public bool Flag;

    /// <summary>Whether the value was actuated last tick (kept up to date by <see cref="InputTrigger.Evaluate"/>).</summary>
    public bool WasActuated;

    /// <summary>The phase this trigger reported last tick.</summary>
    public TriggerPhase LastPhase;

    /// <summary>Free slot for custom triggers.</summary>
    public object Custom;

    /// <summary>Forgets everything.</summary>
    public void Reset()
    {
        Timer = 0;
        Count = 0;
        Flag = false;
        WasActuated = false;
        LastPhase = TriggerPhase.Idle;
        Custom = null;
    }
}

/// <summary>What a trigger is given besides the value.</summary>
public readonly struct TriggerContext
{
    public TriggerContext(Vector3 value, ActionValueKind kind, double delta, InputPlayer player, TriggerMemory memory)
    {
        Value = value;
        Kind = kind;
        Delta = delta;
        Player = player;
        Memory = memory ?? new TriggerMemory();
    }

    /// <summary>The value after modifiers.</summary>
    public Vector3 Value { get; }

    /// <summary>The kind of the action being evaluated.</summary>
    public ActionValueKind Kind { get; }

    /// <summary>Seconds since the previous tick.</summary>
    public double Delta { get; }

    /// <summary>The player being ticked; triggers that depend on other actions ask it (may be null in isolated tests).</summary>
    public InputPlayer Player { get; }

    /// <summary>This trigger's memory for this binding and player.</summary>
    public TriggerMemory Memory { get; }
}

/// <summary>
/// Decides, every tick, whether an action is idle, ongoing or triggered, from the value it receives
/// and from its own memory. See <see cref="TriggerRole"/> for how several triggers combine.
/// </summary>
[Tool]
[GlobalClass]
[Icon("res://addons/godot_enhanced_input/Icons/InputTrigger.svg")]
public abstract partial class InputTrigger : Resource
{
    /// <summary>Tiny tolerance for comparing accumulated tick times against configured durations.</summary>
    protected const double TimeEpsilon = 1e-6;

    /// <summary>
    /// The magnitude at which a value counts as actuated. Zero is never actuated, whatever the threshold.
    /// </summary>
    [Export(PropertyHint.Range, "0,1,0.01,or_greater")] public float ActuationThreshold { get; set; } = 0.5f;

    /// <summary>How this trigger combines with others in the same set.</summary>
    public virtual TriggerRole Role => TriggerRole.Explicit;

    /// <summary>
    /// Evaluates this trigger for one tick. Keeps <see cref="TriggerMemory.WasActuated"/> and
    /// <see cref="TriggerMemory.LastPhase"/> up to date around <see cref="Update"/>.
    /// </summary>
    public TriggerPhase Evaluate(in TriggerContext context)
    {
        bool actuated = IsActuated(context.Value);
        var phase = Update(context, actuated, context.Memory.WasActuated);
        context.Memory.WasActuated = actuated;
        context.Memory.LastPhase = phase;
        return phase;
    }

    /// <summary>The trigger's logic for one tick.</summary>
    /// <param name="context">Value, timing, player and memory.</param>
    /// <param name="actuated">Whether the value is actuated this tick.</param>
    /// <param name="wasActuated">Whether it was actuated last tick.</param>
    protected abstract TriggerPhase Update(in TriggerContext context, bool actuated, bool wasActuated);

    /// <summary>Whether a value reaches <see cref="ActuationThreshold"/> (and is non-zero).</summary>
    public bool IsActuated(Vector3 value)
    {
        float lengthSquared = value.LengthSquared();
        return lengthSquared > 0f && lengthSquared >= ActuationThreshold * ActuationThreshold;
    }
}
