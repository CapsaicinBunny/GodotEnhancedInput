using FluentAssertions;
using Godot;

namespace GodotEnhancedInput.Tests.Players;

/// <summary>SRX-1 … SRX-6 (new sources) and DEV-1 … DEV-8 (devices, backends, XR, paths, rumble).</summary>
[Collection("Godot")]
public class DeviceTests
{
    // ------------------------------------------------------------------
    //  Sources
    // ------------------------------------------------------------------

    [Fact]
    public void MouseWheel_IsAPerTickScrollAxis_WithTrackpadPans()
    {
        var zoom = Make.Action("zoom", ActionValueKind.Axis2D);
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(zoom, InputSource.ForMouseWheel())));
        rig.Player.FeedEvent(new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown, Pressed = true, Factor = 1f });
        rig.Player.FeedEvent(new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown, Pressed = false, Factor = 1f });
        rig.Player.FeedEvent(new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown, Pressed = true, Factor = 2f });
        rig.Player.FeedEvent(new InputEventPanGesture { Delta = new Vector2(0.5f, 0f) });
        rig.Tick();
        rig.Player.GetValue(zoom).AsVector2().Should().Be(new Vector2(0.5f, 3f));
        rig.Tick();
        rig.Player.GetValue(zoom).AsVector2().Should().Be(Vector2.Zero);
        InputSource.ForMouseWheel().Nature.Should().Be(ValueNature.Delta);
    }

    [Fact]
    public void Pen_PressureTiltAndEraser()
    {
        var ink = Make.Action("ink", ActionValueKind.Axis1D);
        var tilt = Make.Action("tilt", ActionValueKind.Axis2D);
        var erase = Make.Action("erase");
        using var rig = new Rig().Add(Make.Context("C",
            Make.Bind(ink, InputSource.ForPen(PenChannel.Pressure)),
            Make.Bind(tilt, InputSource.ForPen(PenChannel.Tilt)),
            Make.Bind(erase, InputSource.ForPen(PenChannel.Eraser))));

        rig.Player.FeedEvent(new InputEventMouseMotion { Pressure = 0.6f, Tilt = new Vector2(0.2f, -0.1f) });
        rig.Tick();
        rig.Player.GetValue(ink).AsFloat().Should().BeApproximately(0.6f, 1e-6f);
        rig.Player.GetValue(tilt).AsVector2().Should().Be(new Vector2(0.2f, -0.1f));
        rig.Player.IsTriggered(erase).Should().BeFalse();

        rig.Player.FeedEvent(new InputEventMouseMotion { Pressure = 0.5f, PenInverted = true });
        rig.Tick();
        rig.Player.IsTriggered(erase).Should().BeTrue();

        rig.Player.FeedEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
        rig.Tick();
        rig.Player.GetValue(ink).AsFloat().Should().Be(0f);
    }

    [Fact]
    public void MotionSensors_ReadPerDevice()
    {
        var aim = Make.Action("aim", ActionValueKind.Axis3D);
        var tiltPhone = Make.Action("tilt_phone", ActionValueKind.Axis3D);
        using var rig = new Rig().Add(Make.Context("C",
            Make.Bind(aim, InputSource.ForMotion(MotionSensor.Gyroscope, joypad: -1)),
            Make.Bind(tiltPhone, InputSource.ForMotion(MotionSensor.Gravity))));
        rig.Player.PollMotionSensors = false;
        rig.Player.Raw.Set(SourceKey.ForMotion(MotionSensor.Gyroscope, 1), new Vector3(0.1f, 0.2f, 0f));
        rig.Player.Raw.Set(SourceKey.ForMotion(MotionSensor.Gravity, SourceKey.Handheld), new Vector3(0f, -9.8f, 0f));
        rig.Tick();
        rig.Player.GetValue(aim).AsVector3().Should().Be(new Vector3(0.1f, 0.2f, 0f));
        rig.Player.GetValue(tiltPhone).AsVector3().Should().Be(new Vector3(0f, -9.8f, 0f));
    }

    [Fact]
    public void RawPadIndices_ReachUnnamedButtonsAndAxes()
    {
        var shiftUp = Make.Action("shift_up");
        var clutch = Make.Action("clutch", ActionValueKind.Axis1D);
        using var rig = new Rig().Add(Make.Context("C",
            Make.Bind(shiftUp, InputSource.ForJoyButtonIndex(40)),
            Make.Bind(clutch, InputSource.ForJoyAxisIndex(7))));
        rig.Player.FeedEvent(new InputEventJoypadButton { ButtonIndex = (JoyButton)40, Pressed = true });
        rig.Player.FeedEvent(new InputEventJoypadMotion { Axis = (JoyAxis)7, AxisValue = 0.4f });
        rig.Tick();
        rig.Player.IsTriggered(shiftUp).Should().BeTrue();
        rig.Player.GetValue(clutch).AsFloat().Should().BeApproximately(0.4f, 1e-6f);
    }

    [Fact]
    public void Composite_ModifierKeysAxesAndAnyOf()
    {
        var save = Make.Action("save");
        var steer = Make.Action("steer", ActionValueKind.Axis1D);
        var fire = Make.Action("fire");
        using var rig = new Rig().Add(Make.Context("C",
            Make.Bind(save, InputSource.WithModifiers(Key.S, Key.Ctrl)),
            Make.Bind(steer, InputSource.Composite(CompositeMode.Axis1D, InputSource.ForJoyButton(JoyButton.DpadLeft), InputSource.ForJoyButton(JoyButton.DpadRight))),
            Make.Bind(fire, InputSource.Composite(CompositeMode.AnyOf, InputSource.ForMouseButton(MouseButton.Left), InputSource.ForKey(Key.F)))));

        rig.Press(Key.S).Tick();
        rig.Player.IsTriggered(save).Should().BeFalse("Ctrl is not held");
        rig.Press(Key.Ctrl).Tick();
        rig.Player.IsTriggered(save).Should().BeTrue();

        rig.Player.FeedEvent(new InputEventJoypadButton { ButtonIndex = JoyButton.DpadLeft, Pressed = true });
        rig.Press(Key.F).Tick();
        rig.Player.GetValue(steer).AsFloat().Should().Be(-1f);
        rig.Player.IsTriggered(fire).Should().BeTrue();

        var keys = new List<SourceKey>();
        InputSource.WithModifiers(Key.S, Key.Ctrl).CollectKeys(keys);
        keys.Should().HaveCount(2, "consumption and conflicts see every part");
    }

    [Fact]
    public void NewKinds_RoundTripThroughJson()
    {
        var sources = new[]
        {
            InputSource.ForMouseWheel(), InputSource.ForPen(PenChannel.Tilt), InputSource.ForMotion(MotionSensor.Accelerometer, 2),
            InputSource.ForJoyButtonIndex(40, 1), InputSource.ForJoyAxisIndex(8, range: JoyAxisRange.Negative),
            InputSource.ForDeviceControl("wheel", "steering", 0), InputSource.WithModifiers(Key.S, Key.Ctrl, Key.Shift),
        };
        foreach (var source in sources)
            InputSourceJson.SameInput(source, InputSourceJson.FromJson(InputSourceJson.ToJson(source), out _)).Should().BeTrue(source.Describe());
    }

    [Fact]
    public void TouchEmulatedMouse_IsIgnoredByDefault()
    {
        var click = Make.Action("click");
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(click, InputSource.ForMouseButton(MouseButton.Left))));
        rig.Player.FeedEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Device = (int)InputEvent.DeviceIdEmulation });
        rig.Tick();
        rig.Player.IsTriggered(click).Should().BeFalse();
        rig.Player.Raw.IgnoreTouchEmulatedMouse = false;
        rig.Player.FeedEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Device = (int)InputEvent.DeviceIdEmulation });
        rig.Tick();
        rig.Player.IsTriggered(click).Should().BeTrue();
    }

    // ------------------------------------------------------------------
    //  Devices
    // ------------------------------------------------------------------

    private sealed class FakeWheelBackend : InputBackend
    {
        public override string Name => "fake wheel";
        public readonly List<(InputDeviceInfo, float, float)> Rumbles = new();
        public float Steering;
        public InputDeviceInfo Device;

        public override void Attached() => Device = AddDevice("wheel", 0, "Fake Wheel", "fake-guid");
        public override void Poll(double delta) => SetControl("wheel", 0, "steering", Steering);
        public override void Rumble(InputDeviceInfo device, float weak, float strong, float seconds) => Rumbles.Add((device, weak, strong));
        public void Unplug() => RemoveDevice("wheel", 0);
    }

    [Fact]
    public void Registry_ListsBuiltinDevicesAndJoypads()
    {
        var system = new EnhancedInput { TickMode = TickMode.Manual };
        try
        {
            system.GetDevice("keyboard_mouse").Should().NotBeNull();
            system.GetDevice("touch").Kind.Should().Be(InputDeviceKind.Touch);
            InputDeviceInfo connected = null;
            system.DeviceConnected += d => connected = d;
            system.NotifyJoypadConnection(5, true, "abc");
            connected.Kind.Should().Be(InputDeviceKind.Joypad);
            system.GetJoypadDevice(5).Guid.Should().Be("abc");
            system.NotifyJoypadConnection(5, false);
            system.GetJoypadDevice(5).IsConnected.Should().BeFalse();
        }
        finally { system.Free(); }
    }

    [Fact]
    public void Roles_AreGuessedFromNames()
    {
        InputDeviceInfo.GuessJoypadRole("Logitech G29 Driving Force Racing Wheel").Should().Be(DeviceRole.Wheel);
        InputDeviceInfo.GuessJoypadRole("Thrustmaster T.16000M").Should().Be(DeviceRole.FlightStick);
        InputDeviceInfo.GuessJoypadRole("Saitek Pro Flight Rudder Pedals").Should().Be(DeviceRole.Pedals);
        InputDeviceInfo.GuessJoypadRole("Xbox Wireless Controller").Should().Be(DeviceRole.Gamepad);
    }

    [Fact]
    public void DeviceProfile_CalibratesAxesAndNamesControls()
    {
        var system = new EnhancedInput { TickMode = TickMode.Manual };
        try
        {
            var profile = new DeviceProfile { Guids = new[] { "pedals-guid" }, Role = DeviceRole.Pedals, DisplayName = "Pedals" };
            profile.AxisCalibration[1] = new Vector3(-1f, -1f, 1f);   // rests at -1, full at +1 → 0…1
            profile.ButtonNames[12] = "Paddle Shift Up";
            system.DeviceProfiles.Add(profile);
            system.NotifyJoypadConnection(3, true, "pedals-guid");
            system.GetJoypadDevice(3).Role.Should().Be(DeviceRole.Pedals);

            var gas = Make.Action("gas", ActionValueKind.Axis1D);
            var shift = Make.Action("shift_up");
            var player = system.GetPlayer(0);
            player.AddContext(Make.Context("C", Make.Bind(gas, InputSource.ForJoyAxisIndex(1)), Make.Bind(shift, InputSource.ForJoyButtonIndex(12))));
            system.FeedEvent(new InputEventJoypadMotion { Axis = (JoyAxis)1, AxisValue = 0f, Device = 3 });
            system.Tick(Rig.Dt);
            player.GetValue(gas).AsFloat().Should().BeApproximately(0.5f, 1e-5f);

            system.AssignJoypad(player, 3);
            player.GetPromptText(shift, InputDeviceKind.Joypad).Should().Be("Paddle Shift Up");
        }
        finally { system.Free(); }
    }

    [Fact]
    public void CustomBackend_DevicesRouteLikeJoypads()
    {
        var system = new EnhancedInput { TickMode = TickMode.Manual };
        var backend = new FakeWheelBackend();
        try
        {
            var steer = Make.Action("steer", ActionValueKind.Axis1D);
            var context = Make.Context("C", Make.Bind(steer, InputSource.ForDeviceControl("wheel", "steering")));
            var first = system.GetPlayer(0);
            var second = system.AddPlayer();
            first.AddContext(context);
            second.AddContext(context);
            system.AddBackend(backend);

            backend.Steering = 0.3f;
            system.Tick(Rig.Dt);
            first.GetValue(steer).AsFloat().Should().BeApproximately(0.3f, 1e-6f, "unowned devices go to player 0");
            second.GetValue(steer).AsFloat().Should().Be(0f);

            system.AssignDevice(second, backend.Device);
            backend.Steering = -0.5f;
            system.Tick(Rig.Dt);
            second.GetValue(steer).AsFloat().Should().BeApproximately(-0.5f, 1e-6f);
            system.GetDeviceOwner(backend.Device).Should().BeSameAs(second);

            second.Rumble(0.2f, 0.4f, 0.1f);
            backend.Rumbles.Should().ContainSingle();

            backend.Unplug();
            system.Tick(Rig.Dt);
            backend.Device.IsConnected.Should().BeFalse();
        }
        finally
        {
            system.RemoveBackend(backend);
            system.Free();
        }
    }

    [Fact]
    public void AssignDevice_KeyboardAndTouchGoToOnePlayer()
    {
        var system = new EnhancedInput { TickMode = TickMode.Manual };
        try
        {
            var second = system.AddPlayer();
            system.AssignDevice(second, system.GetDevice("keyboard_mouse"));
            second.AcceptKeyboardMouse.Should().BeTrue();
            system.GetPlayer(0).AcceptKeyboardMouse.Should().BeFalse();
            system.GetDeviceOwner(system.GetDevice("keyboard_mouse")).Should().BeSameAs(second);
            system.AssignDevice(second, system.GetDevice("touch"));
            system.GetPlayer(0).AcceptTouch.Should().BeFalse();
        }
        finally { system.Free(); }
    }

    [Fact]
    public void XRBackend_TurnsTrackerInputsAndPosesIntoControls()
    {
        var system = new EnhancedInput { TickMode = TickMode.Manual };
        var tracker = new XRControllerTracker { Name = "gei_test_left_hand", Type = XRServer.TrackerType.Controller };
        var backend = new XRInputBackend();
        try
        {
            var grab = Make.Action("grab", ActionValueKind.Axis1D);
            var hand = Make.Action("hand", ActionValueKind.Axis3D);
            system.GetPlayer(0).AddContext(Make.Context("C",
                Make.Bind(grab, InputSource.ForDeviceControl(XRInputBackend.DeviceType, "trigger")),
                Make.Bind(hand, InputSource.ForDeviceControl(XRInputBackend.DeviceType, "default/position"))));
            system.AddBackend(backend);
            XRServer.AddTracker(tracker);

#pragma warning disable CS0618 // Godot marks the base method as "use through XRControllerTracker", which this is
            tracker.SetInput("trigger", 0.75f);
#pragma warning restore CS0618
            tracker.SetPose("default", new Transform3D(Basis.Identity, new Vector3(0.1f, 1.2f, -0.3f)), Vector3.Zero, Vector3.Zero, XRPose.TrackingConfidenceEnum.High);
            system.Tick(Rig.Dt);

            var player = system.GetPlayer(0);
            player.GetValue(grab).AsFloat().Should().BeApproximately(0.75f, 1e-6f);
            player.GetValue(hand).AsVector3().Should().Be(new Vector3(0.1f, 1.2f, -0.3f));
            system.GetDevice($"xr:{backend.IndexOf("gei_test_left_hand")}").Role.Should().Be(DeviceRole.XRController);
        }
        finally
        {
            XRServer.RemoveTracker(tracker);
            system.RemoveBackend(backend);
            system.Free();
        }
    }

    [Theory]
    [InlineData("key:space")]
    [InlineData("keycode:a")]
    [InlineData("keys:w/s/a/d")]
    [InlineData("keys:a/d")]
    [InlineData("mouse:left")]
    [InlineData("mouse:wheel")]
    [InlineData("pad:a")]
    [InlineData("pad2:left_shoulder")]
    [InlineData("pad:button12")]
    [InlineData("pad:left_y-")]
    [InlineData("pad:axis7+")]
    [InlineData("pad:right_stick")]
    [InlineData("pad:gyroscope")]
    [InlineData("device:gravity")]
    [InlineData("touch1:drag")]
    [InlineData("pen:pressure")]
    [InlineData("inputmap:jump")]
    [InlineData("virtual:touch_move")]
    [InlineData("@wheel0:steering")]
    [InlineData("key:ctrl + key:s")]
    [InlineData("mouse:left | pad:trigger_right")]
    [InlineData("axis1d(pad:dpad_left, pad:dpad_right)")]
    [InlineData("none")]
    public void Paths_RoundTrip(string path)
    {
        var source = InputSourcePath.Parse(path);
        InputSourcePath.Format(source).Should().Be(path);
        InputSourceJson.SameInput(source, InputSourcePath.Parse(InputSourcePath.Format(source))).Should().BeTrue();
    }

    [Fact]
    public void Paths_AcceptAliasesAndRejectGarbage()
    {
        InputSourcePath.Parse("pad:south").JoyButton.Should().Be(JoyButton.A);
        InputSourcePath.Parse("PAD:Left_Shoulder").JoyButton.Should().Be(JoyButton.LeftShoulder);
        InputSourcePath.Parse("pad:rt").JoyAxis.Should().Be(JoyAxis.TriggerRight);
        InputSourcePath.TryParse("banana").Should().BeNull();
        InputSourcePath.TryParse("key:not_a_key").Should().BeNull();
    }

    private sealed class RecordingHaptics : IHapticsOutput
    {
        public readonly List<string> Log = new();
        public void StartJoypad(int device, float weak, float strong, float seconds) => Log.Add($"start {device} {weak:0.##} {strong:0.##}");
        public void StopJoypad(int device) => Log.Add($"stop {device}");
        public void SetJoypadLight(int device, Color color) => Log.Add($"light {device}");
        public void VibrateHandheld(int milliseconds, float amplitude) => Log.Add($"handheld {milliseconds}");
    }

    [Fact]
    public void Rumble_GoesToThePlayersPads_ScaledBySetting()
    {
        using var rig = new Rig();
        var haptics = new RecordingHaptics();
        rig.Player.HapticsOutput = haptics;
        rig.Player.JoypadDevices.Add(2);
        rig.Player.JoypadDevices.Add(4);

        rig.Player.Rumble(1f, 0.5f, 0.2f);
        rig.Player.Profile.SetSetting("vibration_strength", 0.5);
        rig.Player.Rumble(1f, 0.5f, 0.2f, device: 4);
        rig.Player.Profile.SetSetting("vibration_strength", 0);
        rig.Player.Rumble(1f, 1f, 0.2f);
        rig.Player.StopRumble();
        rig.Player.SetJoypadLight(Colors.Red, 2);
        rig.Player.VibrateHandheld(100);

        haptics.Log.Should().Equal("start 2 1 0.5", "start 4 1 0.5", "start 4 0.5 0.25", "stop 2", "stop 4", "light 2");
    }
}
