using FluentAssertions;
using Godot;

namespace GodotEnhancedInput.Tests.Rebinding;

/// <summary>RBX-1 … RBX-9: parts, filters, held inputs, timeout, chords, one change, named profiles, presets, safe writes.</summary>
[Collection("Godot")]
public class MoreRebindTests
{
    private static (Rig rig, EnhancedInputAction move, EnhancedInputAction jump, EnhancedInputAction fire) Setup()
    {
        var move = Make.Action("move", ActionValueKind.Axis2D);
        var jump = Make.Action("jump");
        var fire = Make.Action("fire");
        var context = Make.Context("gameplay");
        context.Add(move, InputSource.ForKeyAxis2D(Key.W, Key.S, Key.A, Key.D)).Rebindable("move_keys");
        context.Add(jump, InputSource.ForKey(Key.Space)).Rebindable("jump_key");
        context.Add(fire, InputSource.ForKey(Key.F)).Rebindable("fire_key");
        return (new Rig().Add(context), move, jump, fire);
    }

    [Fact]
    public void Parts_OfAKeyComposite_RebindOneByOne()
    {
        var (rig, move, _, _) = Setup();
        using (rig)
        {
            rig.Player.GetRebindSlot("move_keys").CurrentSource.GetParts().Should().HaveCount(4);
            rig.Player.RebindPart("move_keys", 0, InputSource.ForKey(Key.Up)).Applied.Should().BeTrue();
            var current = rig.Player.GetRebindSlot("move_keys").CurrentSource;
            current.Kind.Should().Be(SourceKind.KeyAxis2D);
            current.UpKey.Should().Be(Key.Up);
            current.NegativeKey.Should().Be(Key.A, "the other parts are kept");

            rig.Press(Key.Up).Tick();
            rig.Player.GetValue(move).AsVector2().Should().Be(new Vector2(0, -1));
        }
    }

    [Fact]
    public void Parts_WithANonKeySource_BecomeACompositeOfSources()
    {
        var (rig, move, _, _) = Setup();
        using (rig)
        {
            rig.Player.RebindPart("move_keys", 3, InputSource.ForJoyButton(JoyButton.DpadRight));
            var current = rig.Player.GetRebindSlot("move_keys").CurrentSource;
            current.Kind.Should().Be(SourceKind.Composite);
            current.CompositeMode.Should().Be(CompositeMode.Axis2D);

            rig.Player.FeedEvent(new InputEventJoypadButton { ButtonIndex = JoyButton.DpadRight, Pressed = true });
            rig.Press(Key.W).Tick();
            rig.Player.GetValue(move).AsVector2().Should().Be(new Vector2(1, -1));
            rig.Player.RebindPart("move_keys", 9, InputSource.ForKey(Key.X)).Applied.Should().BeFalse();
        }
    }

    [Fact]
    public void Capture_OfOnePart()
    {
        var (rig, _, _, _) = Setup();
        using (rig)
        {
            rig.Player.StartCapture("move_keys", part: 2).Should().BeTrue();
            rig.Player.CapturePart.Should().Be(2);
            rig.Player.FeedEvent(Rig.KeyEvent(Key.Left, true));
            rig.Player.GetRebindSlot("move_keys").CurrentSource.NegativeKey.Should().Be(Key.Left);
        }
    }

    [Fact]
    public void Capture_IgnoresInputsHeldWhenItStarted()
    {
        var (rig, _, _, _) = Setup();
        using (rig)
        {
            rig.Press(Key.Enter).Tick();   // the key that opened the rebinding menu
            rig.Player.StartCapture("jump_key");

            rig.Player.FeedEvent(Rig.KeyEvent(Key.Enter, true, echo: false)).Should().BeFalse("still the same held key");
            rig.Player.FeedEvent(Rig.KeyEvent(Key.Enter, false)).Should().BeFalse("its release reaches the game");
            rig.Player.IsCapturing.Should().BeTrue();
            rig.Player.FeedEvent(Rig.KeyEvent(Key.Enter, true)).Should().BeTrue("pressed again, it binds");
            rig.Player.GetRebindSlot("jump_key").CurrentSource.Key.Should().Be(Key.Enter);
        }
    }

    [Fact]
    public void Capture_WaitsForAStickToCentre()
    {
        var (rig, _, _, _) = Setup();
        using (rig)
        {
            rig.Player.FeedEvent(new InputEventJoypadMotion { Axis = JoyAxis.LeftX, AxisValue = 0.9f });
            rig.Tick();
            rig.Player.StartCapture("jump_key");
            rig.Player.FeedEvent(new InputEventJoypadMotion { Axis = JoyAxis.LeftX, AxisValue = 0.95f }).Should().BeFalse();
            rig.Player.FeedEvent(new InputEventJoypadMotion { Axis = JoyAxis.LeftX, AxisValue = 0.1f });
            rig.Player.FeedEvent(new InputEventJoypadMotion { Axis = JoyAxis.LeftX, AxisValue = -0.8f }).Should().BeTrue();
            rig.Player.GetRebindSlot("jump_key").CurrentSource.AxisRange.Should().Be(JoyAxisRange.Negative);
        }
    }

    [Fact]
    public void Capture_ExclusionsAndFilters()
    {
        var (rig, _, _, _) = Setup();
        using (rig)
        {
            rig.Player.StartCapture("jump_key", new CaptureOptions
            {
                ExcludedKeys = new[] { Key.Enter },
                Filter = s => s.Kind != SourceKind.MouseButton,
            });
            rig.Player.FeedEvent(Rig.KeyEvent(Key.Enter, true)).Should().BeTrue("swallowed, not bound");
            rig.Player.FeedEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true }).Should().BeTrue();
            rig.Player.IsCapturing.Should().BeTrue();
            rig.Player.FeedEvent(Rig.KeyEvent(Key.K, true));
            rig.Player.GetRebindSlot("jump_key").CurrentSource.Key.Should().Be(Key.K);
        }
    }

    [Fact]
    public void Capture_TimesOut()
    {
        var (rig, _, _, _) = Setup();
        using (rig)
        {
            string canceled = null;
            rig.Player.RebindCaptureCanceled += id => canceled = id;
            rig.Player.StartCapture("jump_key", new CaptureOptions { TimeoutSeconds = 0.1 });
            rig.Tick(3);
            rig.Player.IsCapturing.Should().BeTrue();
            rig.Tick(4);
            rig.Player.IsCapturing.Should().BeFalse();
            rig.Player.LastCaptureTimedOut.Should().BeTrue();
            canceled.Should().Be("jump_key");
        }
    }

    [Fact]
    public void Capture_Chords()
    {
        var (rig, _, jump, _) = Setup();
        using (rig)
        {
            rig.Player.StartCapture("jump_key", new CaptureOptions { AllowChords = true });
            rig.Player.FeedEvent(Rig.KeyEvent(Key.Ctrl, true)).Should().BeTrue();
            rig.Player.FeedEvent(Rig.KeyEvent(Key.J, true)).Should().BeTrue();
            var chord = rig.Player.GetRebindSlot("jump_key").CurrentSource;
            InputSourcePath.Format(chord).Should().Be("key:ctrl + key:j");

            rig.Release(Key.J).Release(Key.Ctrl).Tick();
            rig.Press(Key.Ctrl).Press(Key.J).Tick();
            rig.Player.IsTriggered(jump).Should().BeTrue();

            rig.Player.StartCapture("fire_key", new CaptureOptions { AllowChords = true, WaitForRelease = false });
            rig.Player.FeedEvent(Rig.KeyEvent(Key.Shift, true));
            rig.Player.FeedEvent(Rig.KeyEvent(Key.Shift, false));
            rig.Player.GetRebindSlot("fire_key").CurrentSource.Key.Should().Be(Key.Shift, "a modifier released alone binds itself");
        }
    }

    [Fact]
    public void Swap_ReachesListenersAsOneChange()
    {
        var (rig, _, _, _) = Setup();
        using (rig)
        {
            int changes = 0;
            rig.Player.BindingsChanged += () => changes++;
            rig.Player.Rebind("jump_key", InputSource.ForKey(Key.F), RebindConflictPolicy.Swap).ChangedSlots.Should().Equal("fire_key");
            changes.Should().Be(1);
        }
    }

    [Fact]
    public void NamedProfiles_SaveListLoadDelete()
    {
        var (rig, _, _, _) = Setup();
        using (rig)
        {
            rig.Player.ProfileId = "gei_test_named";
            foreach (var old in rig.Player.ListProfiles()) rig.Player.DeleteProfile(old);

            rig.Player.Rebind("jump_key", InputSource.ForKey(Key.J));
            rig.Player.SaveProfileAs("left-handed").Should().Be(Error.Ok);
            rig.Player.ResetAllRebinds();
            rig.Player.SaveProfileAs("default").Should().Be(Error.Ok);
            rig.Player.ListProfiles().Should().Equal("default", "left-handed");

            rig.Player.LoadProfileNamed("left-handed").Should().BeTrue();
            rig.Player.GetRebindSlot("jump_key").CurrentSource.Key.Should().Be(Key.J);
            rig.Player.SaveProfileAs("../escape").Should().Be(Error.InvalidParameter);

            rig.Player.DeleteProfile("left-handed").Should().BeTrue();
            rig.Player.DeleteProfile("default").Should().BeTrue();
            rig.Player.ListProfiles().Should().BeEmpty();
        }
    }

    [Fact]
    public void Presets_ImportAndExport_WithSafeWrites()
    {
        var (rig, _, _, _) = Setup();
        using (rig)
        {
            const string path = "user://gei_tests/preset.json";
            rig.Player.Rebind("fire_key", InputSource.ForMouseButton(MouseButton.Left));
            rig.Player.Profile.SetSetting("mouse_sensitivity", 0.7);
            rig.Player.ExportProfile(path).Should().Be(Error.Ok);
            rig.Player.ExportProfile(path).Should().Be(Error.Ok, "overwriting works");
            Godot.FileAccess.FileExists(path + ".tmp").Should().BeFalse();

            using var other = new Rig().Add(rig.Player.GetContexts()[0]);
            other.Player.ImportPreset(path).Should().BeTrue();
            other.Player.GetRebindSlot("fire_key").CurrentSource.MouseButton.Should().Be(MouseButton.Left);
            other.Player.GetSetting("mouse_sensitivity").Should().Be(0.7);
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));
        }
    }
}
