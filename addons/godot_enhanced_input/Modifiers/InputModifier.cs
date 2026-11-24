using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Per-binding, per-player memory a modifier may keep between ticks. Modifier resources are shared,
/// so anything that changes over time belongs here, never in the resource.
/// </summary>
public sealed class ModifierMemory
{
    /// <summary>The value this modifier produced last tick.</summary>
    public Vector3 Previous;

    /// <summary>Whether <see cref="Previous"/> has been set.</summary>
    public bool HasPrevious;

    /// <summary>The four-component value produced last tick (modifiers that work on all four components).</summary>
    public Vector4 Previous4;

    /// <summary>A ring of recent samples (allocated by the modifier when needed).</summary>
    public Vector3[] Samples;

    /// <summary>A ring of recent four-component samples (allocated by the modifier when needed).</summary>
    public Vector4[] Samples4;

    /// <summary>How many samples are stored.</summary>
    public int SampleCount;

    /// <summary>Where the next sample goes.</summary>
    public int SampleIndex;

    /// <summary>Free slot for custom modifiers.</summary>
    public object Custom;

    /// <summary>Forgets everything.</summary>
    public void Reset()
    {
        Previous = Vector3.Zero;
        Previous4 = Vector4.Zero;
        HasPrevious = false;
        SampleCount = 0;
        SampleIndex = 0;
        Custom = null;
    }
}

/// <summary>What a modifier is given besides the value.</summary>
public readonly struct ModifierContext
{
    public ModifierContext(ActionValueKind kind, double delta, InputPlayer player, ModifierMemory memory, ValueNature nature = ValueNature.State)
    {
        Kind = kind;
        Delta = delta;
        Player = player;
        Memory = memory ?? new ModifierMemory();
        Nature = nature;
    }

    /// <summary>What the value is: a held state, a per-tick delta, or an absolute position (action-level modifiers see State).</summary>
    public ValueNature Nature { get; }

    /// <summary>The kind of the action being evaluated.</summary>
    public ActionValueKind Kind { get; }

    /// <summary>Seconds since the previous tick.</summary>
    public double Delta { get; }

    /// <summary>The player being ticked (may be null in isolated tests).</summary>
    public InputPlayer Player { get; }

    /// <summary>This modifier's memory for this binding and player.</summary>
    public ModifierMemory Memory { get; }
}

/// <summary>
/// Changes a value on its way from a source to an action. Binding modifiers run per binding before
/// its triggers; action modifiers run on the combined value before the action's triggers.
/// </summary>
[Tool]
[GlobalClass]
[Icon("res://addons/godot_enhanced_input/Icons/InputModifier.svg")]
public abstract partial class InputModifier : Resource
{
    /// <summary>A disabled modifier passes values through unchanged.</summary>
    [Export] public bool Enabled { get; set; } = true;

    /// <summary>
    /// Returns the changed value for the first three components. Most modifiers only override this;
    /// the fourth component (W of a 4D value or a quaternion) then passes through unchanged.
    /// </summary>
    public virtual Vector3 Apply(Vector3 value, in ModifierContext context) => value;

    /// <summary>
    /// Returns the changed value for all four components. The default applies
    /// <see cref="Apply(Vector3, in ModifierContext)"/> to X, Y and Z and keeps W. Override for modifiers that
    /// need all four (4D vectors, quaternions).
    /// </summary>
    public virtual Vector4 Apply(Vector4 value, in ModifierContext context)
    {
        var xyz = Apply(new Vector3(value.X, value.Y, value.Z), context);
        return new Vector4(xyz.X, xyz.Y, xyz.Z, value.W);
    }

    /// <summary>Applies this modifier if it is enabled.</summary>
    public Vector4 Run(Vector4 value, in ModifierContext context) => Enabled ? Apply(value, context) : value;

    /// <summary>Applies this modifier to a three-component value if it is enabled.</summary>
    public Vector3 Run(Vector3 value, in ModifierContext context)
    {
        if (!Enabled) return value;
        var result = Apply(new Vector4(value.X, value.Y, value.Z, 0f), context);
        return new Vector3(result.X, result.Y, result.Z);
    }
}
