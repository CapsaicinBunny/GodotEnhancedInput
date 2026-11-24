using FluentAssertions;
using Godot;
using Xunit;

namespace GodotEnhancedInput.Tests.Core;

/// <summary>Context handles are counted: a context stays active while anything holds it.</summary>
[Collection("Godot")]
public class ContextHandleTests
{
    private static EnhancedInputMappingContext Vehicle()
    {
        var context = EnhancedInputMappingContext.Create("vehicle");
        context.Bind(EnhancedInputAction.Button("honk"), "key:h");
        return context;
    }

    [Fact]
    public void TwoHandles_ContextStaysUntilTheLastIsDisposed()
    {
        var vehicle = Vehicle();
        using var rig = new Rig();
        var first = rig.Player.PushContext(vehicle, 10);
        var second = rig.Player.PushContext(vehicle, 10);

        first.Dispose();
        rig.Player.HasContext(vehicle).Should().BeTrue("the second handle still holds it");
        first.IsActive.Should().BeFalse();
        second.IsActive.Should().BeTrue();

        second.Dispose();
        rig.Player.HasContext(vehicle).Should().BeFalse();
    }

    [Fact]
    public void DisposingTwice_ReleasesOnce()
    {
        var vehicle = Vehicle();
        using var rig = new Rig();
        var first = rig.Player.PushContext(vehicle);
        var second = rig.Player.PushContext(vehicle);

        first.Dispose();
        first.Dispose();
        rig.Player.HasContext(vehicle).Should().BeTrue();
        second.Dispose();
        rig.Player.HasContext(vehicle).Should().BeFalse();
    }

    [Fact]
    public void TwoScopes_EndingOneKeepsTheContext()
    {
        var vehicle = Vehicle();
        using var rig = new Rig();
        var driver = rig.Player.CreateScope();
        var passenger = rig.Player.CreateScope();
        driver.PushContext(vehicle);
        passenger.PushContext(vehicle);

        driver.Dispose();
        rig.Player.HasContext(vehicle).Should().BeTrue();
        passenger.Dispose();
        rig.Player.HasContext(vehicle).Should().BeFalse();
    }

    [Fact]
    public void AddedDirectly_SurvivesHandles_UntilRemoveContext()
    {
        var vehicle = Vehicle();
        using var rig = new Rig();
        rig.Player.AddContext(vehicle, 5);
        rig.Player.PushContext(vehicle).Dispose();
        rig.Player.HasContext(vehicle).Should().BeTrue("AddContext holds it too");

        var handle = rig.Player.PushContext(vehicle);
        rig.Player.AddContext(vehicle, 5);
        handle.Dispose();
        rig.Player.HasContext(vehicle).Should().BeTrue();
        rig.Player.RemoveContext(vehicle).Should().BeTrue();
        rig.Player.HasContext(vehicle).Should().BeFalse();
    }

    [Fact]
    public void PushingAnActiveContext_KeepsItsPriority()
    {
        var vehicle = Vehicle();
        using var rig = new Rig();
        using var first = rig.Player.PushContext(vehicle, 10);
        using var second = rig.Player.PushContext(vehicle, 99);
        rig.Player.GetContextPriority(vehicle).Should().Be(10);
    }

    [Fact]
    public void RemovedAnotherWay_HandlesGoInactive_AndDoNotRemoveALaterActivation()
    {
        var vehicle = Vehicle();
        using var rig = new Rig();
        var stale = rig.Player.PushContext(vehicle);
        rig.Player.RemoveContext(vehicle);
        stale.IsActive.Should().BeFalse();

        var fresh = rig.Player.PushContext(vehicle);
        stale.Dispose();
        rig.Player.HasContext(vehicle).Should().BeTrue("the stale handle belongs to the earlier activation");
        fresh.IsActive.Should().BeTrue();

        rig.Player.SwitchContext(vehicle, null);
        fresh.IsActive.Should().BeFalse();
        fresh.Dispose();
        rig.Player.HasContext(vehicle).Should().BeFalse();
    }

    [Fact]
    public void Contexts_AreReleasedTogether_AtTheNextTick()
    {
        var vehicle = Vehicle();
        var honk = vehicle.Bindings[0].Action;
        using var rig = new Rig();
        var a = rig.Player.PushContext(vehicle);
        var b = rig.Player.PushContext(vehicle);
        rig.Press(Key.H).Tick();
        rig.Player.IsTriggered(honk).Should().BeTrue();

        a.Dispose();
        rig.Tick();
        rig.Player.IsTriggered(honk).Should().BeTrue("still held by the other handle");
        b.Dispose();
        rig.Tick();
        rig.Player.IsTriggered(honk).Should().BeFalse();
    }
}
