using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Building bindings in code from text paths, with the common triggers and modifiers by name.
/// <code>
/// var gameplay = EnhancedInputMappingContext.Create("gameplay")
///     .Bind(move, "wasd", "pad:left_stick")
///     .Bind(jump, "key:space", "pad:a");
/// gameplay.Add(fire, "mouse:left").Pressed();
/// gameplay.Add(charge, "key:f").Hold(0.8f);
/// gameplay.Add(look, "pad:right_stick").Deadzone(0.15f).Scale(3f);
/// </code>
/// </summary>
public partial class EnhancedInputMappingContext
{
    /// <summary>Adds a binding read from a source path (see <see cref="InputSourcePath"/>) and returns it.</summary>
    public ActionBinding Add(EnhancedInputAction action, string path, params Resource[] modifiersAndTriggers)
        => Add(action, InputSourcePath.Parse(path), modifiersAndTriggers);

    /// <summary>Binds an action to one or more source paths ("key:space", "pad:a", "wasd") and returns the context.</summary>
    public EnhancedInputMappingContext Bind(EnhancedInputAction action, params string[] paths)
    {
        foreach (var path in paths) Add(action, path);
        return this;
    }
}

public partial class ActionBinding
{
    /// <summary>Adds modifiers and triggers (in order) and returns the binding.</summary>
    public ActionBinding With(params Resource[] modifiersAndTriggers)
    {
        foreach (var extra in modifiersAndTriggers)
        {
            switch (extra)
            {
                case InputModifier modifier: Modifiers.Add(modifier); break;
                case InputTrigger trigger: Triggers.Add(trigger); break;
            }
        }
        return this;
    }

    /// <summary>Fires once when the input goes down (<see cref="PressedTrigger"/>).</summary>
    public ActionBinding Pressed() => With(new PressedTrigger());

    /// <summary>Fires once when the input is let go (<see cref="ReleasedTrigger"/>).</summary>
    public ActionBinding Released() => With(new ReleasedTrigger());

    /// <summary>Fires after the input is held for <paramref name="seconds"/> (<see cref="HoldTrigger"/>).</summary>
    public ActionBinding Hold(float seconds = 0.5f, bool once = false) => With(new HoldTrigger { HoldSeconds = seconds, OneShot = once });

    /// <summary>Fires on a release within <paramref name="seconds"/> of the press (<see cref="TapTrigger"/>).</summary>
    public ActionBinding Tap(float seconds = 0.2f) => With(new TapTrigger { TapSeconds = seconds });

    /// <summary>Ignores small movement (<see cref="DeadzoneModifier"/>, radial).</summary>
    public ActionBinding Deadzone(float lower = 0.2f, float upper = 1f) => With(new DeadzoneModifier { Lower = lower, Upper = upper });

    /// <summary>Multiplies the value (<see cref="ScaleModifier"/>).</summary>
    public ActionBinding Scale(float factor) => With(new ScaleModifier { Scale = new Vector3(factor, factor, factor) });

    /// <summary>Flips the value (<see cref="NegateModifier"/>).</summary>
    public ActionBinding Negate() => With(new NegateModifier());
}
