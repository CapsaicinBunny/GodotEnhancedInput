using FluentAssertions;
using Godot;

namespace GodotEnhancedInput.Tests.Players;

/// <summary>HAP-1 … HAP-6: haptic effects, mixing, priorities, settings, ownership.</summary>
[Collection("Godot")]
public class HapticsTests
{
    private sealed class RecordingHaptics : IHapticsOutput
    {
        public readonly List<string> Log = new();
        public void StartJoypad(int device, float weak, float strong, float seconds) => Log.Add($"start {device} {weak:0.##} {strong:0.##}");
        public void StopJoypad(int device) => Log.Add($"stop {device}");
        public void SetJoypadLight(int device, Color color) => Log.Add($"light {device}");
        public void VibrateHandheld(int milliseconds, float amplitude) => Log.Add($"handheld {milliseconds}");
        public void StartTriggers(int device, float left, float right, float seconds) => Log.Add($"triggers {device} {left:0.##} {right:0.##}");
    }

    private static (Rig rig, RecordingHaptics log) Make2Pads()
    {
        var rig = new Rig();
        var log = new RecordingHaptics();
        rig.Player.HapticsOutput = log;
        rig.Player.AcceptTouch = false;
        rig.Player.JoypadDevices.Add(2);
        rig.Player.JoypadDevices.Add(4);
        return (rig, log);
    }

    [Fact]
    public void Rumble_GoesToThePlayersPads_AndFollowsTheStrengthSetting()
    {
        var (rig, haptics) = Make2Pads();
        using (rig)
        {
            rig.Player.Rumble(1f, 0.5f, 0.2f);
            haptics.Log.Should().Equal("start 2 1 0.5", "start 4 1 0.5");

            haptics.Log.Clear();
            rig.Player.Profile.SetSetting("vibration_strength", 0.5);
            rig.Player.Rumble(1f, 0.5f, 0.2f, device: 4);
            haptics.Log.Should().Equal(new[] { "start 2 0.5 0.25", "start 4 0.5 0.25" }, "running effects follow the setting");

            haptics.Log.Clear();
            rig.Player.Profile.SetSetting("vibration_strength", 0);
            rig.Player.UpdateHaptics(0);
            haptics.Log.Should().Equal("stop 2", "stop 4");

            haptics.Log.Clear();
            rig.Player.SetJoypadLight(Colors.Red, 2);
            rig.Player.VibrateHandheld(100);
            haptics.Log.Should().Equal("light 2");
        }
    }

    [Fact]
    public void Effects_EndOnTheirOwn_AndStopTheMotors()
    {
        var (rig, haptics) = Make2Pads();
        using (rig)
        {
            var handle = rig.Player.PlayHaptic(HapticEffect.Click(), device: 2);
            handle.IsPlaying.Should().BeTrue();
            haptics.Log.Should().Equal("start 2 0.6 0.1");
            rig.Tick(10);
            handle.IsPlaying.Should().BeFalse();
            haptics.Log.Should().EndWith("stop 2");
            rig.Player.PlayingHaptics.Should().BeEmpty();
        }
    }

    [Fact]
    public void Mixing_StrongestWins_AndOverridesSilenceLowerPriorities()
    {
        var (rig, haptics) = Make2Pads();
        using (rig)
        {
            var engine = HapticEffect.Engine(0.4f);
            engine.Attack = 0f;
            rig.Player.Haptics.Play(engine, loop: true, device: 2);
            var hit = HapticEffect.Constant(0.1f, 0.9f, 0.1f);
            rig.Player.Haptics.Play(hit, device: 2);
            haptics.Log.Should().EndWith("start 2 0.24 0.9");

            haptics.Log.Clear();
            var cutscene = new HapticEffect { Weak = 0.05f, Strong = 0.05f, Duration = 0f, Blend = HapticBlend.Override, Priority = 10 };
            var cut = rig.Player.Haptics.Play(cutscene, device: 2);
            haptics.Log.Should().Equal("start 2 0.05 0.05");

            haptics.Log.Clear();
            rig.Player.Haptics.Stop(cut);
            rig.Player.Haptics.Stop(hit);
            haptics.Log.Should().EndWith("start 2 0.24 0.4");

            rig.Player.Haptics.Stop(engine);
            haptics.Log.Should().EndWith("stop 2");
        }
    }

    [Fact]
    public void Envelope_RepeatsAndLoop()
    {
        var effect = new HapticEffect { Duration = 0.1f, Attack = 0.05f, Release = 0f, Repeats = 1, Gap = 0.1f };
        effect.Sample(0.025, out _).Should().BeApproximately(0.5f, 1e-4f);
        effect.Sample(0.07, out _).Should().Be(1f);
        effect.Sample(0.15, out var done).Should().Be(0f, "in the gap");
        done.Should().BeFalse();
        effect.Sample(0.27, out _).Should().Be(1f, "second pulse");
        effect.Sample(0.31, out done);
        done.Should().BeTrue();
        effect.Sample(5.07, loop: true, out done).Should().Be(1f);
        done.Should().BeFalse();
        effect.TotalSeconds.Should().BeApproximately(0.3, 1e-6);
    }

    [Fact]
    public void ExplicitDevices_MustBelongToThePlayer()
    {
        var (rig, haptics) = Make2Pads();
        using (rig)
        {
            rig.Player.Rumble(1f, 1f, 0.2f, device: 7).Should().NotBeNull();
            haptics.Log.Should().BeEmpty("pad 7 is not this player's");
            rig.Player.SetJoypadLight(Colors.Blue, 7);
            haptics.Log.Should().BeEmpty();
            rig.Player.Haptics.PlayOn("xr:0", HapticEffect.Click()).Should().BeNull("not an owned backend device");
        }
    }

    [Fact]
    public void Handheld_BuzzesOnce_AndBindHapticPlaysOnPress()
    {
        using var rig = new Rig();
        var haptics = new RecordingHaptics();
        rig.Player.HapticsOutput = haptics;
        rig.Player.AcceptTouch = true;
        rig.Player.PlayHaptic(HapticEffect.Bump());
        haptics.Log.Should().Contain("handheld 120");

        var jump = Make.Action("jump");
        rig.Add(Make.Context("C", Make.Bind(jump, Key.Space, new PressedTrigger())));
        rig.Player.BindHaptic(jump, ActionEvents.Triggered, HapticEffect.Tap());
        rig.Player.StopAllHaptics();
        rig.Press(Key.Space).Tick();
        rig.Player.PlayingHaptics.Should().ContainSingle(h => h.Effect.Duration == 0.03f);
    }
}
