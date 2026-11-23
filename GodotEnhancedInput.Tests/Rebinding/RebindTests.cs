using FluentAssertions;
using Godot;

namespace GodotEnhancedInput.Tests.Rebinding;

/// <summary>MAP-1, MAP-2, MAP-3, MAP-7.</summary>
[Collection("Godot")]
public class RebindTests
{
    private sealed class Setup : IDisposable
    {
        public readonly Rig Rig = new();
        public readonly GameAction Jump = Make.Action("jump");
        public readonly GameAction Fire = Make.Action("fire");
        public readonly GameAction Pause = Make.Action("pause");
        public readonly BindingContext Gameplay = Make.Context("gameplay");

        public Setup()
        {
            Gameplay.Add(Jump, InputSource.ForKey(Key.Space)).Rebindable("jump_key", "Jump", "Keyboard");
            Gameplay.Add(Jump, InputSource.ForJoyButton(JoyButton.A)).Rebindable("jump_pad", null, "Gamepad");
            Gameplay.Add(Fire, InputSource.ForKey(Key.F)).Rebindable("fire_key", "Fire", "Keyboard");
            Gameplay.Add(Pause, InputSource.ForKey(Key.Escape));
            Rig.Add(Gameplay);
        }

        public InputPlayer Player => Rig.Player;
        public void Dispose() => Rig.Dispose();
    }

    [Fact]
    public void Slots_ListRebindableBindings_WithDefaults()
    {
        using var s = new Setup();
        var slots = s.Player.GetRebindSlots();

        slots.Select(x => x.Id).Should().Equal("jump_key", "jump_pad", "fire_key");
        var jumpPad = slots[1];
        jumpPad.DisplayName.Should().Be("jump", "falls back to the action's display name");
        jumpPad.Category.Should().Be("Gamepad");
        jumpPad.Action.Should().BeSameAs(s.Jump);
        jumpPad.CurrentSource.Kind.Should().Be(SourceKind.JoyButton);
        jumpPad.IsOverridden.Should().BeFalse();
    }

    [Fact]
    public void Slots_IncludeRegisteredInactiveContexts_AndShareIds()
    {
        using var s = new Setup();
        var swim = Make.Context("swim");
        swim.Add(s.Jump, InputSource.ForKey(Key.Space)).Rebindable("jump_key");
        swim.Add(s.Fire, InputSource.ForKey(Key.G)).Rebindable("dive_key", "Dive");

        s.Player.GetRebindSlot("dive_key").Should().BeNull();
        s.Player.RegisterRebindContext(swim);
        s.Player.GetRebindSlot("dive_key").Should().NotBeNull();
        s.Player.GetRebindSlot("jump_key").Bindings.Should().HaveCount(2);
    }

    [Fact]
    public void Rebind_ChangesWhatDrivesTheAction()
    {
        using var s = new Setup();
        var changed = 0;
        s.Player.BindingsChanged += () => changed++;

        s.Player.Rebind("jump_key", InputSource.ForKey(Key.J)).Applied.Should().BeTrue();
        changed.Should().BeGreaterThan(0);

        s.Rig.Press(Key.Space).Tick();
        s.Player.IsTriggered(s.Jump).Should().BeFalse();
        s.Rig.Press(Key.J).Tick();
        s.Player.IsTriggered(s.Jump).Should().BeTrue();
        s.Gameplay.Bindings[0].Source.Key.Should().Be(Key.Space, "the resource itself is never changed");
    }

    [Fact]
    public void Rebind_AppliesToEveryContextWithTheId()
    {
        using var s = new Setup();
        var swim = Make.Context("swim");
        swim.Add(s.Jump, InputSource.ForKey(Key.Space)).Rebindable("jump_key");
        s.Player.RemoveContext(s.Gameplay);
        s.Rig.Add(swim);

        s.Player.Rebind("jump_key", InputSource.ForKey(Key.K));
        s.Rig.Press(Key.K).Tick();
        s.Player.IsTriggered(s.Jump).Should().BeTrue();
    }

    [Fact]
    public void Rebind_UnknownId_Fails()
    {
        using var s = new Setup();
        var result = s.Player.Rebind("nope", InputSource.ForKey(Key.J));
        result.Applied.Should().BeFalse();
        result.FailureReason.Should().Contain("nope");
    }

    [Fact]
    public void Rebind_ToDefault_RemovesTheOverride()
    {
        using var s = new Setup();
        s.Player.Rebind("jump_key", InputSource.ForKey(Key.J));
        s.Player.Profile.HasOverride("jump_key").Should().BeTrue();
        s.Player.Rebind("jump_key", InputSource.ForKey(Key.Space));
        s.Player.Profile.HasOverride("jump_key").Should().BeFalse();
    }

    [Fact]
    public void Clear_LeavesSlotWithoutSource()
    {
        using var s = new Setup();
        s.Player.ClearRebind("jump_key").Applied.Should().BeTrue();
        s.Player.GetRebindSlot("jump_key").CurrentSource.Should().BeNull();

        s.Rig.Press(Key.Space).Tick();
        s.Player.IsTriggered(s.Jump).Should().BeFalse();
    }

    [Fact]
    public void Reset_RestoresDefaults()
    {
        using var s = new Setup();
        s.Player.Rebind("jump_key", InputSource.ForKey(Key.J));
        s.Player.Rebind("fire_key", InputSource.ForKey(Key.R));

        s.Player.ResetRebind("jump_key").Should().BeTrue();
        s.Player.GetRebindSlot("jump_key").CurrentSource.Key.Should().Be(Key.Space);
        s.Player.GetRebindSlot("fire_key").IsOverridden.Should().BeTrue();

        s.Player.ResetAllRebinds();
        s.Player.GetRebindSlot("fire_key").CurrentSource.Key.Should().Be(Key.F);
    }

    [Fact]
    public void Conflict_IsReported_ForRebindableAndFixedBindings()
    {
        using var s = new Setup();
        var result = s.Player.Rebind("jump_key", InputSource.ForKey(Key.F));
        result.Applied.Should().BeTrue("Allow is the default policy");
        result.Conflicts.Should().ContainSingle(c => c.RebindId == "fire_key");

        var fixedConflict = s.Player.FindConflicts("jump_key", InputSource.ForKey(Key.Escape));
        fixedConflict.Should().ContainSingle(c => c.RebindId == null && c.Action == s.Pause);
    }

    [Fact]
    public void Conflict_Reject_KeepsTheOldSource()
    {
        using var s = new Setup();
        var result = s.Player.Rebind("jump_key", InputSource.ForKey(Key.F), RebindConflictPolicy.Reject);
        result.Applied.Should().BeFalse();
        s.Player.GetRebindSlot("jump_key").CurrentSource.Key.Should().Be(Key.Space);
    }

    [Fact]
    public void Conflict_Swap_GivesTheOtherSlotTheOldSource()
    {
        using var s = new Setup();
        var result = s.Player.Rebind("jump_key", InputSource.ForKey(Key.F), RebindConflictPolicy.Swap);
        result.ChangedSlots.Should().Equal("fire_key");
        s.Player.GetRebindSlot("jump_key").CurrentSource.Key.Should().Be(Key.F);
        s.Player.GetRebindSlot("fire_key").CurrentSource.Key.Should().Be(Key.Space);
    }

    [Fact]
    public void Conflict_ClearOthers_ClearsRebindableOnly()
    {
        using var s = new Setup();
        s.Player.Rebind("jump_key", InputSource.ForKey(Key.F), RebindConflictPolicy.ClearOthers);
        s.Player.GetRebindSlot("fire_key").CurrentSource.Should().BeNull();

        s.Player.Rebind("jump_key", InputSource.ForKey(Key.Escape), RebindConflictPolicy.ClearOthers);
        s.Gameplay.Bindings[3].Source.Key.Should().Be(Key.Escape, "fixed bindings are never changed");
    }

    [Fact]
    public void Conflict_Scope_SameContextVersusAll()
    {
        using var s = new Setup();
        var menu = Make.Context("menu");
        menu.Add(Make.Action("confirm"), InputSource.ForKey(Key.Enter));
        s.Player.RegisterRebindContext(menu);

        s.Player.FindConflicts("jump_key", InputSource.ForKey(Key.Enter)).Should().BeEmpty();
        s.Player.FindConflicts("jump_key", InputSource.ForKey(Key.Enter), ConflictScope.AllContexts).Should().ContainSingle();
    }

    [Fact]
    public void Conflict_OppositeHalfAxes_DoNotOverlap()
    {
        var right = InputSource.ForJoyAxis(JoyAxis.LeftX, range: JoyAxisRange.Positive);
        var left = InputSource.ForJoyAxis(JoyAxis.LeftX, range: JoyAxisRange.Negative);
        right.Overlaps(left).Should().BeFalse();
        right.Overlaps(InputSource.ForJoyAxis(JoyAxis.LeftX)).Should().BeTrue();
        right.Overlaps(InputSource.ForJoyStick(JoyStickSide.Left)).Should().BeTrue();
    }

    [Fact]
    public void Rebind_KeepsItsOwnConsumption()
    {
        using var s = new Setup();
        var menu = Make.Context("menu");
        var confirm = Make.Action("confirm");
        menu.Add(confirm, InputSource.ForKey(Key.Enter)).Rebindable("confirm_key");
        s.Rig.Add(menu, 10);

        s.Player.Rebind("confirm_key", InputSource.ForKey(Key.Space));
        s.Rig.Press(Key.Space).Tick();
        s.Player.IsTriggered(confirm).Should().BeTrue();
        s.Player.IsTriggered(s.Jump).Should().BeFalse("the rebound source is consumed like the original");
    }

    [Fact]
    public void RebindWhileHeld_CompletesAction()
    {
        using var s = new Setup();
        s.Rig.Press(Key.Space).Tick();
        s.Player.IsTriggered(s.Jump).Should().BeTrue();

        s.Player.Rebind("jump_key", InputSource.ForKey(Key.J));
        s.Rig.Tick();
        s.Player.WasCompleted(s.Jump).Should().BeTrue();
    }

    [Fact]
    public void Profiles_ArePerPlayer()
    {
        using var s = new Setup();
        using var other = new Rig().Add(s.Gameplay);
        s.Player.Rebind("jump_key", InputSource.ForKey(Key.J));

        other.Press(Key.Space).Tick();
        other.Player.IsTriggered(s.Jump).Should().BeTrue();
    }
}
