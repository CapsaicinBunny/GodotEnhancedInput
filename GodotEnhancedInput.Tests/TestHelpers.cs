using Godot;

namespace GodotEnhancedInput.Tests;

/// <summary>
/// A standalone <see cref="InputPlayer"/> for one test, with shortcuts for feeding events and ticking.
/// Frees the node on dispose (no frames run in the shared engine, so QueueFree would never happen).
/// </summary>
public sealed class Rig : IDisposable
{
    public const double Dt = 1.0 / 60.0;

    public InputPlayer Player { get; } = new();

    public Rig Add(BindingContext context, int priority = 0, ContextOptions options = default)
    {
        Player.AddContext(context, priority, options);
        return this;
    }

    public Rig Press(Key key) { Player.FeedEvent(KeyEvent(key, true)); return this; }
    public Rig Release(Key key) { Player.FeedEvent(KeyEvent(key, false)); return this; }

    public Rig Tick(int ticks = 1, double dt = Dt)
    {
        for (int i = 0; i < ticks; i++) Player.Tick(dt);
        return this;
    }

    /// <summary>Ticks until <paramref name="seconds"/> have passed (rounded to whole ticks).</summary>
    public Rig TickFor(double seconds, double dt = Dt) => Tick((int)Math.Round(seconds / dt), dt);

    public static InputEventKey KeyEvent(Key physical, bool pressed, bool echo = false)
        => new() { PhysicalKeycode = physical, Pressed = pressed, Echo = echo };

    public void Dispose()
    {
        if (GodotObject.IsInstanceValid(Player))
        {
            Player.GetParent()?.RemoveChild(Player);
            Player.Free();
        }
    }
}

public static class Make
{
    public static GameAction Action(string name, ActionValueKind kind = ActionValueKind.Digital) => GameAction.Create(name, kind);

    public static BindingContext Context(string name, params ActionBinding[] bindings)
    {
        var context = BindingContext.Create(name);
        foreach (var binding in bindings) context.Bindings.Add(binding);
        return context;
    }

    public static ActionBinding Bind(GameAction action, InputSource source, params Resource[] extras)
        => ActionBinding.Create(action, source, extras);

    public static ActionBinding Bind(GameAction action, Key key, params Resource[] extras)
        => ActionBinding.Create(action, InputSource.ForKey(key), extras);
}
