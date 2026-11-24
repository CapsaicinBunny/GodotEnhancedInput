using FluentAssertions;
using Godot;

namespace GodotEnhancedInput.Tests.Players;

/// <summary>PLR-1 … PLR-4.</summary>
[Collection("Godot")]
public class PlayerTests
{
    private sealed class World : IDisposable
    {
        public readonly EnhancedInput System = new() { TickMode = TickMode.Manual };
        public readonly EnhancedInputAction Jump = Make.Action("jump");
        public readonly EnhancedInputMappingContext Context;

        public World()
        {
            Context = Make.Context("gameplay",
                Make.Bind(Jump, InputSource.ForJoyButton(JoyButton.A)),
                Make.Bind(Jump, Key.Space));
        }

        public InputPlayer Player(int i) => System.GetPlayer(i);

        public InputPlayer AddPlayer()
        {
            var player = System.AddPlayer();
            player.AddContext(Context);
            return player;
        }

        public void Press(int device, JoyButton button = JoyButton.A, bool pressed = true)
            => System.FeedEvent(new InputEventJoypadButton { ButtonIndex = button, Pressed = pressed, Device = device });

        public void Tick() => System.Tick(Rig.Dt);

        public void Dispose() => System.Free();
    }

    [Fact]
    public void NewPlayers_ListenOnlyToTheirDevices()
    {
        using var w = new World();
        w.Player(0).AddContext(w.Context);
        var second = w.AddPlayer();

        second.AcceptKeyboardMouse.Should().BeFalse();
        second.AcceptTouch.Should().BeFalse();
        second.AcceptActionEvents.Should().BeFalse();

        w.System.FeedEvent(Rig.KeyEvent(Key.Space, true));
        w.Tick();
        w.Player(0).IsTriggered(w.Jump).Should().BeTrue();
        second.IsTriggered(w.Jump).Should().BeFalse();
    }

    [Fact]
    public void AssignedJoypad_ReachesOnlyItsOwner()
    {
        using var w = new World();
        w.Player(0).AddContext(w.Context);
        var second = w.AddPlayer();
        w.System.AssignJoypad(second, 3);

        w.Press(3);
        w.Tick();
        second.IsTriggered(w.Jump).Should().BeTrue();
        w.Player(0).IsTriggered(w.Jump).Should().BeFalse("player 0 has no list, but joypad 3 is owned");
        w.System.GetJoypadOwner(3).Should().BeSameAs(second);
    }

    [Fact]
    public void AssignJoypad_MovesItFromThePreviousOwner()
    {
        using var w = new World();
        var second = w.AddPlayer();
        w.System.AssignJoypad(w.Player(0), 1);
        w.System.AssignJoypad(second, 1);

        w.Player(0).JoypadDevices.Should().NotContain(1);
        second.JoypadDevices.Should().Contain(1);
        w.System.UnassignJoypad(1).Should().BeTrue();
        w.System.GetJoypadOwner(1).Should().BeNull();
    }

    [Fact]
    public void UnassignedJoypads_GoToPlayersWithoutAList()
    {
        using var w = new World();
        w.Player(0).AddContext(w.Context);
        var second = w.AddPlayer();
        w.System.AssignJoypad(second, 1);

        w.Press(5);
        w.Tick();
        w.Player(0).IsTriggered(w.Jump).Should().BeTrue("joypad 5 belongs to nobody, and player 0 has no list");
        second.IsTriggered(w.Jump).Should().BeFalse();
    }

    [Fact]
    public void Join_FirstPadGoesToPlayer0_NextOnesCreatePlayers()
    {
        using var w = new World();
        w.System.JoinMode = JoinMode.OnButtonPress;
        var joined = new List<(int Player, int Device)>();
        w.System.PlayerJoined += (p, d) => joined.Add((p.PlayerIndex, d));

        w.Press(2);
        w.Press(7);

        joined.Should().Equal((0, 2), (1, 7));
        w.System.Players.Should().HaveCount(2);
        w.System.GetJoypadOwner(7).PlayerIndex.Should().Be(1);
    }

    [Fact]
    public void Join_PressDoesNotReachActions_UnlessConfigured()
    {
        using var w = new World();
        w.System.JoinMode = JoinMode.OnButtonPress;
        w.Player(0).AddContext(w.Context);

        w.Press(2);
        w.Tick();
        w.Player(0).IsTriggered(w.Jump).Should().BeFalse();

        w.System.ConsumeJoinPress = false;
        w.Press(4);  // creates player 1 and reaches it
        var second = w.Player(1);
        second.JoypadDevices.Should().Contain(4);
    }

    [Fact]
    public void Join_SwallowsTheSamePressInTheUnhandledStage()
    {
        using var w = new World();
        w.System.JoinMode = JoinMode.OnButtonPress;
        w.Player(0).AddContext(w.Context);
        var press = new InputEventJoypadButton { ButtonIndex = JoyButton.A, Pressed = true, Device = 2 };

        w.System.FeedEvent(press, IntakeStage.Early);
        w.System.FeedEvent(press, IntakeStage.Unhandled);
        w.Tick();
        w.Player(0).IsTriggered(w.Jump).Should().BeFalse();

        w.Press(2);
        w.Tick();
        w.Player(0).IsTriggered(w.Jump).Should().BeTrue("later presses reach the player");
    }

    [Fact]
    public void Join_RespectsMaxPlayers_AndManualMode()
    {
        using var w = new World();
        w.Press(2);
        w.System.GetJoypadOwner(2).Should().BeNull("manual mode assigns nothing");

        w.System.JoinMode = JoinMode.OnButtonPress;
        w.System.MaxPlayers = 2;
        w.Press(1);
        w.Press(2);
        w.Press(3);
        w.System.Players.Should().HaveCount(2);
        w.System.GetJoypadOwner(3).Should().BeNull();
    }

    [Fact]
    public void Disconnect_KeepsAssignment()
    {
        using var w = new World();
        var second = w.AddPlayer();
        w.System.AssignJoypad(second, 1);
        var changes = new List<(int, bool)>();
        w.System.JoypadConnectionChanged += (d, c) => changes.Add((d, c));

        w.Press(1);
        w.Tick();
        second.IsTriggered(w.Jump).Should().BeTrue();

        w.System.NotifyJoypadConnection(1, false);
        w.Tick();
        second.WasCanceled(w.Jump).Should().BeTrue("a lost pad cuts input off");
        second.JoypadDevices.Should().Contain(1);

        w.System.NotifyJoypadConnection(1, true);
        changes.Should().Equal((1, false), (1, true));
    }

    [Fact]
    public void RemovePlayer_ReleasesJoypads_AndReindexes()
    {
        using var w = new World();
        var second = w.AddPlayer();
        var third = w.AddPlayer();
        w.System.AssignJoypad(second, 1);

        w.System.RemovePlayer(second).Should().BeTrue();
        w.System.GetJoypadOwner(1).Should().BeNull();
        third.PlayerIndex.Should().Be(1);
        w.System.RemovePlayer(w.Player(0)).Should().BeFalse("player 0 always exists");
    }

    [Fact]
    public void Players_HaveTheirOwnProfiles()
    {
        using var w = new World();
        w.Player(0).Profile.Should().NotBeSameAs(w.AddPlayer().Profile);
        w.Player(1).DefaultProfilePath.Should().EndWith("player_1.json");
    }
}
