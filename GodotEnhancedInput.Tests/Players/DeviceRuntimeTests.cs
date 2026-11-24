using FluentAssertions;
using Godot;

namespace GodotEnhancedInput.Tests.Players;

/// <summary>DEV-5 … DEV-10: device identity, reconnection, switching, fallback, controller families.</summary>
[Collection("Godot")]
public class DeviceRuntimeTests
{
    private sealed class ButtonBackend : InputBackend
    {
        public override string Name => "buttons";
        public override void Attached() => AddDevice("box", 0, "Button Box", "box-guid");
        public void Press(float v) => SetControl("box", 0, "fire", v);
    }

    [Fact]
    public void Reconnecting_UnderAnotherIndex_KeepsIdentityAndOwner()
    {
        var system = new EnhancedInput { TickMode = TickMode.Manual };
        try
        {
            var p1 = system.AddPlayer();
            system.NotifyJoypadConnection(0, true, "pad-guid", "SN-1", "Xbox Wireless Controller");
            var device = system.GetJoypadDevice(0);
            system.AssignJoypad(p1, 0, "pad-guid");
            string id = device.Id;

            string lost = null, regained = null;
            system.PlayerDeviceLost += (p, d) => lost = d;
            system.PlayerDeviceRegained += (p, d) => regained = d;
            system.NotifyJoypadConnection(0, false);
            lost.Should().Be(id, "player 2 lost its only pad");
            device.IsConnected.Should().BeFalse();

            system.NotifyJoypadConnection(2, true, "pad-guid", "SN-1", "Xbox Wireless Controller");
            system.GetJoypadDevice(2).Should().BeSameAs(device, "the same pad, recognised by its serial");
            device.Id.Should().Be(id);
            device.Index.Should().Be(2);
            device.PreviousIndex.Should().Be(0);
            p1.JoypadDevices.Should().Equal(2);
            system.GetJoypadOwner(2).Should().Be(p1);
            regained.Should().Be(id);
            device.ConnectionCount.Should().Be(2);
        }
        finally { system.Free(); }
    }

    [Fact]
    public void IdenticalPads_GetSeparateIdentities_AndReturnToTheirOwners()
    {
        var system = new EnhancedInput { TickMode = TickMode.Manual };
        try
        {
            var p0 = system.GetPlayer(0);
            var p1 = system.AddPlayer();
            system.NotifyJoypadConnection(0, true, "same-guid", "", "Generic Pad");
            system.NotifyJoypadConnection(1, true, "same-guid", "", "Generic Pad");
            var a = system.GetJoypadDevice(0);
            var b = system.GetJoypadDevice(1);
            a.Id.Should().NotBe(b.Id);
            system.AssignJoypad(p0, 0, "same-guid");
            system.AssignJoypad(p1, 1, "same-guid");

            system.NotifyJoypadConnection(1, false);
            system.NotifyJoypadConnection(1, true, "same-guid", "", "Generic Pad");
            system.GetJoypadDevice(1).Should().BeSameAs(b, "same identity at the same index");
            system.GetJoypadOwner(1).Should().Be(p1);
            system.GetJoypadOwner(0).Should().Be(p0);
        }
        finally { system.Free(); }
    }

    [Fact]
    public void ADifferentPad_DoesNotInheritAnAssignment()
    {
        var system = new EnhancedInput { TickMode = TickMode.Manual };
        try
        {
            var p1 = system.AddPlayer();
            system.NotifyJoypadConnection(0, true, "pad-a", "", "Pad A");
            system.AssignJoypad(p1, 0, "pad-a");
            system.NotifyJoypadConnection(0, false);
            system.NotifyJoypadConnection(0, true, "pad-b", "", "Pad B");
            system.GetJoypadOwner(0).Should().BeNull();
            system.GetJoypadDevice(0).Name.Should().Be("Pad B");
        }
        finally { system.Free(); }
    }

    [Fact]
    public void Switching_BetweenPadKeyboardAndBackendDevices()
    {
        var system = new EnhancedInput { TickMode = TickMode.Manual };
        var backend = new ButtonBackend();
        try
        {
            var player = system.GetPlayer(0);
            system.NotifyJoypadConnection(0, true, "ps-guid", "", "DualSense Wireless Controller");
            var switches = new List<string>();
            player.DeviceSwitched += (from, to) => switches.Add(to?.Id ?? "none");

            system.FeedEvent(new InputEventJoypadButton { ButtonIndex = JoyButton.A, Pressed = true, Device = 0 });
            player.CurrentDevice.Family.Should().Be(ControllerFamily.PlayStation5);
            player.JoypadStyle.Should().Be(JoypadStyle.PlayStation);
            system.FeedEvent(Rig.KeyEvent(Key.W, true));
            player.CurrentDevice.Kind.Should().Be(InputDeviceKind.KeyboardMouse);

            system.AddBackend(backend);
            backend.Press(1f);
            system.Tick(Rig.Dt);
            player.CurrentDevice.Should().BeSameAs(system.GetCustomDevice("box", 0));
            player.CurrentDevice.Id.Should().Be("box:box-guid", "backend devices are known by their hardware identity");
            player.LastDeviceKind.Should().Be(InputDeviceKind.Custom);

            switches.Should().Equal(system.GetJoypadDevice(0).Id, "keyboard_mouse", "box:box-guid");
        }
        finally { system.Free(); }
    }

    [Fact]
    public void LosingTheCurrentPad_FallsBackToAnotherDevice()
    {
        var system = new EnhancedInput { TickMode = TickMode.Manual };
        try
        {
            var player = system.GetPlayer(0);
            system.NotifyJoypadConnection(0, true, "g1", "", "Xbox Controller");
            system.FeedEvent(new InputEventJoypadButton { ButtonIndex = JoyButton.A, Pressed = true, Device = 0 });
            player.LastDeviceKind.Should().Be(InputDeviceKind.Joypad);

            var kinds = new List<InputDeviceKind>();
            player.InputDeviceChanged += (kind, _) => kinds.Add(kind);
            system.NotifyJoypadConnection(0, false);
            player.LastDeviceKind.Should().Be(InputDeviceKind.KeyboardMouse, "player 0 listens to the keyboard");
            player.CurrentDevice.Id.Should().Be("keyboard_mouse");
            kinds.Should().Equal(InputDeviceKind.KeyboardMouse);
        }
        finally { system.Free(); }
    }

    [Theory]
    [InlineData("Steam Deck", 0, 0, ControllerFamily.SteamDeck)]
    [InlineData("whatever", 0x28DE, 0x1205, ControllerFamily.SteamDeck)]
    [InlineData("Steam Controller", 0, 0, ControllerFamily.SteamController)]
    [InlineData("Steam Virtual Gamepad", 0, 0, ControllerFamily.SteamVirtual)]
    [InlineData("PS5 Controller", 0, 0, ControllerFamily.PlayStation5)]
    [InlineData("whatever", 0x054C, 0x0DF2, ControllerFamily.PlayStation5Edge)]
    [InlineData("PS4 Controller", 0, 0, ControllerFamily.PlayStation4)]
    [InlineData("Xbox Elite Wireless Controller Series 2", 0, 0, ControllerFamily.XboxElite)]
    [InlineData("whatever", 0x045E, 0x0B12, ControllerFamily.XboxSeries)]
    [InlineData("Xbox 360 Controller", 0, 0, ControllerFamily.Xbox360)]
    [InlineData("Nintendo Switch Pro Controller", 0, 0, ControllerFamily.SwitchPro)]
    [InlineData("Joy-Con (L)", 0, 0, ControllerFamily.JoyConLeft)]
    [InlineData("whatever", 0x057E, 0x2007, ControllerFamily.JoyConRight)]
    [InlineData("Logitech Dual Action", 0, 0, ControllerFamily.Generic)]
    public void Families_AreRecognised(string name, int vendor, int product, ControllerFamily family)
        => ControllerDetection.Detect(name, vendor, product).Should().Be(family);

    [Fact]
    public void Families_HaveFeaturesAndPromptStyles()
    {
        ControllerDetection.HardwareFeatures(ControllerFamily.PlayStation5).Should().HaveFlag(ControllerFeatures.AdaptiveTriggers).And.HaveFlag(ControllerFeatures.Touchpad);
        ControllerDetection.HardwareFeatures(ControllerFamily.SteamDeck).Should().HaveFlag(ControllerFeatures.Trackpads).And.HaveFlag(ControllerFeatures.BackButtons);
        ControllerDetection.HardwareFeatures(ControllerFamily.XboxElite).Should().HaveFlag(ControllerFeatures.TriggerRumble).And.HaveFlag(ControllerFeatures.BackButtons);
        ControllerDetection.StyleOf(ControllerFamily.SteamDeck).Should().Be(JoypadStyle.SteamDeck);
        ControllerDetection.DisplayName(ControllerFamily.PlayStation5).Should().Be("DualSense");

        InputPrompts.JoyButtonName(JoyButton.Paddle2, JoypadStyle.SteamDeck).Should().Be("L4");
        InputPrompts.JoyButtonName(JoyButton.LeftShoulder, JoypadStyle.SteamDeck).Should().Be("L1");
        InputPrompts.JoyButtonName(JoyButton.A, JoypadStyle.SteamDeck).Should().Be("A");
        InputPrompts.JoyButtonName(JoyButton.Paddle3, JoypadStyle.Xbox).Should().Be("P2");
        InputPrompts.JoyButtonName(JoyButton.Paddle1, JoypadStyle.PlayStation).Should().Be("Right Back Button");
        InputPrompts.JoyAxisName(JoyAxis.TriggerRight, style: JoypadStyle.SteamDeck).Should().Be("R2");
        InputPrompts.DetectStyle("Steam Deck").Should().Be(JoypadStyle.SteamDeck);
    }

    [Fact]
    public void RegisteredPads_CarryFamilyAndDisplayName()
    {
        var system = new EnhancedInput { TickMode = TickMode.Manual };
        try
        {
            system.NotifyJoypadConnection(4, true, "deck", "", "Steam Deck Controller");
            var deck = system.GetJoypadDevice(4);
            deck.Family.Should().Be(ControllerFamily.SteamDeck);
            deck.DisplayName.Should().Be("Steam Deck");
            deck.Style.Should().Be(JoypadStyle.SteamDeck);
            deck.Features.Should().HaveFlag(ControllerFeatures.BackButtons);
        }
        finally { system.Free(); }
    }
}
