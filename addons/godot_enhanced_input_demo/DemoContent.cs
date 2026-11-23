using Godot;

namespace GodotEnhancedInput.Demo;

/// <summary>
/// Builds the demo's actions and contexts in code. The same resources are saved as .tres files in
/// this folder (see <c>DemoContentWriter</c> in the tests), so they can also be opened and edited in
/// the Inspector. The demo scene uses the .tres files.
/// </summary>
public sealed class DemoContent
{
    public const string Folder = "res://addons/godot_enhanced_input_demo";

    public GameAction Move { get; } = Action("move", "Move", ActionValueKind.Axis2D);
    public GameAction Sprint { get; } = Action("sprint", "Sprint");
    public GameAction Jump { get; } = Action("jump", "Jump");
    public GameAction Dash { get; } = Action("dash", "Dash");
    public GameAction Charge { get; } = Action("charge", "Charge");
    public GameAction Inspect { get; } = Action("inspect", "Inspect");
    public GameAction PickUp { get; } = Action("pick_up", "Pick up");
    public GameAction Fire { get; } = Action("fire", "Fire");
    public GameAction Spin { get; } = Action("spin", "Spin");
    public GameAction Pause { get; } = Action("pause", "Pause");
    public GameAction Resume { get; } = Action("resume", "Resume");

    public BindingContext Gameplay { get; }
    public BindingContext Menu { get; }

    public GameAction[] AllActions => new[] { Move, Sprint, Jump, Dash, Charge, Inspect, PickUp, Fire, Spin, Pause, Resume };

    public DemoContent()
    {
        // Move: keys and the left stick; the action keeps diagonals from being faster.
        Move.Modifiers.Add(new NormalizeModifier { OnlyIfLonger = true });
        Move.Description = "WASD or the left stick.";

        Resume.TriggerWhenPaused = true;
        Charge.Description = "Hold E (or X) for 0.6 s. Watch the bar: it follows the action's elapsed time.";
        Inspect.Description = "Tap F.";
        PickUp.Description = "Hold F for 0.5 s. Same key as Inspect: tap and hold tell them apart.";
        Dash.Description = "Shift + Space (chord), or B.";
        Jump.Description = "Space (silenced while Dash fires), or A.";
        Fire.Description = "Hold the left mouse button or the right trigger: fires in pulses.";
        Spin.Description = "Combo: Jump, then Dash within 0.6 s.";

        Gameplay = BindingContext.Create("gameplay");
        Gameplay.Description = "On-foot controls.";
        Gameplay.Add(Move, InputSource.ForKeyAxis2D(Key.W, Key.S, Key.A, Key.D));
        Gameplay.Add(Move, InputSource.ForJoyStick(JoyStickSide.Left), new DeadzoneModifier { Shape = DeadzoneShape.Radial, Lower = 0.2f });

        Gameplay.Add(Sprint, InputSource.ForKey(Key.Shift)).Rebindable("sprint_key", "Sprint", "Keyboard");
        Gameplay.Add(Sprint, InputSource.ForJoyButton(JoyButton.LeftShoulder)).Rebindable("sprint_pad", "Sprint", "Gamepad");

        // Jump and Dash share the Space binding id, so rebinding it moves both.
        Gameplay.Add(Dash, InputSource.ForKey(Key.Space), new PressedTrigger(), new ChordTrigger { ChordAction = Sprint }).Rebindable("jump_key", "Jump / Dash", "Keyboard");
        Gameplay.Add(Dash, InputSource.ForJoyButton(JoyButton.B), new PressedTrigger()).Rebindable("dash_pad", "Dash", "Gamepad");

        Gameplay.Add(Jump, InputSource.ForKey(Key.Space), new PressedTrigger(), new ChordBlockerTrigger { BlockingAction = Dash }).Rebindable("jump_key", "Jump / Dash", "Keyboard");
        Gameplay.Add(Jump, InputSource.ForJoyButton(JoyButton.A), new PressedTrigger()).Rebindable("jump_pad", "Jump", "Gamepad");

        Gameplay.Add(Charge, InputSource.ForKey(Key.E), new HoldTrigger { HoldSeconds = 0.6f, OneShot = true }).Rebindable("charge_key", "Charge", "Keyboard");
        Gameplay.Add(Charge, InputSource.ForJoyButton(JoyButton.X), new HoldTrigger { HoldSeconds = 0.6f, OneShot = true }).Rebindable("charge_pad", "Charge", "Gamepad");

        Gameplay.Add(Inspect, InputSource.ForKey(Key.F), new TapTrigger { TapSeconds = 0.25f }).Rebindable("interact_key", "Inspect / Pick up", "Keyboard");
        Gameplay.Add(PickUp, InputSource.ForKey(Key.F), new HoldTrigger { HoldSeconds = 0.5f, OneShot = true }).Rebindable("interact_key", "Inspect / Pick up", "Keyboard");

        Gameplay.Add(Fire, InputSource.ForMouseButton(MouseButton.Left), new PulseTrigger { IntervalSeconds = 0.15f }).Rebindable("fire_mouse", "Fire", "Keyboard");
        Gameplay.Add(Fire, InputSource.ForJoyAxis(JoyAxis.TriggerRight), new PulseTrigger { IntervalSeconds = 0.15f, ActuationThreshold = 0.5f }).Rebindable("fire_pad", "Fire", "Gamepad");

        var spin = new ComboTrigger();
        spin.Steps.Add(ComboStep.Create(Jump));
        spin.Steps.Add(ComboStep.Create(Dash, ActionEvents.Triggered, 0.6f));
        Gameplay.Add(Spin, InputSource.Nothing(), spin);

        // Pause and Resume share an id across the two contexts.
        Gameplay.Add(Pause, InputSource.ForKey(Key.Escape), new PressedTrigger()).Rebindable("pause_key", "Pause", "Keyboard");
        Gameplay.Add(Pause, InputSource.ForJoyButton(JoyButton.Start), new PressedTrigger());

        Menu = BindingContext.Create("menu");
        Menu.Description = "Pause menu: blocks everything below it.";
        Menu.BlockLowerPriority = true;
        Menu.Add(Resume, InputSource.ForKey(Key.Escape), new PressedTrigger()).Rebindable("pause_key", "Pause", "Keyboard");
        Menu.Add(Resume, InputSource.ForJoyButton(JoyButton.Start), new PressedTrigger());
    }

    private static GameAction Action(string name, string display, ActionValueKind kind = ActionValueKind.Digital)
    {
        var action = GameAction.Create(name, kind);
        action.DisplayName = display;
        return action;
    }
}
