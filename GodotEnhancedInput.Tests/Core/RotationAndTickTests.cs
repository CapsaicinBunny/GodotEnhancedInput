using FluentAssertions;
using Godot;

namespace GodotEnhancedInput.Tests.Core;

/// <summary>VAL-1 … VAL-4, TCK-1, TCK-2, FFB-1: four-component and rotation values, rotation modifiers, Auto ticking, stuck input, force feedback.</summary>
[Collection("Godot")]
public class RotationAndTickTests
{
    private static bool Same(Quaternion a, Quaternion b, float tolerance = 1e-4f) => Mathf.Abs(a.Dot(b)) > 1f - tolerance;

    [Fact]
    public void Axis4D_KeepsAllComponents_LowerKindsDropThem()
    {
        var four = Make.Action("four", ActionValueKind.Axis4D);
        var three = Make.Action("three", ActionValueKind.Axis3D);
        using var rig = new Rig().Add(Make.Context("C",
            Make.Bind(four, InputSource.ForVirtual("v")), Make.Bind(three, InputSource.ForVirtual("v"))));
        rig.Player.SetVirtualControl("v", new Vector4(0.1f, 0.2f, 0.3f, 0.4f));
        rig.Tick();
        rig.Player.GetValue(four).AsVector4().Should().Be(new Vector4(0.1f, 0.2f, 0.3f, 0.4f));
        rig.Player.GetValue(three).AsVector4().Should().Be(new Vector4(0.1f, 0.2f, 0.3f, 0f));
    }

    [Fact]
    public void Quaternion_IsNormalized_IdentityIsNoInput_MagnitudeIsTheAngle()
    {
        var aim = Make.Action("aim", ActionValueKind.Quaternion);
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(aim, InputSource.ForVirtual("q"))));

        rig.Player.SetVirtualControl("q", Quaternion.Identity);
        rig.Tick();
        rig.Player.IsTriggered(aim).Should().BeFalse("no rotation is no input");
        rig.Player.GetValue(aim).AsQuaternion().Should().Be(Quaternion.Identity);

        var quarter = new Quaternion(Vector3.Up, Mathf.Pi / 2f);
        rig.Player.SetVirtualControl("q", ActionValue.FromQuaternion(quarter) * 3f);   // not unit length
        rig.Tick();
        rig.Player.IsTriggered(aim).Should().BeTrue();
        Same(rig.Player.GetValue(aim).AsQuaternion(), quarter).Should().BeTrue();
        rig.Player.GetValue(aim).Magnitude.Should().BeApproximately(0.5f, 1e-4f, "a quarter turn is half of the largest rotation");
        rig.Player.GetValue(aim).AsEuler().Y.Should().BeApproximately(Mathf.Pi / 2f, 1e-4f);
    }

    [Fact]
    public void ScaleRotation_ScalesTheAngle_TheShortWayRound()
    {
        var q = new Quaternion(Vector3.Right, 0.8f);
        Same(ActionValue.ToQuaternion(ActionValue.ScaleRotation(ActionValue.FromQuaternion(q), 0.5f)), new Quaternion(Vector3.Right, 0.4f)).Should().BeTrue();
        var negated = ActionValue.FromQuaternion(q) * -1f;   // same rotation stored the long way
        Same(ActionValue.ToQuaternion(ActionValue.ScaleRotation(negated, 0.5f)), new Quaternion(Vector3.Right, 0.4f)).Should().BeTrue();
        ActionValue.ScaleRotation(Vector4.Zero, 2f).Should().Be(Vector4.Zero);
    }

    [Fact]
    public void RotationConvert_AllDirections()
    {
        static Vector4 Run(RotationConversion c, Vector4 v) => new RotationConvertModifier { Conversion = c }.Apply(v, default);

        var degrees = new Vector4(30f, 45f, -10f, 0f);
        var radians = Run(RotationConversion.DegreesToRadians, degrees);
        radians.X.Should().BeApproximately(Mathf.DegToRad(30f), 1e-6f);

        var q = Run(RotationConversion.EulerToQuaternion, radians);
        Same(ActionValue.ToQuaternion(q), Quaternion.FromEuler(new Vector3(radians.X, radians.Y, radians.Z))).Should().BeTrue();
        var back = Run(RotationConversion.QuaternionToEuler, q);
        new Vector3(back.X, back.Y, back.Z).DistanceTo(new Vector3(radians.X, radians.Y, radians.Z)).Should().BeLessThan(1e-4f);

        var rotationVector = new Vector4(0f, 0f, 1.2f, 0f);   // 1.2 rad about Z
        var fromVector = Run(RotationConversion.RotationVectorToQuaternion, rotationVector);
        Same(ActionValue.ToQuaternion(fromVector), new Quaternion(Vector3.Back, 1.2f)).Should().BeTrue();
        var toVector = Run(RotationConversion.QuaternionToRotationVector, fromVector);
        toVector.Z.Should().BeApproximately(1.2f, 1e-4f);

        Run(RotationConversion.EulerToQuaternion, Vector4.Zero).Should().Be(Vector4.Zero, "no input stays no input");
        Run(RotationConversion.RotationVectorToQuaternion, Vector4.Zero).Should().Be(Vector4.Zero);
    }

    [Fact]
    public void RotationOffset_TurnsByAFixedOrPlayerSetAngle()
    {
        var grip = Make.Action("grip", ActionValueKind.Quaternion);
        var offset = new RotationOffsetModifier { OffsetDegrees = new Vector3(0f, 90f, 0f) };
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(grip, InputSource.ForVirtual("q"), offset)));
        var tilt = new Quaternion(Vector3.Right, 0.3f);
        rig.Player.SetVirtualControl("q", tilt);
        rig.Tick();
        var turn = Quaternion.FromEuler(new Vector3(0f, Mathf.Pi / 2f, 0f));
        Same(rig.Player.GetValue(grip).AsQuaternion(), tilt * turn).Should().BeTrue("local: after the rotation");

        offset.Local = false;
        rig.Tick();
        Same(rig.Player.GetValue(grip).AsQuaternion(), turn * tilt).Should().BeTrue("global: before it");

        offset.SettingPrefix = "grip_offset";
        rig.Player.Profile.SetSetting("grip_offset_y", 0.0);
        rig.Tick();
        Same(rig.Player.GetValue(grip).AsQuaternion(), tilt).Should().BeTrue("the player's setting replaces the fixed offset");
    }

    [Fact]
    public void RotationSmoothing_FollowsAlongTheArc_AtAnyFrameRate()
    {
        var look = Make.Action("look", ActionValueKind.Quaternion);
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(look, InputSource.ForVirtual("q"), new RotationSmoothingModifier { Rate = 10f })));
        var start = new Quaternion(Vector3.Up, 0.2f);
        var target = new Quaternion(Vector3.Up, 1.2f);
        rig.Player.SetVirtualControl("q", start);
        rig.Tick();
        Same(rig.Player.GetValue(look).AsQuaternion(), start).Should().BeTrue("the first value passes straight through");

        rig.Player.SetVirtualControl("q", target);
        rig.Tick();
        var step = rig.Player.GetValue(look).AsQuaternion();
        step.IsNormalized().Should().BeTrue();
        step.GetAxis().Normalized().Dot(Vector3.Up).Should().BeGreaterThan(0.999f, "it stays on the arc between the two");
        float angle = step.GetAngle();
        angle.Should().BeInRange(0.2f, 1.2f);

        // Same elapsed time in one big frame or many small ones ends in the same place.
        float expected = 0.2f + (1.2f - 0.2f) * (1f - Mathf.Exp(-10f * 0.25f));
        using var coarse = new Rig().Add(Make.Context("C", Make.Bind(look, InputSource.ForVirtual("q"), new RotationSmoothingModifier { Rate = 10f })));
        coarse.Player.SetVirtualControl("q", start);
        coarse.Tick();
        coarse.Player.SetVirtualControl("q", target);
        coarse.Tick(1, 0.25);
        coarse.Player.GetValue(look).AsQuaternion().GetAngle().Should().BeApproximately(expected, 1e-3f);
        using var fine = new Rig().Add(Make.Context("C", Make.Bind(look, InputSource.ForVirtual("q"), new RotationSmoothingModifier { Rate = 10f })));
        fine.Player.SetVirtualControl("q", start);
        fine.Tick();
        fine.Player.SetVirtualControl("q", target);
        fine.Tick(25, 0.01);
        fine.Player.GetValue(look).AsQuaternion().GetAngle().Should().BeApproximately(expected, 1e-3f);
    }

    [Fact]
    public void AutoTick_TicksOncePerFrame_WhicheverLoopComesFirst()
    {
        var system = new EnhancedInput();
        try
        {
            system.TickMode.Should().Be(TickMode.Auto, "the default");
            var player = system.GetPlayer(0);
            ulong before = player.TickCount;
            system._PhysicsProcess(Rig.Dt);   // frame 1: two physics steps, then process
            system._PhysicsProcess(Rig.Dt);
            system._Process(Rig.Dt);
            player.TickCount.Should().Be(before + 1);
            system._Process(Rig.Dt);          // frame 2: no physics step
            player.TickCount.Should().Be(before + 2);
        }
        finally { system.Free(); }
    }

    [Fact]
    public void StuckInput_IsReleased_WhenGodotNoLongerHoldsIt()
    {
        var run = Make.Action("run");
        var scroll = Make.Action("scroll");
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(run, Key.Shift), Make.Bind(scroll, InputSource.ForMouseButton(MouseButton.WheelUp))));
        rig.Press(Key.Shift).Tick();
        rig.Player.IsTriggered(run).Should().BeTrue();

        // The release went to another window (nothing holds Shift in the headless engine).
        rig.Player.Raw.ReleaseStuck().Should().Be(1);
        rig.Tick();
        rig.Player.IsTriggered(run).Should().BeFalse();
        rig.Player.WasCompleted(run).Should().BeTrue("it was released, only late");
        rig.Player.Raw.ReleaseStuck().Should().Be(0, "nothing else is held, and nothing is ever pressed");
    }

    private sealed class WheelOutput : IHapticsOutput, IForceFeedbackOutput
    {
        public readonly List<(int Device, ForceFeedback Force)> Forces = new();
        public void StartJoypad(int device, float weak, float strong, float seconds) { }
        public void StopJoypad(int device) { }
        public void SetJoypadLight(int device, Color color) { }
        public void VibrateHandheld(int milliseconds, float amplitude) { }
        public void StartTriggers(int device, float left, float right, float seconds) { }
        public void SetForceFeedback(int device, ForceFeedback force) => Forces.Add((device, force));
    }

    [Fact]
    public void ForceFeedback_GoesToThePlayersWheels_ScaledByTheSetting()
    {
        using var rig = new Rig();
        var output = new WheelOutput();
        rig.Player.HapticsOutput = output;
        rig.Player.JoypadDevices.Add(3);
        rig.Player.JoypadDevices.Add(5);
        rig.Player.Profile.SetSetting("force_feedback_strength", 0.5);

        rig.Player.SetForceFeedback(new ForceFeedback { Spring = 0.8f, Constant = -0.4f, Center = 0.2f });
        output.Forces.Select(f => f.Device).Should().Equal(3, 5);
        output.Forces[0].Force.Spring.Should().BeApproximately(0.4f, 1e-6f);
        output.Forces[0].Force.Constant.Should().BeApproximately(-0.2f, 1e-6f);
        output.Forces[0].Force.Center.Should().Be(0.2f, "where the wheel centres is not a strength");

        output.Forces.Clear();
        rig.Player.SetForceFeedback(ForceFeedback.Centering(), device: 5);
        output.Forces.Select(f => f.Device).Should().Equal(5);

        output.Forces.Clear();
        rig.Player.Profile.SetSetting("force_feedback_strength", 0.0);
        rig.Player.SetForceFeedback(ForceFeedback.Centering());
        output.Forces.Should().OnlyContain(f => f.Force.Spring == 0f && f.Force.Damper == 0f, "0 turns it off");
    }
}
