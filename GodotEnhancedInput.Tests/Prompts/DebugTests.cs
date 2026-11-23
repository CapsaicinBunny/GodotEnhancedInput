using FluentAssertions;
using Godot;

namespace GodotEnhancedInput.Tests.Prompts;

/// <summary>DBG-1, DBG-2.</summary>
[Collection("Godot")]
public class DebugTests(EngineFixture fixture)
{
    [Fact]
    public void Dump_ListsDevicesContextsActionsAndTimings()
    {
        var jump = Make.Action("jump");
        var close = Make.Action("close");
        var gameplay = Make.Context("gameplay");
        gameplay.Add(jump, InputSource.ForKey(Key.Space)).Rebindable("jump_key");
        var menu = Make.Context("menu", Make.Bind(close, Key.Escape));
        menu.BlockLowerPriority = true;
        using var rig = new Rig().Add(gameplay);

        rig.Press(Key.Space).Press(Key.Escape).Tick();
        rig.Add(menu, 100, new ContextOptions { IgnoreHeldSourcesUntilReleased = true });
        rig.Player.Rebind("jump_key", InputSource.ForKey(Key.J));
        rig.Player.StartCapture("jump_key");

        var text = rig.Player.DescribeState();
        text.Should().Contain("Player 0").And.Contain("keyboard/mouse").And.Contain("last: KeyboardMouse");
        text.IndexOf("menu", StringComparison.Ordinal).Should().BeLessThan(text.IndexOf("gameplay", StringComparison.Ordinal), "contexts are listed by priority");
        text.Should().Contain("[blocks lower]").And.Contain("ignoring until released: PhysicalKey:Escape");
        text.Should().Contain("capturing a press for 'jump_key'").And.Contain("1 override(s) — jump_key");
        text.Should().Contain("jump").And.Contain("Triggered").And.Contain("[Started, Triggered]").And.Contain("0.017s");
    }

    [Fact]
    public void Dump_OnlyActive_HidesIdleActions()
    {
        var jump = Make.Action("jump");
        var crouch = Make.Action("crouch");
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(jump, Key.Space), Make.Bind(crouch, Key.C)));
        rig.Press(Key.Space).Tick();

        var text = rig.Player.DescribeState(onlyActive: true);
        text.Should().Contain("jump").And.NotContain("crouch");
        using var empty = new Rig();
        empty.Player.DescribeState().Should().Contain("(none evaluated yet)");
    }

    [Fact]
    public void Dump_System_ListsEveryPlayer()
    {
        var system = new EnhancedInput { TickMode = TickMode.Manual };
        try
        {
            var second = system.AddPlayer();
            system.AssignJoypad(second, 4);
            var text = system.DescribeState();
            text.Should().Contain("players: 2").And.Contain("Player 0").And.Contain("Player 1").And.Contain("joypads 4");
        }
        finally { system.Free(); }
    }

    [Fact]
    public void Overlay_ShowsStateAndRecentEvents()
    {
        var jump = Make.Action("jump");
        var system = new EnhancedInput { TickMode = TickMode.Manual };
        var overlay = new InputDebugOverlay { System = system, HistoryLength = 3 };
        fixture.Tree.Root.AddChild(overlay);
        try
        {
            overlay.Visible.Should().BeFalse("hidden until toggled");
            overlay.Refresh();   // connects to the players
            system.GetPlayer(0).AddContext(Make.Context("C", Make.Bind(jump, Key.Space)));

            system.FeedEvent(Rig.KeyEvent(Key.Space, true));
            system.Tick(Rig.Dt);
            system.Tick(Rig.Dt);
            system.FeedEvent(Rig.KeyEvent(Key.Space, false));
            system.Tick(Rig.Dt);
            overlay.Refresh();

            overlay.Text.Should().Contain("Player 0").And.Contain("jump");
            overlay.History.Should().HaveCount(3, "Started, Triggered ×2, Completed — capped at 3");
            overlay.History[^1].Should().Contain("jump Completed");
            overlay.History.Should().NotContain(line => line.Contains("Ongoing"));
        }
        finally
        {
            overlay.GetParent().RemoveChild(overlay);
            overlay.Free();
            system.Free();
        }
    }

    [Fact]
    public void Overlay_ToggleKey_AndSinglePlayer()
    {
        var system = new EnhancedInput { TickMode = TickMode.Manual };
        system.AddPlayer();
        var overlay = new InputDebugOverlay { System = system, PlayerIndex = 1 };
        fixture.Tree.Root.AddChild(overlay);
        try
        {
            overlay._Input(Rig.KeyEvent(Key.F3, true));
            overlay.Visible.Should().BeTrue();
            overlay._Input(Rig.KeyEvent(Key.F3, true, echo: true));
            overlay.Visible.Should().BeTrue("echoes do not toggle");
            overlay._Input(Rig.KeyEvent(Key.F3, true));
            overlay.Visible.Should().BeFalse();

            overlay.Refresh();
            overlay.Text.Should().Contain("Player 1").And.NotContain("Player 0");
        }
        finally
        {
            overlay.GetParent().RemoveChild(overlay);
            overlay.Free();
            system.Free();
        }
    }
}
