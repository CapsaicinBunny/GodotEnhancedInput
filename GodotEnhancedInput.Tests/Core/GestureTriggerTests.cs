using System.Linq;
using FluentAssertions;
using Godot;
using Xunit;
using static GodotEnhancedInput.TriggerPhase;

namespace GodotEnhancedInput.Tests.Core;

/// <summary>Direction sectors, release velocity, double-click distance and combo tolerance.</summary>
[Collection("Godot")]
public class GestureTriggerTests
{
    private const double Step = 1.0 / 60.0;

    private static TriggerPhase[] Run(InputTrigger trigger, params Vector2[] values)
    {
        var memory = new TriggerMemory();
        return values.Select(v => trigger.Evaluate(new TriggerContext(new Vector3(v.X, v.Y, 0), ActionValueKind.Axis2D, Step, null, memory))).ToArray();
    }

    [Fact]
    public void Direction_TriggeredInsideTheSector()
    {
        var up = new DirectionTrigger { Direction = Vector2.Up, SectorDegrees = 90 };
        Run(up, new Vector2(0, -1), new Vector2(0.3f, -0.9f), new Vector2(1, 0), new Vector2(0, -0.2f), Vector2.Zero)
            .Should().Equal(Triggered, Triggered, Idle, Idle, Idle);
    }

    [Fact]
    public void Direction_Once_FiresOnEntering()
    {
        var right = new DirectionTrigger { Direction = Vector2.Right, SectorDegrees = 45, Once = true };
        Run(right, new Vector2(1, 0), new Vector2(1, 0.1f), new Vector2(0, 1), new Vector2(1, 0))
            .Should().Equal(Triggered, Ongoing, Idle, Triggered);
    }

    [Fact]
    public void ReleaseVelocity_FastReleaseFires_SlowOneDoesNot()
    {
        var throwing = new ReleaseVelocityTrigger { MinSpeed = 4f, WindowSeconds = 0.1f };
        Run(throwing, new Vector2(0.6f, 0), new Vector2(1f, 0), Vector2.Zero).Should().Equal(Ongoing, Ongoing, Triggered);

        var slow = new ReleaseVelocityTrigger { MinSpeed = 4f };
        Run(slow, new Vector2(0.6f, 0), new Vector2(0.62f, 0), new Vector2(0.64f, 0), Vector2.Zero).Should().Equal(Ongoing, Ongoing, Ongoing, Idle);
    }

    [Fact]
    public void ReleaseVelocity_MotionValues_AreSpeedsAlready()
    {
        var flingDrag = new ReleaseVelocityTrigger { MinSpeed = 4f, Measure = VelocityMeasure.Motion, ActuationThreshold = 0.01f };
        Run(flingDrag, new Vector2(0.2f, 0), new Vector2(0.2f, 0), Vector2.Zero).Should().Equal(Ongoing, Ongoing, Triggered);
    }

    [Fact]
    public void MultiTap_MaxDistance_RequiresTheSameSpot()
    {
        using var rig = new Rig();
        var doubleClick = new MultiTapTrigger { Taps = 2, MaxDistance = 8f };
        var memory = new TriggerMemory();
        TriggerPhase Tap(float value, Vector2 at)
        {
            rig.Player.Raw.Set(SourceKey.MousePosition, new Vector4(at.X, at.Y, 0, 0));
            return doubleClick.Evaluate(new TriggerContext(new Vector3(value, 0, 0), ActionValueKind.Digital, Step, rig.Player, memory));
        }

        Tap(1, new Vector2(10, 10));
        Tap(0, new Vector2(10, 10));
        Tap(1, new Vector2(100, 100));
        Tap(0, new Vector2(100, 100)).Should().Be(Ongoing, "too far: a new sequence started");
        Tap(1, new Vector2(103, 101));
        Tap(0, new Vector2(103, 101)).Should().Be(Triggered, "close enough to the first tap");
    }

    private static (Rig Rig, EnhancedInputAction Special) ComboRig(float tolerance, bool ignoreOutOfOrder = false)
    {
        var down = Make.Action("down");
        var forward = Make.Action("forward");
        var punch = Make.Action("punch");
        var special = Make.Action("special");
        var combo = new ComboTrigger { ToleranceSeconds = tolerance, IgnoreOutOfOrder = ignoreOutOfOrder };
        combo.Steps.Add(ComboStep.Create(down));
        combo.Steps.Add(ComboStep.Create(forward, timeLimit: 0.5f));
        combo.Steps.Add(ComboStep.Create(punch, timeLimit: 0.5f));
        var rig = new Rig().Add(Make.Context("C",
            Make.Bind(special, InputSource.Nothing(), combo),
            Make.Bind(down, Key.S, new PressedTrigger()),
            Make.Bind(forward, Key.D, new PressedTrigger()),
            Make.Bind(punch, Key.J, new PressedTrigger())));
        return (rig, special);
    }

    [Fact]
    public void Combo_Tolerance_AcceptsTheButtonAMomentEarly()
    {
        var (rig, special) = ComboRig(tolerance: 0.05f);
        using var _ = rig;
        rig.Press(Key.S).Tick().Release(Key.S).Tick();
        rig.Press(Key.J).Tick();       // the punch comes 2 frames before the last direction
        rig.Press(Key.D).Tick();
        rig.Player.WasTriggered(special).Should().BeTrue();
    }

    [Fact]
    public void Combo_Strict_RejectsTheSameInput()
    {
        var (rig, special) = ComboRig(tolerance: 0f);
        using var _ = rig;
        rig.Press(Key.S).Tick().Release(Key.S).Tick();
        rig.Press(Key.J).Tick();
        rig.Press(Key.D).Tick();
        rig.Player.WasTriggered(special).Should().BeFalse();
    }

    [Fact]
    public void Combo_Tolerance_DoesNotReachBackTooFar()
    {
        var (rig, special) = ComboRig(tolerance: 0.05f);
        using var _ = rig;
        rig.Press(Key.S).Tick().Release(Key.S).Tick();
        rig.Press(Key.J).Tick().Release(Key.J).TickFor(0.2);
        rig.Press(Key.D).Tick();
        rig.Player.WasTriggered(special).Should().BeFalse("the punch was 0.2 s early");
    }

    [Fact]
    public void Combo_IgnoreOutOfOrder_SurvivesAStrayStep()
    {
        var (rig, special) = ComboRig(tolerance: 0f, ignoreOutOfOrder: true);
        using var _ = rig;
        rig.Press(Key.S).Tick().Release(Key.S).Tick();
        rig.Press(Key.J).Tick().Release(Key.J).Tick();   // stray
        rig.Press(Key.D).Tick().Release(Key.D).Tick();
        rig.Press(Key.J).Tick();
        rig.Player.WasTriggered(special).Should().BeTrue();
    }
}
