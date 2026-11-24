using FluentAssertions;
using Godot;
using GodotEnhancedInput.Demo;

namespace GodotEnhancedInput.Tests.Demo;

/// <summary>
/// Checks that the demo's saved resources load and behave, and (when GEI_WRITE_DEMO=1) rewrites them
/// from <see cref="DemoContent"/>, so the .tres files are always produced by Godot itself.
/// </summary>
[Collection("Godot")]
public class DemoTests(EngineFixture fixture)
{
    private const string Gameplay = DemoContent.Folder + "/contexts/gameplay.tres";
    private const string Menu = DemoContent.Folder + "/contexts/menu.tres";
    private const string Scene = DemoContent.Folder + "/demo.tscn";

    [Fact]
    public void WriteDemoResources_WhenRequested()
    {
        if (System.Environment.GetEnvironmentVariable("GEI_WRITE_DEMO") != "1") return;

        var content = new DemoContent();
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(DemoContent.Folder + "/actions"));
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(DemoContent.Folder + "/contexts"));

        foreach (var action in content.AllActions)
            Save(action, $"{DemoContent.Folder}/actions/{action.Name}.tres");
        Save(content.Gameplay, Gameplay);
        Save(content.Menu, Menu);

        var root = new GodotEnhancedInput.Demo.Demo { Name = "Demo", Gameplay = content.Gameplay, Menu = content.Menu };
        var packed = new PackedScene();
        packed.Pack(root).Should().Be(Error.Ok);
        ResourceSaver.Save(packed, Scene).Should().Be(Error.Ok);
        root.Free();
    }

    private static void Save(Resource resource, string path)
    {
        ResourceSaver.Save(resource, path).Should().Be(Error.Ok);
        resource.TakeOverPath(path);
    }

    [Fact]
    public void SavedContexts_LoadWithTheirBindings()
    {
        var gameplay = ResourceLoader.Load<BindingContext>(Gameplay, cacheMode: ResourceLoader.CacheMode.Ignore);
        var menu = ResourceLoader.Load<BindingContext>(Menu, cacheMode: ResourceLoader.CacheMode.Ignore);
        var expected = new DemoContent();

        gameplay.Should().NotBeNull();
        gameplay.Bindings.Count.Should().Be(expected.Gameplay.Bindings.Count);
        menu.BlockLowerPriority.Should().BeTrue();

        var dash = gameplay.Bindings.First(b => b.Action.Name == "dash" && b.Source.Kind == SourceKind.Key);
        dash.Triggers.Should().ContainItemsAssignableTo<InputTrigger>();
        dash.Triggers.OfType<ChordTrigger>().Single().ChordAction.Name.Should().Be("sprint");
        gameplay.Bindings.First(b => b.Action.Name == "move").Action.ValueKind.Should().Be(ActionValueKind.Axis2D);
    }

    [Fact]
    public void SavedContexts_Drive_ChordTapHoldAndCombo()
    {
        var gameplay = ResourceLoader.Load<BindingContext>(Gameplay);
        GameAction Find(string name) => gameplay.Bindings.First(b => b.Action.Name == name).Action;
        using var rig = new Rig().Add(gameplay);

        rig.Press(Key.Space).Tick();
        rig.Player.WasTriggered(Find("jump")).Should().BeTrue();
        rig.Release(Key.Space).Tick();
        rig.Press(Key.Shift).Press(Key.Space).Tick();
        rig.Player.WasTriggered(Find("dash")).Should().BeTrue();
        rig.Player.WasTriggered(Find("jump")).Should().BeFalse();
        rig.Player.WasTriggered(Find("spin")).Should().BeTrue("jump then dash is the spin combo");
        rig.Release(Key.Space).Release(Key.Shift).Tick();

        rig.Press(Key.F).Tick(5).Release(Key.F).Tick();
        rig.Player.WasTriggered(Find("inspect")).Should().BeTrue();

        rig.Press(Key.F).TickFor(0.6);
        rig.Player.IsTriggered(Find("pick_up")).Should().BeFalse("one shot");
        rig.Player.GetState(Find("inspect")).Phase.Should().Be(TriggerPhase.Idle, "held too long for a tap");
    }

    [Fact]
    public void SavedContexts_SharedRebindIds_MoveTogether()
    {
        var gameplay = ResourceLoader.Load<BindingContext>(Gameplay);
        var menu = ResourceLoader.Load<BindingContext>(Menu);
        GameAction Find(BindingContext c, string name) => c.Bindings.First(b => b.Action.Name == name).Action;
        using var rig = new Rig().Add(gameplay);
        rig.Player.RegisterRebindContext(menu);

        rig.Player.GetRebindSlot("jump_key").Bindings.Should().HaveCount(2, "Jump and Dash both use Space");
        rig.Player.GetRebindSlot("pause_key").Bindings.Should().HaveCount(2, "Pause and Resume, across two contexts");

        rig.Player.Rebind("jump_key", InputSource.ForKey(Key.J));
        rig.Press(Key.Shift).Press(Key.J).Tick();
        rig.Player.WasTriggered(Find(gameplay, "dash")).Should().BeTrue();
    }

    [Fact]
    public void DemoScene_Runs_AndOpensTheMenu()
    {
        var packed = ResourceLoader.Load<PackedScene>(Scene);
        packed.Should().NotBeNull();
        var demo = packed.Instantiate<GodotEnhancedInput.Demo.Demo>();
        demo.PersistBindings = false;
        fixture.Tree.Root.AddChild(demo);
        try
        {
            var player = EnhancedInput.Instance?.GetPlayer(0) ?? demo.GetNode<EnhancedInput>("EnhancedInput").GetPlayer(0);
            player.HasContext(demo.Gameplay).Should().BeTrue();

            player.FeedEvent(Rig.KeyEvent(Key.Escape, true));
            player.Tick(Rig.Dt);
            demo._Process(Rig.Dt);
            player.HasContext(demo.Menu).Should().BeTrue();

            player.Tick(Rig.Dt);
            player.HasContext(demo.Menu).Should().BeTrue("the held Escape is ignored by the menu until released");

            player.FeedEvent(Rig.KeyEvent(Key.Escape, false));
            player.Tick(Rig.Dt);
            player.FeedEvent(Rig.KeyEvent(Key.Escape, true));
            player.Tick(Rig.Dt);
            player.HasContext(demo.Menu).Should().BeFalse();
            player.Tick(Rig.Dt);
            player.HasContext(demo.Menu).Should().BeFalse("the Esc that closed the menu is still held: gameplay does not see it as a new press");
        }
        finally
        {
            demo.GetParent().RemoveChild(demo);
            demo.Free();
        }
    }

    [Fact]
    public void DemoScene_SecondJoypadJoinsAsANewPlayer()
    {
        var demo = ResourceLoader.Load<PackedScene>(Scene).Instantiate<GodotEnhancedInput.Demo.Demo>();
        demo.PersistBindings = false;
        fixture.Tree.Root.AddChild(demo);
        var system = EnhancedInput.Instance ?? demo.GetNode<EnhancedInput>("EnhancedInput");
        try
        {
            system.JoinMode.Should().Be(JoinMode.OnButtonPress);
            system.FeedEvent(new InputEventJoypadButton { ButtonIndex = JoyButton.A, Pressed = true, Device = 11 });
            system.FeedEvent(new InputEventJoypadButton { ButtonIndex = JoyButton.A, Pressed = true, Device = 12 });

            system.Players.Should().HaveCount(2);
            system.GetJoypadOwner(11).PlayerIndex.Should().Be(0);
            var second = system.GetJoypadOwner(12);
            second.PlayerIndex.Should().Be(1);
            second.HasContext(demo.Gameplay).Should().BeTrue("the demo gives joined players the gameplay context");
        }
        finally
        {
            demo.GetParent().RemoveChild(demo);
            demo.Free();
            system.UnassignJoypad(11);
            if (GodotObject.IsInstanceValid(system)) system.JoinMode.Should().Be(JoinMode.Manual, "the demo restores the join mode");
        }
    }
}
