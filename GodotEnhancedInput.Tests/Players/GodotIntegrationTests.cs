using System.Linq;
using FluentAssertions;
using Godot;
using Xunit;

namespace GodotEnhancedInput.Tests.Players;

/// <summary>InputMap import and output bridge, project settings, scene contexts, loop frames, split-screen and window routing, tool players, GDScript names.</summary>
[Collection("Godot")]
public class GodotIntegrationTests(EngineFixture fixture)
{
    // ------------------------------------------------------------------
    //  InputMap importer
    // ------------------------------------------------------------------

    [Fact]
    public void Importer_ConvertsEachKindOfEvent()
    {
        var space = InputMapImporter.ToSource(new InputEventKey { PhysicalKeycode = Key.Space });
        space.Kind.Should().Be(SourceKind.Key);
        space.Key.Should().Be(Key.Space);
        space.KeyMatch.Should().Be(KeyMatch.Physical);

        var save = InputMapImporter.ToSource(new InputEventKey { Keycode = Key.S, CtrlPressed = true });
        InputSourcePath.Format(save).Should().Be("keycode:ctrl + keycode:s");

        InputSourcePath.Format(InputMapImporter.ToSource(new InputEventMouseButton { ButtonIndex = MouseButton.Right })).Should().Be("mouse:right");
        InputSourcePath.Format(InputMapImporter.ToSource(new InputEventJoypadButton { ButtonIndex = JoyButton.A, Device = -1 })).Should().Be("pad:a");
        InputSourcePath.Format(InputMapImporter.ToSource(new InputEventJoypadMotion { Axis = JoyAxis.LeftX, AxisValue = -1f, Device = -1 })).Should().Be("pad:left_x-");
        InputMapImporter.ToSource(new InputEventScreenTouch()).Should().BeNull();
    }

    [Fact]
    public void Importer_MakesActionsAndMappings_FromTheRunningInputMap()
    {
        const string name = "gei_test_import";
        InputMap.AddAction(name, 0.3f);
        try
        {
            InputMap.ActionAddEvent(name, new InputEventKey { PhysicalKeycode = Key.Q });
            InputMap.ActionAddEvent(name, new InputEventJoypadMotion { Axis = JoyAxis.TriggerRight, AxisValue = 1f, Device = -1 });
            var imported = InputMapImporter.FromInputMap();
            imported.Actions.Select(a => a.Name).Should().Contain(name).And.NotContain(a => a.StartsWith("ui_"));
            var bindings = imported.Context.Bindings.Where(b => b.Action.Name == name).ToList();
            bindings.Should().HaveCount(2);
            bindings[1].Modifiers.OfType<DeadzoneModifier>().Single().Lower.Should().BeApproximately(0.3f, 1e-4f);

            using var rig = new Rig().Add(imported.Context);
            rig.Press(Key.Q).Tick();
            rig.Player.IsTriggered(imported.Actions.First(a => a.Name == name)).Should().BeTrue();
        }
        finally { InputMap.EraseAction(name); }
    }

    // ------------------------------------------------------------------
    //  InputMap output bridge
    // ------------------------------------------------------------------

    [Fact]
    public void Bridge_PressesTheGodotAction_WhileTriggered()
    {
        var jump = Make.Action("jump");
        jump.MirrorToInputMap = "gei_test_mirror";
        try
        {
            using var rig = new Rig().Add(Make.Context("C", Make.Bind(jump, Key.Space)));
            rig.Press(Key.Space).Tick();
            Input.IsActionPressed("gei_test_mirror").Should().BeTrue();
            InputMapBridge.GetMirroredStrength("gei_test_mirror").Should().Be(1f);
            rig.Release(Key.Space).Tick();
            Input.IsActionPressed("gei_test_mirror").Should().BeFalse();
        }
        finally { InputMapBridge.ReleaseAll(); if (InputMap.HasAction("gei_test_mirror")) InputMap.EraseAction("gei_test_mirror"); }
    }

    [Fact]
    public void Bridge_StrongestPlayerWins_AndNeverFeedsAnActionItsOwnInput()
    {
        var fire = Make.Action("fire");
        fire.MirrorToInputMap = "gei_test_shared";
        var echo = Make.Action("echo");
        echo.MirrorToInputMap = "gei_test_loop";
        try
        {
            using var one = new Rig().Add(Make.Context("C", Make.Bind(fire, Key.F)));
            using var two = new Rig().Add(Make.Context("C", Make.Bind(fire, Key.F), Make.Bind(echo, InputSource.ForInputMapAction("gei_test_loop"))));
            one.Press(Key.F).Tick();
            two.Press(Key.F).Tick();
            one.Release(Key.F).Tick();
            Input.IsActionPressed("gei_test_shared").Should().BeTrue("player two still holds it");
            two.Release(Key.F).Tick();
            Input.IsActionPressed("gei_test_shared").Should().BeFalse();
            InputMapBridge.GetMirroredStrength("gei_test_loop").Should().Be(0f, "an action never mirrors into what it reads");
        }
        finally
        {
            InputMapBridge.ReleaseAll();
            foreach (var n in new[] { "gei_test_shared", "gei_test_loop" }) if (InputMap.HasAction(n)) InputMap.EraseAction(n);
        }
    }

    // ------------------------------------------------------------------
    //  Project settings
    // ------------------------------------------------------------------

    [Fact]
    public void ProjectSettings_AreRegistered_AndApplied()
    {
        var system = new EnhancedInput();
        try
        {
            EnhancedInputSettings.Register();
            ProjectSettings.HasSetting(EnhancedInputSettings.Ticking).Should().BeTrue();
            ProjectSettings.SetSetting(EnhancedInputSettings.Ticking, (int)TickMode.Physics);
            ProjectSettings.SetSetting(EnhancedInputSettings.MaxPlayers, 2);
            ProjectSettings.SetSetting(EnhancedInputSettings.Config, "res://addons/godot_enhanced_input_demo/demo_input.tres");
            EnhancedInputSettings.ApplyTo(system);
            system.TickMode.Should().Be(TickMode.Physics);
            system.MaxPlayers.Should().Be(2);
            system.Configuration.Should().NotBeNull();
            system.Configuration.Name.Should().Be("demo");
        }
        finally
        {
            foreach (var name in new[] { EnhancedInputSettings.Config, EnhancedInputSettings.Ticking, EnhancedInputSettings.Gui, EnhancedInputSettings.ResetOnFocusLoss,
                         EnhancedInputSettings.Joining, EnhancedInputSettings.MaxPlayers, EnhancedInputSettings.MirrorActions })
                ProjectSettings.Clear(name);
            InputMapBridge.Enabled = true;
            system.Free();
        }
    }

    // ------------------------------------------------------------------
    //  Scene-local contexts
    // ------------------------------------------------------------------

    [Fact]
    public void ContextNode_IsActiveWhileInTheTree_AndWhileItsParentShows()
    {
        var menu = Make.Context("menu");
        using var rig = new Rig();
        var parent = new Control();
        var node = new EnhancedInputContextNode { Context = menu, Player = rig.Player, OnlyWhileParentVisible = true };
        parent.AddChild(node);
        fixture.Tree.Root.AddChild(parent);
        try
        {
            rig.Player.HasContext(menu).Should().BeTrue();
            parent.Visible = false;
            rig.Player.HasContext(menu).Should().BeFalse("hidden");
            parent.Visible = true;
            rig.Player.HasContext(menu).Should().BeTrue();
            node.Active = false;
            rig.Player.HasContext(menu).Should().BeFalse("switched off");
            node.Active = true;
            fixture.Tree.Root.RemoveChild(parent);
            rig.Player.HasContext(menu).Should().BeFalse("left the tree");
        }
        finally { parent.Free(); }
    }

    // ------------------------------------------------------------------
    //  Loop frames
    // ------------------------------------------------------------------

    [Fact]
    public void Frames_APressInAFrameWithoutPhysics_ReachesTheNextPhysicsStep_Once()
    {
        var jump = Make.Action("jump");
        var system = new EnhancedInput();
        try
        {
            var player = system.GetPlayer(0);
            player.AddContext(Make.Context("C", Make.Bind(jump, Key.Space)));
            var physics = player.PhysicsFrame;
            var process = player.ProcessFrame;

            player.FeedEvent(Rig.KeyEvent(Key.Space, true));
            system._Process(Rig.Dt);                       // frame 1: no physics step
            process.WasPressed(jump).Should().BeTrue();
            physics.WasPressed(jump).Should().BeFalse("no physics step has run yet");

            player.FeedEvent(Rig.KeyEvent(Key.Space, false));
            system._PhysicsProcess(Rig.Dt);                // frame 2: two physics steps
            physics.WasPressed(jump).Should().BeTrue("the press is not lost");
            physics.WasReleased(jump).Should().BeTrue();
            system._PhysicsProcess(Rig.Dt);
            physics.WasPressed(jump).Should().BeFalse("nor seen twice");
            system._Process(Rig.Dt);
            process.WasPressed(jump).Should().BeFalse();
            process.WasReleased(jump).Should().BeTrue();
            jump.WasReleased(InputLoop.Process, player).Should().BeTrue();
        }
        finally { system.Free(); }
    }

    // ------------------------------------------------------------------
    //  Split screen and windows
    // ------------------------------------------------------------------

    [Fact]
    public void PointerRegion_RoutesAndLocalizesPointerEvents()
    {
        var system = new EnhancedInput();
        var left = new Control { Position = Vector2.Zero, Size = new Vector2(100, 100) };
        var right = new Control { Position = new Vector2(100, 0), Size = new Vector2(100, 100) };
        fixture.Tree.Root.AddChild(left);
        fixture.Tree.Root.AddChild(right);
        try
        {
            var one = system.GetPlayer(0);
            var two = system.AddPlayer();
            one.AcceptKeyboardMouse = two.AcceptKeyboardMouse = true;
            one.PointerRegion = left;
            two.PointerRegion = right;

            system.FeedEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = new Vector2(150, 40) });
            one.Raw.GetCurrent(SourceKey.ForMouseButton(MouseButton.Left)).X.Should().Be(0f, "the click was in player two's half");
            two.Raw.GetCurrent(SourceKey.ForMouseButton(MouseButton.Left)).X.Should().Be(1f);

            system.FeedEvent(new InputEventMouseMotion { Position = new Vector2(130, 60) });
            two.Raw.MousePosition.Should().Be(new Vector2(30, 60), "in player two's own coordinates");
            one.Raw.MousePosition.Should().Be(Vector2.Zero, "the motion was not in player one's half");
        }
        finally { left.Free(); right.Free(); system.Free(); }
    }

    [Fact]
    public void AttachedWindow_SendsItsEventsToItsPlayers()
    {
        var system = new EnhancedInput();
        var window = new Window { Visible = false };
        fixture.Tree.Root.AddChild(window);
        try
        {
            var one = system.GetPlayer(0);
            var two = system.AddPlayer();
            one.AcceptKeyboardMouse = two.AcceptKeyboardMouse = true;
            system.AttachWindow(window, two);
            system.AttachedWindows.Should().Contain(window);
            var relay = window.GetChildren(includeInternal: true).OfType<WindowInputRelay>().Single();

            var press = Rig.KeyEvent(Key.K, true);
            relay._Input(press);            // as Godot does: input first, then unhandled input
            relay._UnhandledInput(press);
            two.Raw.GetCurrent(SourceKey.ForKey(Key.K)).X.Should().Be(1f);
            one.Raw.GetCurrent(SourceKey.ForKey(Key.K)).X.Should().Be(0f, "the window belongs to player two");

            system.DetachWindow(window);
            system.AttachedWindows.Should().NotContain(window);
        }
        finally { window.Free(); system.Free(); }
    }

    // ------------------------------------------------------------------
    //  Tool player and GDScript
    // ------------------------------------------------------------------

    [Fact]
    public void ToolPlayer_RunsOnItsOwn()
    {
        var snap = Make.Action("snap");
        var tool = new ToolInputPlayer();
        tool.Contexts.Add(Make.Context("tools", Make.Bind(snap, Key.Ctrl)));
        fixture.Tree.Root.AddChild(tool);
        try
        {
            tool.HasContext(tool.Contexts[0]).Should().BeTrue();
            tool.FeedEvent(Rig.KeyEvent(Key.Ctrl, true));
            tool._Process(Rig.Dt);
            tool.IsTriggered(snap).Should().BeTrue();
        }
        finally { tool.GetParent()?.RemoveChild(tool); tool.Free(); }
    }

    [Fact]
    public void GDScriptNames_AreCallableThroughGodot()
    {
        var jump = Make.Action("jump");
        var move = Make.Action("move", ActionValueKind.Axis2D);
        var system = new EnhancedInput();
        try
        {
            var player = system.GetPlayer(0);
            player.AddContext(Make.Context("C", Make.Bind(jump, Key.Space), Make.Bind(move, InputSourcePath.Parse("wasd"))));
            player.FeedEvent(Rig.KeyEvent(Key.Space, true));
            player.FeedEvent(Rig.KeyEvent(Key.D, true));
            system.Tick(Rig.Dt);

            system.Call("was_pressed", "jump").AsBool().Should().BeTrue();
            system.Call("is_triggered_for", "jump", 0).AsBool().Should().BeTrue();
            system.Call("get_vector", "move").AsVector2().Should().Be(new Vector2(1, 0));
            system.Call("was_pressed", jump).AsBool().Should().BeTrue("a resource works too");
            system.Call("consume_triggered", "jump", 0.1).AsBool().Should().BeTrue();
            system.Call("consume_triggered", "jump", 0.1).AsBool().Should().BeFalse();
            system.Call("find_action", "jump").AsGodotObject().Should().BeSameAs(jump);
            system.Call("was_pressed", "nope").AsBool().Should().BeFalse();
        }
        finally { system.Free(); }
    }

    // ------------------------------------------------------------------
    //  Review fixes
    // ------------------------------------------------------------------

    [Fact]
    public void PointerCapture_KeepsAPressWithItsPlayer_UntilReleased()
    {
        var system = new EnhancedInput();
        var left = new Control { Position = Vector2.Zero, Size = new Vector2(100, 100) };
        var right = new Control { Position = new Vector2(100, 0), Size = new Vector2(100, 100) };
        fixture.Tree.Root.AddChild(left);
        fixture.Tree.Root.AddChild(right);
        try
        {
            var one = system.GetPlayer(0);
            var two = system.AddPlayer();
            one.AcceptKeyboardMouse = two.AcceptKeyboardMouse = true;
            one.PointerRegion = left;
            two.PointerRegion = right;
            var button = SourceKey.ForMouseButton(MouseButton.Left);

            system.FeedEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = new Vector2(40, 40), ButtonMask = MouseButtonMask.Left });
            system.FeedEvent(new InputEventMouseMotion { Position = new Vector2(150, 40), ButtonMask = MouseButtonMask.Left });
            one.Raw.MousePosition.Should().Be(new Vector2(150, 40), "the drag stays with player one, in its coordinates");
            two.Raw.MousePosition.Should().Be(Vector2.Zero);
            system.FeedEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = new Vector2(150, 40) });
            one.Raw.GetCurrent(button).X.Should().Be(0f, "the release outside its half still reaches player one");
            two.Raw.GetCurrent(button).X.Should().Be(0f);

            system.FeedEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = new Vector2(150, 40), ButtonMask = MouseButtonMask.Left });
            two.Raw.GetCurrent(button).X.Should().Be(1f, "after the release, a new press goes by region again");
        }
        finally { left.Free(); right.Free(); system.Free(); }
    }

    [Fact]
    public void Bridge_ReleasesWhenMirroringStops_OrTheTargetChanges_AndTakesTheStrongestOfOnePlayer()
    {
        var fire = Make.Action("fire");
        var alt = Make.Action("alt_fire");
        fire.MirrorToInputMap = "gei_test_fire";
        alt.MirrorToInputMap = "gei_test_fire";
        try
        {
            using var rig = new Rig().Add(Make.Context("C", Make.Bind(fire, Key.F), Make.Bind(alt, Key.G)));
            rig.Press(Key.F).Tick();
            Input.IsActionPressed("gei_test_fire").Should().BeTrue();
            rig.Tick();
            Input.IsActionPressed("gei_test_fire").Should().BeTrue("the other action of the same player is idle, the strongest wins");

            rig.Player.MirrorsToInputMap = false;
            rig.Tick();
            Input.IsActionPressed("gei_test_fire").Should().BeFalse("turning mirroring off releases it");

            rig.Player.MirrorsToInputMap = true;
            rig.Tick();
            Input.IsActionPressed("gei_test_fire").Should().BeTrue();
            fire.MirrorToInputMap = "gei_test_fire2";
            rig.Tick();
            Input.IsActionPressed("gei_test_fire").Should().BeFalse("the old target is released");
            Input.IsActionPressed("gei_test_fire2").Should().BeTrue();
        }
        finally
        {
            InputMapBridge.ReleaseAll();
            foreach (var n in new[] { "gei_test_fire", "gei_test_fire2" }) if (InputMap.HasAction(n)) InputMap.EraseAction(n);
        }
    }

    [Fact]
    public void Bridge_BlocksIndirectFeedbackCycles()
    {
        var a = Make.Action("a");
        var b = Make.Action("b");
        a.MirrorToInputMap = "gei_test_y";
        b.MirrorToInputMap = "gei_test_x";
        try
        {
            using var rig = new Rig().Add(Make.Context("C",
                Make.Bind(a, InputSource.ForInputMapAction("gei_test_x")),
                Make.Bind(a, Key.A),
                Make.Bind(b, InputSource.ForInputMapAction("gei_test_y"))));
            rig.Press(Key.A).Tick().Tick();
            InputMapBridge.GetMirroredStrength("gei_test_y").Should().Be(0f, "x → a → y → b → x is a cycle");
            InputMapBridge.GetMirroredStrength("gei_test_x").Should().Be(0f);
        }
        finally
        {
            InputMapBridge.ReleaseAll();
            foreach (var n in new[] { "gei_test_x", "gei_test_y" }) if (InputMap.HasAction(n)) InputMap.EraseAction(n);
        }
    }

    [Fact]
    public void Frames_MadeAfterAPress_StillSeeIt()
    {
        var jump = Make.Action("jump");
        using var rig = new Rig().Add(Make.Context("C", Make.Bind(jump, Key.Space)));
        rig.Press(Key.Space).Tick();
        rig.Player.PhysicsFrame.WasPressed(jump).Should().BeTrue("first use comes after the tick");
    }

    [Fact]
    public void Importer_ReportsApproximations_AndOnlySkipsGodotsOwnUiActions()
    {
        var click = InputMapImporter.ToSource(new InputEventMouseButton { ButtonIndex = MouseButton.Left, ShiftPressed = true }, out var note);
        InputSourcePath.Format(click).Should().Be("key:shift + mouse:left");
        note.Should().NotBeNull();

        InputMapImporter.IsBuiltIn("ui_accept").Should().BeTrue();
        ProjectSettings.SetSetting("input/ui_my_own", new Godot.Collections.Dictionary { ["deadzone"] = 0.5f, ["events"] = new Godot.Collections.Array() });
        try { InputMapImporter.IsBuiltIn("ui_my_own").Should().BeFalse("a project's own action is imported"); }
        finally { ProjectSettings.Clear("input/ui_my_own"); }
    }

    [Fact]
    public void DetachingAWindow_ReleasesItsPlayersInput()
    {
        var system = new EnhancedInput();
        var window = new Window { Visible = false };
        fixture.Tree.Root.AddChild(window);
        try
        {
            var two = system.AddPlayer();
            two.AcceptKeyboardMouse = true;
            system.AttachWindow(window, two);
            var relay = window.GetChildren(includeInternal: true).OfType<WindowInputRelay>().Single();
            var press = Rig.KeyEvent(Key.K, true);
            relay._Input(press);
            relay._UnhandledInput(press);
            two.Raw.GetCurrent(SourceKey.ForKey(Key.K)).X.Should().Be(1f);
            system.DetachWindow(window);
            two.Raw.GetCurrent(SourceKey.ForKey(Key.K)).X.Should().Be(0f, "what the window held is released");
        }
        finally { window.Free(); system.Free(); }
    }

    [Fact]
    public void ContextNode_MovesItsContext_WhenItsPlayerChanges()
    {
        var menu = Make.Context("menu");
        using var first = new Rig();
        using var second = new Rig();
        var node = new EnhancedInputContextNode { Context = menu, Player = first.Player };
        fixture.Tree.Root.AddChild(node);
        try
        {
            first.Player.HasContext(menu).Should().BeTrue();
            node.Player = second.Player;
            first.Player.HasContext(menu).Should().BeFalse();
            second.Player.HasContext(menu).Should().BeTrue();
        }
        finally { node.GetParent()?.RemoveChild(node); node.Free(); }
    }
}
