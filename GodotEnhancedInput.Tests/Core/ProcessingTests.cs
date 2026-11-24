using FluentAssertions;
using Godot;
using Xunit;

namespace GodotEnhancedInput.Tests.Core;

/// <summary>Adaptive deadzone, spring-damper, opposing inputs (SOCD) and sensor fusion.</summary>
[Collection("Godot")]
public class ProcessingTests
{
    private const double Dt = 1.0 / 60.0;

    private static Vector3 Run(InputModifier modifier, Vector3 value, ModifierMemory memory, double delta = Dt)
        => modifier.Run(value, new ModifierContext(ActionValueKind.Axis2D, delta, null, memory));

    [Fact]
    public void AdaptiveDeadzone_LearnsADriftingRestPoint()
    {
        var dz = new AdaptiveDeadzoneModifier { MinDeadzone = 0.05f, LearnRate = 2f };
        var memory = new ModifierMemory();
        var drift = new Vector3(0.12f, -0.04f, 0f);
        Run(dz, drift, memory).Should().NotBe(Vector3.Zero, "at first the drift is outside the minimum deadzone");
        for (int i = 0; i < 300; i++) Run(dz, drift + new Vector3(i % 2 == 0 ? 0.01f : -0.01f, 0, 0), memory);
        Run(dz, drift, memory).Should().Be(Vector3.Zero, "the rest point was learned");
        AdaptiveDeadzoneModifier.LearnedCenter(memory).X.Should().BeApproximately(0.12f, 0.02f);
        Run(dz, new Vector3(1f, 0f, 0f), memory).X.Should().BeGreaterThan(0.8f, "a real push still gets through");
    }

    [Fact]
    public void AdaptiveDeadzone_DoesNotLearnAStickThatIsMoving_OrFrozen_AndCapsTheCentre()
    {
        var dz = new AdaptiveDeadzoneModifier { LearnRate = 4f, RestSeconds = 0.3f };
        var memory = new ModifierMemory();
        for (int i = 0; i < 300; i++) Run(dz, new Vector3(0.15f + (i % 2 == 0 ? 0.05f : -0.05f), 0, 0), memory);   // creeping on purpose
        AdaptiveDeadzoneModifier.LearnedCenter(memory).Length().Should().BeLessThan(0.01f, "a moving stick is not resting");

        var frozen = new AdaptiveDeadzoneModifier { LearnRate = 4f, FreezeLearning = true };
        var fm = new ModifierMemory();
        for (int i = 0; i < 300; i++) Run(frozen, new Vector3(0.12f, 0, 0), fm);
        AdaptiveDeadzoneModifier.LearnedCenter(fm).Should().Be(Vector2.Zero);

        var capped = new AdaptiveDeadzoneModifier { LearnRate = 4f, LearnRadius = 0.5f, MaxCenterOffset = 0.1f };
        var cm = new ModifierMemory();
        for (int i = 0; i < 600; i++) Run(capped, new Vector3(0.3f, 0, 0), cm);
        AdaptiveDeadzoneModifier.LearnedCenter(cm).Length().Should().BeApproximately(0.1f, 1e-3f);
    }

    [Fact]
    public void SpringDamper_Critical_SettlesWithoutOvershoot()
    {
        var spring = new SpringDamperModifier { Frequency = 4f, DampingRatio = 1f };
        var memory = new ModifierMemory();
        Run(spring, Vector3.Zero, memory);
        float max = 0f, last = 0f;
        for (int i = 0; i < 120; i++) { last = Run(spring, Vector3.Right, memory).X; max = Mathf.Max(max, last); }
        max.Should().BeLessThanOrEqualTo(1.0001f);
        last.Should().BeApproximately(1f, 0.01f);
    }

    [Fact]
    public void SpringDamper_Underdamped_Overshoots_AndIsFrameRateIndependent()
    {
        var spring = new SpringDamperModifier { Frequency = 3f, DampingRatio = 0.3f };
        float Peak(double dt, int steps)
        {
            var memory = new ModifierMemory();
            Run(spring, Vector3.Zero, memory, dt);
            float peak = 0f;
            for (int i = 0; i < steps; i++) peak = Mathf.Max(peak, Run(spring, Vector3.Right, memory, dt).X);
            return peak;
        }
        float at60 = Peak(1.0 / 60.0, 60), at20 = Peak(1.0 / 20.0, 20);
        at60.Should().BeGreaterThan(1.2f, "it overshoots");
        at20.Should().BeApproximately(at60, 0.05f);
    }

    private static (Rig Rig, EnhancedInputAction Move) MoveRig(OpposingInputs horizontal, OpposingInputs vertical = OpposingInputs.Neutral)
    {
        var move = Make.Action("move", ActionValueKind.Axis2D);
        var source = InputSource.ForKeyAxis2D(Key.W, Key.S, Key.A, Key.D);
        source.Opposing = horizontal;
        source.OpposingVertical = vertical;
        var rig = new Rig().Add(Make.Context("C", Make.Bind(move, source)));
        return (rig, move);
    }

    [Theory]
    [InlineData(OpposingInputs.Neutral, 0f)]
    [InlineData(OpposingInputs.LastWins, 1f)]
    [InlineData(OpposingInputs.FirstWins, -1f)]
    [InlineData(OpposingInputs.NegativeWins, -1f)]
    [InlineData(OpposingInputs.PositiveWins, 1f)]
    public void Opposing_LeftThenRight(OpposingInputs rule, float expectedX)
    {
        var (rig, move) = MoveRig(rule);
        using var _ = rig;
        rig.Press(Key.A).Tick().Press(Key.D).Tick();
        rig.Player.ReadVector2(move).X.Should().BeApproximately(expectedX, 1e-4f);
        rig.Release(Key.D).Tick();
        rig.Player.ReadVector2(move).X.Should().BeApproximately(-1f, 1e-4f, "one side alone is unaffected");
    }

    [Fact]
    public void Opposing_HitboxRule_LeftRightNeutral_UpWins()
    {
        var (rig, move) = MoveRig(OpposingInputs.Neutral, OpposingInputs.NegativeWins);
        using var _ = rig;
        rig.Press(Key.S).Press(Key.W).Press(Key.A).Press(Key.D).Tick();
        rig.Player.ReadVector2(move).Should().Be(new Vector2(0, -1));
    }

    [Fact]
    public void Opposing_LastWins_OnComposites_AndSurvivesJson()
    {
        var steer = Make.Action("steer", ActionValueKind.Axis1D);
        var source = InputSource.Composite(CompositeMode.Axis1D, InputSource.ForKey(Key.Q), InputSource.ForKey(Key.E));
        source.Opposing = OpposingInputs.LastWins;
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(steer, source)));
        rig.Press(Key.E).Tick().Press(Key.Q).Tick();
        rig.Player.ReadFloat(steer).Should().BeApproximately(-1f, 1e-4f);

        var copy = InputSourceJson.FromJson(InputSourceJson.ToJson(source), out _);
        copy.Opposing.Should().Be(OpposingInputs.LastWins);
        BindingEditing.KeepSettings(InputSourcePath.Parse("axis1d(key:q, key:e)"), source).Opposing.Should().Be(OpposingInputs.LastWins);
    }

    [Fact]
    public void FuseStep_StartsFromTheMeasuredTilt_AndFollowsTheGyro()
    {
        // Lying level: gravity straight down the device's -Y.
        var level = InputPlayer.FuseStep(Quaternion.Identity, Vector3.Zero, new Vector3(0, -9.81f, 0), 0.016f, 1f, fresh: true);
        (level * Vector3.Down).DistanceTo(Vector3.Down).Should().BeLessThan(1e-3f);

        // Turning about the vertical at 90°/s for one second, gravity unchanged.
        var q = level;
        for (int i = 0; i < 100; i++) q = InputPlayer.FuseStep(q, new Vector3(0, Mathf.Pi / 2f, 0), new Vector3(0, -9.81f, 0), 0.01f, 1f, fresh: false);
        (q * Vector3.Forward).DistanceTo(Vector3.Left).Should().BeLessThan(0.02f, "a quarter turn to the left");
    }

    [Fact]
    public void FuseStep_GravityCorrectsGyroDrift()
    {
        var q = InputPlayer.FuseStep(Quaternion.Identity, Vector3.Zero, new Vector3(0, -9.81f, 0), 0.01f, 2f, fresh: true);
        for (int i = 0; i < 1000; i++) q = InputPlayer.FuseStep(q, new Vector3(0.05f, 0, 0), new Vector3(0, -9.81f, 0), 0.01f, 2f, fresh: false);
        (q * Vector3.Down).AngleTo(Vector3.Down).Should().BeLessThan(Mathf.DegToRad(3f), "a biased gyro does not tip it over");
    }

    [Fact]
    public void Orientation_Source_IsFusedFromFedSensors()
    {
        var aim = Make.Action("aim", ActionValueKind.Quaternion);
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(aim, InputSourcePath.Parse("pad0:orientation"))));
        rig.Player.PollMotionSensors = false;
        rig.Player.Raw.Set(SourceKey.ForMotion(MotionSensor.Accelerometer, 0), new Vector4(0, -9.81f, 0, 0));
        rig.Player.Raw.Set(SourceKey.ForMotion(MotionSensor.Gyroscope, 0), new Vector4(0, Mathf.Pi / 2f, 0, 0));
        rig.Tick(30, 1.0 / 30.0);   // one second
        var q = rig.Player.ReadRotation(aim);
        (q * Vector3.Forward).DistanceTo(Vector3.Left).Should().BeLessThan(0.05f);
        rig.Player.IsTriggered(aim).Should().BeTrue("an orientation is tracked input");

        // The sensor reading disappears (pad unplugged, sensors off): the orientation ends as lost tracking.
        rig.Player.Raw.Set(SourceKey.ForMotion(MotionSensor.Accelerometer, 0), Vector4.Zero);
        rig.Tick();
        rig.Player.IsTriggered(aim).Should().BeFalse("no reading, no orientation");
        rig.Player.GetState(aim).EndReason.Should().Be(InputEndReason.TrackingLost);
        rig.Player.Raw.Set(SourceKey.ForMotion(MotionSensor.Accelerometer, 0), new Vector4(0, -9.81f, 0, 0));

        rig.Player.ResetOrientation();
        rig.Player.Raw.Set(SourceKey.ForMotion(MotionSensor.Gyroscope, 0), Vector4.Zero);
        rig.Tick();
        (rig.Player.ReadRotation(aim) * Vector3.Forward).DistanceTo(Vector3.Forward).Should().BeLessThan(1e-3f, "recentered");
    }
}
