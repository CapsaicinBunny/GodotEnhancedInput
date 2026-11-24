using FluentAssertions;
using Godot;

namespace GodotEnhancedInput.Tests.Core;

/// <summary>VAL-1: the binding validator used by the editor dock.</summary>
[Collection("Godot")]
public class ValidatorTests
{
    [Fact]
    public void CleanContexts_HaveNoWarnings()
    {
        var jump = Make.Action("jump");
        var move = Make.Action("move", ActionValueKind.Axis2D);
        var context = Make.Context("Game",
            Make.Bind(jump, Key.Space),
            Make.Bind(jump, InputSource.ForJoyButton(JoyButton.A)),
            Make.Bind(move, InputSourcePath.Parse("keys:w/s/a/d")));
        BindingValidator.Validate(new[] { context }).Should().NotContain(i => i.Severity >= IssueSeverity.Warning);
    }

    [Fact]
    public void FindsTheUsualMistakes()
    {
        var jump = Make.Action("jump");
        var fire = Make.Action("fire");
        var other = Make.Action("jump");                       // same name, different resource
        var move = Make.Action("move", ActionValueKind.Axis2D);
        var nameless = Make.Action("");
        var context = Make.Context("Game",
            Make.Bind(jump, Key.Space),
            Make.Bind(fire, Key.Space),                       // one key, two plain actions
            Make.Bind(null, Key.Q),                           // no action
            Make.Bind(other, (InputSource)null),             // no input
            Make.Bind(move, Key.D),                           // 1 axis for a 2D action
            Make.Bind(nameless, Key.N, new ChordTrigger()),   // chord to nothing
            Make.Bind(jump, Key.J, new ChordTrigger { ChordAction = Make.Action("unbound") }));
        context.Bindings[0].Rebindable("same");
        context.Bindings[1].Rebindable("same");
        context.Bindings[0].Modifiers.Add(null);

        var issues = BindingValidator.Validate(new[] { context });
        string all = string.Join("\n", issues);

        issues.Should().Contain(i => i.Severity == IssueSeverity.Error && i.BindingIndex == 2, all);
        issues.Should().Contain(i => i.Message.Contains("drives both"), all);
        issues.Should().Contain(i => i.Message.Contains("has no input"), all);
        issues.Should().Contain(i => i.Message.Contains("only the first axis"), all);
        issues.Should().Contain(i => i.Severity == IssueSeverity.Error && i.Message.Contains("chord trigger has no action"), all);
        issues.Should().Contain(i => i.Message.Contains("not bound in any"), all);
        issues.Should().Contain(i => i.Message.Contains("Rebind id \"same\""), all);
        issues.Should().Contain(i => i.Message.Contains("Empty modifier slot"), all);
        issues.Should().Contain(i => i.Message.Contains("named \"jump\""), all);
        issues.Should().Contain(i => i.Message.Contains("has no name"), all);
    }

    [Fact]
    public void TriggeredOverlaps_AndChordsAcrossContexts_AreFine()
    {
        var modifier = Make.Action("shift");
        var dash = Make.Action("dash");
        var walk = Make.Action("walk");
        var a = Make.Context("A", Make.Bind(modifier, Key.Shift));
        var b = Make.Context("B",
            Make.Bind(walk, Key.W),
            Make.Bind(dash, Key.W, new ChordTrigger { ChordAction = modifier }));
        BindingValidator.Validate(new[] { a, b }).Should().NotContain(i => i.Severity >= IssueSeverity.Warning);
    }
}
