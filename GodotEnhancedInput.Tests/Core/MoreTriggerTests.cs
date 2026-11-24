using FluentAssertions;
using Godot;
using static GodotEnhancedInput.TriggerPhase;

namespace GodotEnhancedInput.Tests.Core;

/// <summary>TRX-1 … TRX-8: multi-tap, toggle, repeat, threshold, hysteresis, flick, distance, predicate.</summary>
[Collection("Godot")]
public class MoreTriggerTests
{
    private const double Step = 0.1;

    private static TriggerPhase[] Run(InputTrigger trigger, params float[] values)
    {
        var memory = new TriggerMemory();
        return values.Select(v => trigger.Evaluate(new TriggerContext(new Vector3(v, 0, 0), ActionValueKind.Axis1D, Step, null, memory))).ToArray();
    }

    [Fact]
    public void MultiTap_DoubleTap()
        => Run(new MultiTapTrigger { Taps = 2, TapSeconds = 0.2f, GapSeconds = 0.25f }, 1, 0, 0, 1, 0, 0)
            .Should().Equal(Ongoing, Ongoing, Ongoing, Ongoing, Triggered, Idle);

    [Fact]
    public void MultiTap_GapTooLong_StartsOver()
        => Run(new MultiTapTrigger { Taps = 2, GapSeconds = 0.15f }, 1, 0, 0, 0, 1, 0)
            .Should().Equal(Ongoing, Ongoing, Ongoing, Idle, Ongoing, Ongoing);

    [Fact]
    public void MultiTap_HeldTooLong_StartsOver()
        => Run(new MultiTapTrigger { Taps = 2, TapSeconds = 0.15f }, 1, 1, 1, 0, 1, 0, 1, 0)
            .Should().Equal(Ongoing, Idle, Idle, Idle, Ongoing, Ongoing, Ongoing, Triggered);

    [Fact]
    public void Toggle_FlipsOnEachPress()
        => Run(new ToggleTrigger(), 1, 1, 0, 0, 1, 0).Should().Equal(Triggered, Triggered, Triggered, Triggered, Idle, Idle);

    [Fact]
    public void Repeat_PressThenInitialDelayThenInterval()
        => Run(new RepeatTrigger { InitialDelaySeconds = 0.3f, IntervalSeconds = 0.1f }, 1, 1, 1, 1, 1, 1, 0)
            .Should().Equal(Triggered, Ongoing, Ongoing, Triggered, Triggered, Triggered, Idle);

    [Fact]
    public void Repeat_LongPressWithoutPressTrigger_AndLimit()
        => Run(new RepeatTrigger { TriggerOnPress = false, InitialDelaySeconds = 0.2f, IntervalSeconds = 0.1f, MaxRepeats = 2 }, 1, 1, 1, 1, 1)
            .Should().Equal(Ongoing, Ongoing, Triggered, Triggered, Idle);

    [Fact]
    public void Threshold_ReportsCrossingsWithABand()
    {
        Run(new ThresholdTrigger { Threshold = 0.5f, Band = 0.1f }, 0.3f, 0.52f, 0.6f, 0.4f, 0.7f)
            .Should().Equal(Idle, Idle, Triggered, Idle, Triggered);
        Run(new ThresholdTrigger { Threshold = 0.5f, Band = 0.1f, Direction = CrossingDirection.Both }, 0.6f, 0.4f)
            .Should().Equal(Triggered, Triggered);
    }

    [Fact]
    public void Hysteresis_OnHighOffLow()
        => Run(new HysteresisTrigger { PressThreshold = 0.6f, ReleaseThreshold = 0.4f }, 0.5f, 0.7f, 0.5f, 0.45f, 0.3f)
            .Should().Equal(Idle, Triggered, Triggered, Triggered, Idle);

    [Fact]
    public void Flick_FastPushFires_SlowPushDoesNot()
    {
        Run(new FlickTrigger { MaxSeconds = 0.1f }, 0f, 0.5f, 1f, 1f, 0f, 0.95f)
            .Should().Equal(Idle, Ongoing, Triggered, Idle, Idle, Triggered);
        Run(new FlickTrigger { MaxSeconds = 0.1f }, 0f, 0.3f, 0.5f, 0.7f, 1f)
            .Should().Equal(Idle, Ongoing, Ongoing, Idle, Idle);
    }

    [Fact]
    public void Distance_AddsUpMovement()
        => Run(new DistanceTrigger { Distance = 10f, ResetSeconds = 0.15f, OneShot = true }, 4, 4, 4, 4, 0, 0, 9)
            .Should().Equal(Ongoing, Ongoing, Triggered, Idle, Idle, Idle, Ongoing);

    [Fact]
    public void Predicate_RunsCodeAndHonoursItsRole()
    {
        var predicate = new PredicateTrigger { Evaluator = ctx => ctx.Value.X > 0.5f ? Triggered : Idle };
        Run(predicate, 0.2f, 0.8f).Should().Equal(Idle, Triggered);
        new PredicateTrigger { TriggerRole = TriggerRole.Blocker }.Role.Should().Be(TriggerRole.Blocker);
        Run(new PredicateTrigger(), 1f).Should().Equal(Idle);
    }
}

/// <summary>MDX-1 … MDX-11: remap, sensitivity, acceleration, anti-deadzone, calibration, quantize, hysteresis, integrate, differentiate, DPI, gyro calibration, value natures.</summary>
[Collection("Godot")]
public class MoreModifierTests
{
    private static Vector3 Run(InputModifier m, Vector3 v, double dt = 0.1, InputPlayer player = null, ModifierMemory memory = null, ValueNature nature = ValueNature.State)
        => m.Run(v, new ModifierContext(ActionValueKind.Axis3D, dt, player, memory ?? new ModifierMemory(), nature));

    private static void Near(Vector3 a, Vector3 b)
    {
        a.X.Should().BeApproximately(b.X, 1e-4f);
        a.Y.Should().BeApproximately(b.Y, 1e-4f);
        a.Z.Should().BeApproximately(b.Z, 1e-4f);
    }

    [Fact]
    public void Remap_PedalToZeroOne()
        => Near(Run(new RemapModifier(), new Vector3(-1f, 0f, 1f)), new Vector3(0f, 0.5f, 1f));

    [Fact]
    public void Sensitivity_ReadsProfileSettings()
    {
        using var rig = new Rig();
        var modifier = new SensitivityModifier { Setting = "look_sensitivity", InvertYSetting = "invert_y", Scale = new Vector3(2, 2, 1) };
        Near(Run(modifier, new Vector3(1, 1, 0), player: rig.Player), new Vector3(2, 2, 0));
        rig.Player.Profile.SetSetting("look_sensitivity", 0.5);
        rig.Player.Profile.SetFlag("invert_y", true);
        Near(Run(modifier, new Vector3(1, 1, 0), player: rig.Player), new Vector3(1, -1, 0));
    }

    [Fact]
    public void Acceleration_FastMovementGainsMore()
    {
        var accel = new AccelerationModifier { Acceleration = 0.01f, Exponent = 1f, MaxGain = 3f };
        Run(accel, new Vector3(1, 0, 0), dt: 0.1).X.Should().BeApproximately(1.1f, 1e-4f);
        Run(accel, new Vector3(100, 0, 0), dt: 0.1).X.Should().BeApproximately(300f, 1e-3f, "capped at the maximum gain");
    }

    [Fact]
    public void AntiDeadzone_LiftsSmallValues()
    {
        Near(Run(new AntiDeadzoneModifier { Minimum = 0.2f }, new Vector3(0.1f, 0, 0)), new Vector3(0.28f, 0, 0));
        Near(Run(new AntiDeadzoneModifier(), Vector3.Zero), Vector3.Zero);
    }

    [Fact]
    public void Calibration_MapsMinCentreMax()
        => Near(Run(new CalibrationModifier { Min = new Vector3(-0.8f, 0, 0), Center = new Vector3(0.1f, 0, 0), Max = new Vector3(0.9f, 1, 1) }, new Vector3(0.5f, 0, 0)),
            new Vector3(0.5f, 0, 0));

    [Fact]
    public void Quantize_StepsAndDirections()
    {
        Near(Run(new QuantizeModifier { Step = 0.25f }, new Vector3(0.3f, 0.6f, 0)), new Vector3(0.25f, 0.5f, 0));
        Near(Run(new QuantizeModifier { Mode = QuantizeMode.Directions, Directions = 4 }, new Vector3(0.9f, 0.3f, 0)), new Vector3(new Vector2(0.9f, 0.3f).Length(), 0, 0));
    }

    [Fact]
    public void Hysteresis_IgnoresJitter()
    {
        var m = new HysteresisModifier { Band = 0.05f };
        var memory = new ModifierMemory();
        Run(m, new Vector3(0.5f, 0, 0), memory: memory);
        Near(Run(m, new Vector3(0.52f, 0, 0), memory: memory), new Vector3(0.5f, 0, 0));
        Near(Run(m, new Vector3(0.6f, 0, 0), memory: memory), new Vector3(0.6f, 0, 0));
    }

    [Fact]
    public void Integrate_And_Differentiate()
    {
        var integrate = new IntegrateModifier { Min = new Vector3(-1, -1, -1), Max = new Vector3(1, 1, 1) };
        var memory = new ModifierMemory();
        Run(integrate, new Vector3(1, 0, 0), dt: 0.25, memory: memory);
        Near(Run(integrate, new Vector3(1, 0, 0), dt: 0.25, memory: memory), new Vector3(0.5f, 0, 0));
        Near(Run(new IntegrateModifier(), new Vector3(0.3f, 0, 0), nature: ValueNature.Delta), new Vector3(0.3f, 0, 0));

        var differentiate = new DifferentiateModifier();
        var dm = new ModifierMemory();
        Near(Run(differentiate, new Vector3(5, 0, 0), memory: dm), Vector3.Zero);
        Near(Run(differentiate, new Vector3(7, 1, 0), memory: dm), new Vector3(2, 1, 0));
    }

    [Fact]
    public void Dpi_ScalesByScreenDensity()
    {
        using var rig = new Rig();
        rig.Player.ScreenDpiOverride = 192f;
        Near(Run(new DpiScaleModifier { ReferenceDpi = 96f }, new Vector3(10, 4, 0), player: rig.Player), new Vector3(5, 2, 0));
    }

    [Fact]
    public void GyroCalibration_RemovesDrift()
    {
        var gyro = new GyroCalibrationModifier { StillThreshold = 0.05f, LearnRate = 50f };
        var memory = new ModifierMemory();
        for (int i = 0; i < 30; i++) Run(gyro, new Vector3(0.02f, 0, 0), memory: memory);
        Run(gyro, new Vector3(0.02f, 0, 0), memory: memory).X.Should().BeApproximately(0f, 1e-3f);
        Run(gyro, new Vector3(1.02f, 0, 0), memory: memory).X.Should().BeApproximately(1f, 0.01f);
    }

    [Fact]
    public void DeltaTimeScale_LeavesDeltaValuesAlone()
    {
        Near(Run(new DeltaTimeScaleModifier(), new Vector3(10, 0, 0), dt: 0.5, nature: ValueNature.Delta), new Vector3(10, 0, 0));
        Near(Run(new DeltaTimeScaleModifier { ScaleDeltaValues = true }, new Vector3(10, 0, 0), dt: 0.5, nature: ValueNature.Delta), new Vector3(5, 0, 0));
        InputSource.ForMouseMotion().Nature.Should().Be(ValueNature.Delta);
        InputSource.ForMousePosition().Nature.Should().Be(ValueNature.Absolute);
        InputSource.ForKey(Key.A).Nature.Should().Be(ValueNature.State);
    }

    [Fact]
    public void ProfileSettings_RoundTrip_AndBatchRaisesOnce()
    {
        var profile = new InputProfile();
        int changes = 0;
        profile.Changed += () => changes++;
        profile.Batch(() =>
        {
            profile.SetSetting("mouse_sensitivity", 1.5);
            profile.SetFlag("invert_look_y", true);
            profile.SetOverride("jump", InputSource.ForKey(Key.J));
        });
        changes.Should().Be(1);

        var back = InputProfile.FromJson(profile.ToJson());
        back.GetSetting("mouse_sensitivity").Should().Be(1.5);
        back.GetFlag("invert_look_y").Should().BeTrue();
        back.GetSetting("missing", 7).Should().Be(7);
    }
}
