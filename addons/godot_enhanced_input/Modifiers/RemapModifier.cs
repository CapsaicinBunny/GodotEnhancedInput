using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Maps each component linearly from an input range to an output range — e.g. a pedal axis that rests at
/// −1 and reads +1 pressed onto 0…1, or a throttle to 0…100. Optionally clamps to the output range.
/// </summary>
[Tool]
[GlobalClass]
public partial class RemapModifier : InputModifier
{
    [Export] public Vector3 InputMin { get; set; } = new(-1f, -1f, -1f);
    [Export] public Vector3 InputMax { get; set; } = new(1f, 1f, 1f);
    [Export] public Vector3 OutputMin { get; set; } = Vector3.Zero;
    [Export] public Vector3 OutputMax { get; set; } = Vector3.One;
    [Export] public bool Clamp { get; set; } = true;

    public override Vector3 Apply(Vector3 v, in ModifierContext context)
        => new(Map(v.X, InputMin.X, InputMax.X, OutputMin.X, OutputMax.X),
               Map(v.Y, InputMin.Y, InputMax.Y, OutputMin.Y, OutputMax.Y),
               Map(v.Z, InputMin.Z, InputMax.Z, OutputMin.Z, OutputMax.Z));

    private float Map(float v, float inMin, float inMax, float outMin, float outMax)
    {
        if (inMax == inMin) return outMin;
        float t = (v - inMin) / (inMax - inMin);
        if (Clamp) t = Mathf.Clamp(t, 0f, 1f);
        return outMin + t * (outMax - outMin);
    }
}
