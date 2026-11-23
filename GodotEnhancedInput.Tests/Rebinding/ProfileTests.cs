using System.Text.Json.Nodes;
using FluentAssertions;
using Godot;

namespace GodotEnhancedInput.Tests.Rebinding;

/// <summary>MAP-6: saving and loading profiles.</summary>
[Collection("Godot")]
public class ProfileTests
{
    private static InputSource[] AllKinds() => new[]
    {
        InputSource.ForKey(Key.Space, KeyMatch.Label),
        InputSource.ForKeyAxis1D(Key.E, Key.Q),
        InputSource.ForKeyAxis2D(Key.Up, Key.Down, Key.Left, Key.Right, KeyMatch.Logical),
        InputSource.ForMouseButton(MouseButton.WheelDown),
        InputSource.ForMouseMotion(),
        InputSource.ForMousePosition(),
        InputSource.ForJoyButton(JoyButton.Start, 1),
        InputSource.ForJoyAxis(JoyAxis.TriggerRight, range: JoyAxisRange.Positive),
        InputSource.ForJoyStick(JoyStickSide.Right, 3),
        InputSource.ForInputMapAction("ui_accept"),
        InputSource.ForVirtual("touch_fire"),
    };

    [Fact]
    public void Json_RoundTripsEverySourceKind()
    {
        foreach (var source in AllKinds())
        {
            var back = InputSourceJson.FromJson(InputSourceJson.ToJson(source), out var error);
            error.Should().BeNull();
            InputSourceJson.SameInput(source, back).Should().BeTrue(source.Describe());
        }
    }

    [Fact]
    public void Json_StoresEnumsByName()
    {
        var json = InputSourceJson.ToJson(InputSource.ForKey(Key.Space)).ToJsonString();
        json.Should().Contain("\"Space\"").And.Contain("\"Physical\"");
    }

    [Fact]
    public void Profile_RoundTrips_IncludingClearedSlots()
    {
        var profile = new InputProfile { Name = "left-handed" };
        profile.SetOverride("jump_key", InputSource.ForKey(Key.J));
        profile.SetOverride("fire_pad", null);

        var back = InputProfile.FromJson(profile.ToJson());

        back.Name.Should().Be("left-handed");
        back.TryGetOverride("jump_key", out var jump).Should().BeTrue();
        jump.Key.Should().Be(Key.J);
        back.TryGetOverride("fire_pad", out var cleared).Should().BeTrue();
        cleared.Should().BeNull();
    }

    [Fact]
    public void Profile_SkipsMalformedEntries_AndKeepsUnknownIds()
    {
        const string text = """
        {
          "format": 1,
          "overrides": [
            { "id": "jump_key", "source": { "kind": "Key", "key": "NotAKey" } },
            { "source": { "kind": "Key", "key": "J" } },
            { "id": "from_a_future_context", "source": { "kind": "MouseButton", "mouseButton": "Right" } }
          ]
        }
        """;
        var warnings = new List<string>();
        var profile = InputProfile.FromJson(text, warnings);

        profile.Should().NotBeNull();
        profile.HasOverride("jump_key").Should().BeFalse();
        profile.HasOverride("from_a_future_context").Should().BeTrue();
        warnings.Should().HaveCount(2);
    }

    [Fact]
    public void Profile_RefusesNewerFormatsAndGarbage()
    {
        var warnings = new List<string>();
        InputProfile.FromJson("""{ "format": 99, "overrides": [] }""", warnings).Should().BeNull();
        InputProfile.FromJson("not json", warnings).Should().BeNull();
        InputProfile.FromJson("""{ "overrides": [] }""", warnings).Should().BeNull();
        warnings.Should().HaveCount(3);
    }

    [Fact]
    public void FormerIds_ApplySavedOverridesToRenamedBindings()
    {
        var jump = Make.Action("jump");
        var context = Make.Context("gameplay");
        var binding = context.Add(jump, InputSource.ForKey(Key.Space)).Rebindable("jump_keyboard");
        binding.RebindFormerIds = new[] { "jump_key" };

        var saved = new InputProfile();
        saved.SetOverride("jump_key", InputSource.ForKey(Key.J));

        using var rig = new Rig().Add(context);
        rig.Player.Profile = InputProfile.FromJson(saved.ToJson());

        rig.Press(Key.J).Tick();
        rig.Player.IsTriggered(jump).Should().BeTrue();
        rig.Player.Profile.HasOverride("jump_keyboard").Should().BeTrue("the entry moved to the new id");
        rig.Player.Profile.HasOverride("jump_key").Should().BeFalse();
    }

    [Fact]
    public void Player_SavesAndLoadsUserFiles()
    {
        const string path = "user://gei_tests/profile_roundtrip.json";
        var jump = Make.Action("jump");
        var context = Make.Context("gameplay");
        context.Add(jump, InputSource.ForKey(Key.Space)).Rebindable("jump_key");

        using (var rig = new Rig().Add(context))
        {
            rig.Player.Rebind("jump_key", InputSource.ForJoyAxis(JoyAxis.TriggerLeft, range: JoyAxisRange.Positive));
            rig.Player.SaveProfile(path).Should().Be(Error.Ok);
        }

        using (var rig = new Rig().Add(context))
        {
            rig.Player.LoadProfile(path).Should().BeTrue();
            rig.Player.GetRebindSlot("jump_key").CurrentSource.Describe().Should().Be("Joy TriggerLeft+");
            rig.Player.LoadProfile("user://gei_tests/missing.json").Should().BeFalse();
        }

        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));
    }

    [Fact]
    public void Json_IsVersionedAndReadable()
    {
        var profile = new InputProfile();
        profile.SetOverride("a", InputSource.ForKey(Key.A));
        var root = JsonNode.Parse(profile.ToJson()).AsObject();
        ((int)root["format"]).Should().Be(InputProfile.FormatVersion);
        root["overrides"].AsArray().Should().HaveCount(1);
    }
}
