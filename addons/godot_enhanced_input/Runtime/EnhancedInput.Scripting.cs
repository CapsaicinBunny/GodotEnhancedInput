using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// The GDScript-friendly API: snake_case methods on the autoload taking an action as an id or name (or the
/// resource) — <c>EnhancedInput.was_pressed("jump")</c>, <c>EnhancedInput.get_vector("move")</c>. GDScript
/// cannot use C# overloads or default arguments, so each method reads player 0 and has a <c>_for</c> variant
/// taking the player index. Actions are looked up among the player's active contexts and its config.
/// </summary>
public partial class EnhancedInput
{
#pragma warning disable IDE1006, CA1707 // snake_case on purpose: these are GDScript's names.

    /// <summary>The action for an id, name or resource, as seen by a player (null when unknown).</summary>
    public EnhancedInputAction find_action(Variant action) => Resolve(action, 0);

    /// <summary>Whether the action is triggered (held, for an action without triggers).</summary>
    public bool is_triggered(Variant action) => is_triggered_for(action, 0);

    /// <summary><see cref="is_triggered"/> for a player.</summary>
    public bool is_triggered_for(Variant action, int player) => Player(player)?.IsTriggered(Resolve(action, player)) ?? false;

    /// <summary>Whether the action's input went down this frame.</summary>
    public bool was_pressed(Variant action) => was_pressed_for(action, 0);

    /// <summary><see cref="was_pressed"/> for a player.</summary>
    public bool was_pressed_for(Variant action, int player) => Player(player)?.WasPressed(Resolve(action, player)) ?? false;

    /// <summary>Whether the action was released after firing this frame.</summary>
    public bool was_released(Variant action) => was_released_for(action, 0);

    /// <summary><see cref="was_released"/> for a player.</summary>
    public bool was_released_for(Variant action, int player) => Player(player)?.WasReleased(Resolve(action, player)) ?? false;

    /// <summary>Whether the action fired this frame.</summary>
    public bool was_triggered(Variant action) => was_triggered_for(action, 0);

    /// <summary><see cref="was_triggered"/> for a player.</summary>
    public bool was_triggered_for(Variant action, int player) => Player(player)?.WasTriggered(Resolve(action, player)) ?? false;

    /// <summary>Whether the input went down since the previous physics step (read it in <c>_physics_process</c>).</summary>
    public bool was_pressed_in_physics(Variant action) => was_pressed_in_physics_for(action, 0);

    /// <summary><see cref="was_pressed_in_physics"/> for a player.</summary>
    public bool was_pressed_in_physics_for(Variant action, int player) => Player(player)?.PhysicsFrame.WasPressed(Resolve(action, player)) ?? false;

    /// <summary>Whether the action was released since the previous physics step.</summary>
    public bool was_released_in_physics(Variant action) => Player(0)?.PhysicsFrame.WasReleased(Resolve(action, 0)) ?? false;

    /// <summary>The value's first axis (a trigger, a throttle, a 1D axis).</summary>
    public float get_axis(Variant action) => get_axis_for(action, 0);

    /// <summary><see cref="get_axis"/> for a player.</summary>
    public float get_axis_for(Variant action, int player) => Player(player)?.ReadFloat(Resolve(action, player)) ?? 0f;

    /// <summary>The value's first two axes (a stick, movement).</summary>
    public Vector2 get_vector(Variant action) => get_vector_for(action, 0);

    /// <summary><see cref="get_vector"/> for a player.</summary>
    public Vector2 get_vector_for(Variant action, int player) => Player(player)?.ReadVector2(Resolve(action, player)) ?? Vector2.Zero;

    /// <summary>The value's first three axes.</summary>
    public Vector3 get_vector3(Variant action) => Player(0)?.ReadVector3(Resolve(action, 0)) ?? Vector3.Zero;

    /// <summary>The value as a rotation.</summary>
    public Quaternion get_rotation(Variant action) => Player(0)?.ReadRotation(Resolve(action, 0)) ?? Quaternion.Identity;

    /// <summary>Input buffering: true once per activation within the window (see <see cref="InputPlayer.ConsumeTriggered"/>).</summary>
    public bool consume_triggered(Variant action, double within_seconds) => consume_triggered_for(action, within_seconds, 0);

    /// <summary><see cref="consume_triggered"/> for a player.</summary>
    public bool consume_triggered_for(Variant action, double within_seconds, int player)
        => Player(player)?.ConsumeTriggered(Resolve(action, player), within_seconds) ?? false;

    /// <summary>Activates a mapping context (an id or name from the config, or the resource) on player 0.</summary>
    public bool activate_context(Variant context) => activate_context_for(context, 0);

    /// <summary><see cref="activate_context"/> for a player.</summary>
    public bool activate_context_for(Variant context, int player)
    {
        var target = Player(player);
        if (target == null) return false;
        if (context.AsGodotObject() is EnhancedInputMappingContext resource)
        {
            target.AddContext(resource, resource.DefaultPriority);
            return true;
        }
        return target.ActivateContext(context.AsString());
    }

    /// <summary>Deactivates a mapping context on player 0.</summary>
    public bool deactivate_context(Variant context) => deactivate_context_for(context, 0);

    /// <summary><see cref="deactivate_context"/> for a player.</summary>
    public bool deactivate_context_for(Variant context, int player)
    {
        var target = Player(player);
        if (target == null) return false;
        return context.AsGodotObject() is EnhancedInputMappingContext resource ? target.RemoveContext(resource) : target.DeactivateContext(context.AsString());
    }

    /// <summary>A prompt for the action ("Space / A") on the player's current device.</summary>
    public string get_prompt(Variant action) => Player(0)?.GetPromptText(Resolve(action, 0)) ?? string.Empty;

    /// <summary>The player with an index (null when there is none).</summary>
    public InputPlayer get_player(int index) => GetPlayer(index);

#pragma warning restore IDE1006, CA1707

    private InputPlayer Player(int index) => GetPlayer(index);

    private EnhancedInputAction Resolve(Variant action, int player)
    {
        if (action.VariantType == Variant.Type.Object) return action.AsGodotObject() as EnhancedInputAction;
        var name = action.AsString();
        if (string.IsNullOrEmpty(name)) return null;
        return GetPlayer(player)?.FindAction(name) ?? Configuration?.FindAction(name);
    }
}
