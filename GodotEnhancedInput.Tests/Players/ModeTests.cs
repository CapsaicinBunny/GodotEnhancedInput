using FluentAssertions;
using Godot;

namespace GodotEnhancedInput.Tests.Players;

/// <summary>MODE-1 … MODE-5 (modes and binding groups) and ACC-1 … ACC-5 (settings and accessibility).</summary>
[Collection("Godot")]
public class ModeTests
{
    private static readonly InputMode KeyboardMode = InputMode.Create("Keyboard & mouse", DeviceKinds.KeyboardMouse, new[] { "kbm" }, DeviceKinds.KeyboardMouse);
    private static readonly InputMode PadMode = InputMode.Create("Gamepad", DeviceKinds.Joypad, new[] { "pad" }, DeviceKinds.Joypad);

    private static (Rig rig, EnhancedInputAction jump) Setup()
    {
        var jump = Make.Action("jump");
        var context = Make.Context("C");
        context.Add(jump, InputSource.ForKey(Key.Space)).InGroups("kbm");
        context.Add(jump, InputSource.ForJoyButton(JoyButton.A)).InGroups("pad");
        context.Add(jump, InputSource.ForVirtual("touch_jump"));   // no groups: always on
        var rig = new Rig().Add(context);
        rig.Player.Modes.Add(KeyboardMode);
        rig.Player.Modes.Add(PadMode);
        return (rig, jump);
    }

    [Fact]
    public void Groups_LimitBindingsToTheActiveMode()
    {
        var (rig, jump) = Setup();
        using (rig)
        {
            rig.Player.ActiveMode = PadMode;
            rig.Player.FeedEvent(Rig.KeyEvent(Key.Space, true));
            rig.Player.TickCount.Should().Be(0);
            rig.Player.LockMode(PadMode);   // keep the pad mode although a key was used
            rig.Tick();
            rig.Player.IsTriggered(jump).Should().BeFalse("keyboard bindings are off in the gamepad mode");

            rig.Player.SetVirtualControl("touch_jump", 1f);
            rig.Tick();
            rig.Player.IsTriggered(jump).Should().BeTrue("bindings without groups are always on");
        }
    }

    [Fact]
    public void NoActiveMode_EvaluatesEverything()
    {
        var (rig, jump) = Setup();
        using (rig)
        {
            rig.Press(Key.Space).Tick();
            rig.Player.IsTriggered(jump).Should().BeTrue();
        }
    }

    [Fact]
    public void Modes_SwitchWithTheDeviceUsed_UnlessLocked()
    {
        var (rig, jump) = Setup();
        using (rig)
        {
            InputMode changed = null;
            rig.Player.ModeChanged += m => changed = m;
            rig.Player.JoypadDevices.Add(0);

            rig.Player.FeedEvent(new InputEventJoypadButton { ButtonIndex = JoyButton.A, Pressed = true, Device = 0 });
            rig.Player.ActiveMode.Should().BeSameAs(PadMode);
            changed.Should().BeSameAs(PadMode);
            rig.Tick();
            rig.Player.IsTriggered(jump).Should().BeTrue();

            rig.Press(Key.Q);
            rig.Player.ActiveMode.Should().BeSameAs(KeyboardMode);

            rig.Player.LockMode(PadMode);
            rig.Press(Key.E);
            rig.Player.ActiveMode.Should().BeSameAs(PadMode, "locked");
            rig.Player.UnlockMode();
            rig.Press(Key.R);
            rig.Player.ActiveMode.Should().BeSameAs(KeyboardMode);
        }
    }

    [Fact]
    public void Modes_NeedTheirRequiredDevices()
    {
        using var rig = new Rig();
        rig.Player.AcceptKeyboardMouse = false;
        rig.Player.RequiredDevicesAvailable(KeyboardMode).Should().BeFalse();
        rig.Player.AcceptKeyboardMouse = true;
        rig.Player.RequiredDevicesAvailable(KeyboardMode).Should().BeTrue();
    }

    [Fact]
    public void Prompts_FollowTheActiveMode()
    {
        var (rig, jump) = Setup();
        using (rig)
        {
            rig.Player.LockMode(KeyboardMode);
            rig.Player.GetPrompts(jump).Select(p => p.Text).Should().NotContain("South");
        }
    }

    [Fact]
    public void Settings_ProfileThenModeDefaultThenFallback()
    {
        using var rig = new Rig();
        var mode = InputMode.Create("Pad", DeviceKinds.None, null);
        mode.SettingDefaults["look_sensitivity"] = 2.5;
        rig.Player.GetSetting("look_sensitivity", 1).Should().Be(1);
        rig.Player.ActiveMode = mode;
        rig.Player.GetSetting("look_sensitivity", 1).Should().Be(2.5);
        rig.Player.Profile.SetSetting("look_sensitivity", 3);
        rig.Player.GetSetting("look_sensitivity", 1).Should().Be(3);
    }

    [Fact]
    public void HoldToToggle_TurnsHoldsIntoPresses()
    {
        var aim = Make.Action("aim");
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(aim, Key.Q)));
        rig.Player.SetHoldToToggle(aim, true);

        rig.Press(Key.Q).Tick().Release(Key.Q).Tick();
        rig.Player.IsTriggered(aim).Should().BeTrue("toggled on, held without holding");
        rig.Tick(5);
        rig.Player.IsTriggered(aim).Should().BeTrue();
        rig.Press(Key.Q).Tick().Release(Key.Q).Tick();
        rig.Player.IsTriggered(aim).Should().BeFalse("toggled off");
    }

    [Fact]
    public void TimingScales_StretchTapsAndShortenHolds()
    {
        var tap = new TapTrigger { TapSeconds = 0.2f };
        var hold = new HoldTrigger { HoldSeconds = 0.4f };
        using var rig = new Rig();
        rig.Player.Profile.Apply(AccessibilityPresets.RelaxedTiming());
        var tapMemory = new TriggerMemory();
        var holdMemory = new TriggerMemory();
        TriggerPhase Tap(float v) => tap.Evaluate(new TriggerContext(new Vector3(v, 0, 0), ActionValueKind.Digital, 0.1, rig.Player, tapMemory));
        TriggerPhase Hold(float v) => hold.Evaluate(new TriggerContext(new Vector3(v, 0, 0), ActionValueKind.Digital, 0.1, rig.Player, holdMemory));

        new[] { Tap(1), Tap(1), Tap(1), Tap(0) }.Should().Equal(TriggerPhase.Ongoing, TriggerPhase.Ongoing, TriggerPhase.Ongoing, TriggerPhase.Triggered);   // a 0.3 s press is a tap with doubled windows
        new[] { Hold(1), Hold(1) }.Should().Equal(TriggerPhase.Ongoing, TriggerPhase.Triggered);   // a 0.4 s hold takes 0.2 s
    }

    [Fact]
    public void DeadzoneSetting_LetsPlayersTuneIt()
    {
        using var rig = new Rig();
        var dz = new DeadzoneModifier { Shape = DeadzoneShape.Axial, Lower = 0.2f, LowerSetting = "stick_deadzone" };
        Vector3 Run(float v) => dz.Run(new Vector3(v, 0, 0), new ModifierContext(ActionValueKind.Axis1D, 0.1, rig.Player, new ModifierMemory()));
        Run(0.3f).X.Should().BeGreaterThan(0f);
        rig.Player.Profile.SetSetting("stick_deadzone", 0.35);
        Run(0.3f).X.Should().Be(0f);
    }

    [Fact]
    public void Presets_ApplyAsOneChange()
    {
        var aim = Make.Action("aim");
        using var rig = new Rig();
        int changes = 0;
        rig.Player.BindingsChanged += () => changes++;
        rig.Player.Profile.Apply(AccessibilityPresets.HoldToToggle(aim));
        rig.Player.Profile.Apply(AccessibilityPresets.NoVibration());
        changes.Should().Be(2);
        rig.Player.IsHoldToToggle(aim).Should().BeTrue();
        rig.Player.GetSetting("vibration_strength", 1).Should().Be(0);
    }
}
