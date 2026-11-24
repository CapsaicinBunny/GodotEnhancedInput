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

    /// <summary>A second general-purpose timer in seconds.</summary>
    public double Timer2;

    /// <summary>A general-purpose counter.</summary>
    public int Count;

    /// <summary>A general-purpose flag.</summary>
    public bool Flag;

    /// <summary>Whether the value was actuated last tick (kept up to date by <see cref="InputTrigger.Evaluate"/>).</summary>
    public bool WasActuated;

    /// <summary>The phase this trigger reported last tick.</summary>
    public TriggerPhase LastPhase;

    /// <summary>A value remembered from an earlier tick (e.g. the previous value).</summary>
    public Vector3 PreviousValue;

    /// <summary>A four-component value remembered from an earlier tick.</summary>
    public Vector4 PreviousValue4;

    /// <summary>Free slot for custom triggers.</summary>
    public object Custom;

    /// <summary>Forgets everything.</summary>
    public void Reset()
    {
        Timer = 0;
        Timer2 = 0;
        Count = 0;
        Flag = false;
        WasActuated = false;
        LastPhase = TriggerPhase.Idle;
        PreviousValue = Vector3.Zero;
        PreviousValue4 = Vector4.Zero;
        Custom = null;
    }
}

/// <summary>What a trigger is given besides the value.</summary>
public readonly struct TriggerContext
{
    /// <summary>Creates a context from a three-component value.</summary>
    public TriggerContext(Vector3 value, ActionValueKind kind, double delta, InputPlayer player, TriggerMemory memory)
        : this(new Vector4(value.X, value.Y, value.Z, 0f), kind, delta, player, memory) { }

    /// <summary>Creates a context (the player does this; tests and tools can too).</summary>
    public TriggerContext(Vector4 value, ActionValueKind kind, double delta, InputPlayer player, TriggerMemory memory)
    {
        Value4 = value;
        Value = new Vector3(value.X, value.Y, value.Z);
        Magnitude = ActionValue.MagnitudeOf(kind, value);
        Kind = kind;
        Delta = delta;
        Player = player;
        Memory = memory ?? new TriggerMemory();
    }

    /// <summary>The first three components of the value after modifiers.</summary>
    public Vector3 Value { get; }

    /// <summary>All four components of the value after modifiers.</summary>
    public Vector4 Value4 { get; }

    /// <summary>How strongly the value is actuated (see <see cref="ActionValue.MagnitudeOf"/>).</summary>
    public float Magnitude { get; }

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
[Icon("res://addons/godot_enhanced_input/Icons/InputTrigger.svg")]
public abstract partial class InputTrigger : Resource
{
    /// <summary>Tiny tolerance for comparing accumulated tick times against configured durations.</summary>
    protected const double TimeEpsilon = 1e-6;

    /// <summary>
    /// The magnitude at which a value counts as actuated. Zero is never actuated, whatever the threshold.
    /// </summary>
    [Export(PropertyHint.Range, "0,1,0.01,or_greater")] public float ActuationThreshold { get; set; } = 0.5f;

    /// <summary>Player setting that scales hold times (hold, hold-release, long presses). 1 when not set.</summary>
    public const string HoldTimeScaleSetting = "hold_time_scale";

    /// <summary>Player setting that scales tap windows (tap, multi-tap). 1 when not set.</summary>
    public const string TapTimeScaleSetting = "tap_time_scale";

    /// <summary>Player setting that scales repeat and pulse intervals. 1 when not set.</summary>
    public const string RepeatTimeScaleSetting = "repeat_time_scale";

    /// <summary>A duration scaled by one of the player's timing settings (accessibility, comfort).</summary>
    protected static double Scaled(in TriggerContext context, double seconds, string setting)
        => seconds * (context.Player?.GetSetting(setting, 1.0) ?? 1.0);

    /// <summary>How this trigger combines with others in the same set.</summary>
    public virtual TriggerRole Role => TriggerRole.Explicit;

    /// <summary>
    /// True for triggers that look at other actions (chords, blockers, combos, code): their action is
    /// evaluated at every step of a tick, so it sees the other actions' changes in order.
    /// </summary>
    public virtual bool ReadsOtherActions => false;

    /// <summary>
    /// Evaluates this trigger for one tick. Keeps <see cref="TriggerMemory.WasActuated"/> and
    /// <see cref="TriggerMemory.LastPhase"/> up to date around <see cref="Update"/>.
    /// </summary>
    public TriggerPhase Evaluate(in TriggerContext context)
    {
        bool actuated = IsActuated(context.Magnitude);
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
    public bool IsActuated(Vector3 value) => IsActuated(value.Length());

    /// <summary>Whether a magnitude reaches <see cref="ActuationThreshold"/> (and is non-zero).</summary>
    public bool IsActuated(float magnitude) => magnitude > 0f && magnitude >= ActuationThreshold;
}
