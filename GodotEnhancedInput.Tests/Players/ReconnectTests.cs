using FluentAssertions;
using Godot;

namespace GodotEnhancedInput.Tests.Players;

/// <summary>DEV-7, DEV-8: pads keep their identity across connection types; doubtful returns are explicit.</summary>
[Collection("Godot")]
public class ReconnectTests
{
    // SDL-style GUIDs: bus (03 USB, 05 Bluetooth), name checksum, vendor, 0, product, 0, version, driver.
    private const string DualSenseUsb = "030000004c050000e60c000000000000";
    private const string DualSenseBluetooth = "050000004c050000e60c000000000000";
    private const string XboxSeriesUsb = "030000005e040000120b000000000000";
    private const string XboxSeriesBluetooth = "050000005e040000130b000000000000";
    private const string SwitchProUsb = "030000007e0500000920000000000000";

    private sealed class Setup : IDisposable
    {
        public readonly EnhancedInput System = new() { TickMode = TickMode.Manual };
        public readonly GameAction Jump = Make.Action("jump");
        public readonly InputPlayer P0, P1, P2;

        public Setup()
        {
            P0 = System.GetPlayer(0);
            P1 = System.AddPlayer();
            P2 = System.AddPlayer();
            foreach (var p in new[] { P0, P1, P2 }) p.AddContext(Make.Context("C", Make.Bind(Jump, InputSource.ForJoyButton(JoyButton.A))));
        }

        public void Press(int device)
        {
            System.FeedEvent(new InputEventJoypadButton { ButtonIndex = JoyButton.A, Pressed = true, Device = device });
            System.Tick(Rig.Dt);
        }

        public void Release(int device)
        {
            System.FeedEvent(new InputEventJoypadButton { ButtonIndex = JoyButton.A, Pressed = false, Device = device });
            System.Tick(Rig.Dt);
        }

        public void Dispose() => System.Free();
    }

    [Fact]
    public void Guids_YieldModels_ThatIgnoreTheConnection()
    {
        ControllerDetection.TryParseGuid(DualSenseBluetooth, out int bus, out int vendor, out int product).Should().BeTrue();
        (bus, vendor, product).Should().Be((5, 0x054C, 0x0CE6));
        ControllerDetection.ModelKey(DualSenseUsb, 0, 0).Should().Be(ControllerDetection.ModelKey(DualSenseBluetooth, 0, 0));
        ControllerDetection.ModelKey(XboxSeriesUsb, 0, 0).Should().Be(ControllerDetection.ModelKey(XboxSeriesBluetooth, 0, 0),
            "the Bluetooth Series pad has another product id, but it is the same family");
        ControllerDetection.ModelKey(DualSenseUsb, 0, 0).Should().NotBe(ControllerDetection.ModelKey(SwitchProUsb, 0, 0));
        ControllerDetection.ModelKey("030000001234000078560000000000", 0, 0).Should().Be("030000001234000078560000000000", "not a full GUID");
        ControllerDetection.ModelKey("03000000341200007856000000000000", 0, 0).Should().Be("1234:5678", "unknown pads go by their ids");
        ControllerDetection.ModelKey("__XINPUT_DEVICE__", 0, 0).Should().Be("__XINPUT_DEVICE__");
    }

    [Theory]
    [InlineData(DualSenseUsb, DualSenseBluetooth)]
    [InlineData(XboxSeriesBluetooth, XboxSeriesUsb)]
    public void SwitchingBetweenCableAndBluetooth_KeepsIdentityAndOwner(string before, string after)
    {
        using var s = new Setup();
        s.System.NotifyJoypadConnection(0, true, before, "", "Pad");
        s.System.AssignJoypad(s.P1, 0, before);
        var device = s.System.GetJoypadDevice(0);

        s.System.NotifyJoypadConnection(0, false);
        s.System.NotifyJoypadConnection(3, true, after, "", "Pad");

        s.System.GetJoypadDevice(3).Should().BeSameAs(device);
        device.Guid.Should().Be(after);
        s.P1.JoypadDevices.Should().Equal(3);
        s.Press(3);
        s.P1.IsTriggered(s.Jump).Should().BeTrue();
    }

    [Fact]
    public void TwoIdenticalPadsLost_FirstLostGetsTheFirstReturn_AndTheGameCanCorrectIt()
    {
        using var s = new Setup();
        s.System.NotifyJoypadConnection(0, true, XboxSeriesUsb, "", "Xbox");
        s.System.NotifyJoypadConnection(1, true, XboxSeriesUsb, "", "Xbox");
        var padOf1 = s.System.GetJoypadDevice(0);
        var padOf2 = s.System.GetJoypadDevice(1);
        s.System.AssignJoypad(s.P1, 0);
        s.System.AssignJoypad(s.P2, 1);

        var doubts = new List<(int Device, InputPlayer[] Candidates, InputPlayer Chosen)>();
        s.System.JoypadReconnectAmbiguous += (d, c, chosen) => doubts.Add((d, c.ToArray(), chosen));
        var lost = new List<InputPlayer>();
        s.System.PlayerDeviceLost += (p, _) => lost.Add(p);

        s.System.NotifyJoypadConnection(0, false);   // player 1's pad dies first
        s.System.NotifyJoypadConnection(1, false);
        lost.Should().Equal(s.P1, s.P2);

        s.System.NotifyJoypadConnection(5, true, XboxSeriesUsb, "", "Xbox");
        s.System.GetJoypadOwner(5).Should().Be(s.P1, "the pad lost first comes back first");
        doubts.Should().ContainSingle();
        doubts[0].Device.Should().Be(5);
        doubts[0].Candidates.Should().Equal(s.P1, s.P2);
        doubts[0].Chosen.Should().Be(s.P1);
        s.System.GetReconnectCandidates(5).Should().Equal(s.P1, s.P2);

        // It was player 2's pad after all.
        lost.Clear();
        s.System.ResolveJoypadReconnect(5, s.P2).Should().BeTrue();
        s.System.GetJoypadOwner(5).Should().Be(s.P2);
        s.System.GetJoypadDevice(5).Should().BeSameAs(padOf2);
        padOf1.IsConnected.Should().BeFalse();
        s.P1.JoypadDevices.Should().Equal(0);
        lost.Should().Equal(s.P1);
        s.System.ResolveJoypadReconnect(5, s.P0).Should().BeFalse("player 0 lost nothing");

        // The other pad returns: only player 1 is still missing one.
        doubts.Clear();
        s.System.NotifyJoypadConnection(6, true, XboxSeriesUsb, "", "Xbox");
        s.System.GetJoypadOwner(6).Should().Be(s.P1);
        s.System.GetJoypadDevice(6).Should().BeSameAs(padOf1);
        doubts.Should().BeEmpty();
    }

    [Fact]
    public void Ask_APadWithoutSerial_WaitsAndReachesNobody_UntilResolved()
    {
        using var s = new Setup();
        s.System.ReconnectWithoutSerial = ReconnectPolicy.Ask;
        s.System.NotifyJoypadConnection(0, true, DualSenseUsb, "", "DualSense");
        var pad = s.System.GetJoypadDevice(0);
        s.System.AssignJoypad(s.P1, 0);
        s.System.NotifyJoypadConnection(0, false);

        InputPlayer chosen = s.P0;
        s.System.JoypadReconnectAmbiguous += (_, c, ch) => chosen = ch;
        s.System.NotifyJoypadConnection(0, true, DualSenseUsb, "", "DualSense");
        chosen.Should().BeNull("nothing was guessed");
        s.System.IsJoypadReconnectPending(0).Should().BeTrue();
        s.System.GetJoypadOwner(0).Should().BeNull();

        s.Press(0);
        s.P0.IsTriggered(s.Jump).Should().BeFalse("player 0 takes unowned pads, but not one that may be someone's");
        s.P1.IsTriggered(s.Jump).Should().BeFalse();
        s.Release(0);

        s.System.ResolveJoypadReconnect(0, s.P1).Should().BeTrue();
        s.System.IsJoypadReconnectPending(0).Should().BeFalse();
        s.System.GetJoypadDevice(0).Should().BeSameAs(pad, "the pad takes back its identity");
        s.System.Devices.Count(d => d.Kind == InputDeviceKind.Joypad).Should().Be(1, "the stand-in record is gone");
        s.Press(0);
        s.P1.IsTriggered(s.Jump).Should().BeTrue();
        s.P0.IsTriggered(s.Jump).Should().BeFalse();
    }

    [Fact]
    public void Ask_PadsWithSerials_StillReturnOnTheirOwn()
    {
        using var s = new Setup();
        s.System.ReconnectWithoutSerial = ReconnectPolicy.Ask;
        s.System.NotifyJoypadConnection(0, true, DualSenseUsb, "SN-7", "DualSense");
        s.System.AssignJoypad(s.P2, 0);
        s.System.NotifyJoypadConnection(0, false);
        s.System.NotifyJoypadConnection(2, true, DualSenseBluetooth, "SN-7", "DualSense");
        s.System.IsJoypadReconnectPending(2).Should().BeFalse();
        s.System.GetJoypadOwner(2).Should().Be(s.P2);
    }

    [Fact]
    public void APadOfAnotherModel_TakesTheFreedIndex_ButTheMissingPadStillComesHome()
    {
        using var s = new Setup();
        s.System.NotifyJoypadConnection(0, true, DualSenseUsb, "", "DualSense");
        s.System.AssignJoypad(s.P1, 0);
        InputPlayer unassigned = null;
        s.System.JoypadUnassigned += (p, _) => unassigned = p;

        s.System.NotifyJoypadConnection(0, false);
        s.System.NotifyJoypadConnection(0, true, SwitchProUsb, "", "Pro Controller");
        s.System.GetJoypadOwner(0).Should().BeNull("a different pad never inherits the assignment");
        unassigned.Should().Be(s.P1);

        s.System.NotifyJoypadConnection(1, true, DualSenseBluetooth, "", "DualSense");
        s.System.GetJoypadOwner(1).Should().Be(s.P1, "player 1 still misses its pad, wherever it reconnects");
    }

    [Fact]
    public void UnassigningAMissingPad_StopsItComingBack()
    {
        using var s = new Setup();
        s.System.NotifyJoypadConnection(0, true, DualSenseUsb, "", "DualSense");
        s.System.AssignJoypad(s.P1, 0);
        s.System.NotifyJoypadConnection(0, false);
        s.System.UnassignJoypad(0);
        s.System.NotifyJoypadConnection(4, true, DualSenseUsb, "", "DualSense");
        s.System.GetJoypadOwner(4).Should().BeNull();
    }

    [Fact]
    public void Ownership_IsKeptByIdentity_IndicesOnlyRoute()
    {
        using var s = new Setup();
        s.System.NotifyJoypadConnection(0, true, DualSenseUsb, "SN-9", "DualSense");
        var pad = s.System.GetJoypadDevice(0);
        s.System.AssignJoypad(s.P1, 0);
        pad.Owner.Should().Be(s.P1);
        s.P1.OwnedDeviceIds.Should().Equal(pad.Id);

        s.System.NotifyJoypadConnection(0, false);
        pad.Owner.Should().Be(s.P1, "a missing pad keeps its owner");
        s.P1.OwnedDeviceIds.Should().Equal(pad.Id);

        s.System.NotifyJoypadConnection(7, true, DualSenseBluetooth, "SN-9", "DualSense");
        s.P1.OwnedDeviceIds.Should().Equal(pad.Id);
        s.P1.JoypadDevices.Should().Equal(new[] { 7 }, "the index is only how its input is routed now");

        s.System.UnassignJoypad(7);
        pad.Owner.Should().BeNull();
        s.P1.OwnedDeviceIds.Should().BeEmpty();
    }

    [Fact]
    public void BeingToldTwice_IsNotAReconnection()
    {
        using var s = new Setup();
        s.System.NotifyJoypadConnection(0, true, DualSenseUsb, "", "DualSense");
        s.System.AssignJoypad(s.P1, 0);
        int regained = 0;
        s.System.PlayerDeviceRegained += (_, _) => regained++;
        s.System.NotifyJoypadConnection(0, true, DualSenseUsb, "", "DualSense");
        s.System.GetJoypadDevice(0).ConnectionCount.Should().Be(1);
        s.System.GetJoypadOwner(0).Should().Be(s.P1);
        regained.Should().Be(0);
    }
}
