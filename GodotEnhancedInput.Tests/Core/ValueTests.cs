using FluentAssertions;
using Godot;

namespace GodotEnhancedInput.Tests.Core;

/// <summary>ACT-1, ACT-2.</summary>
[Collection("Godot")]
public class ValueTests
{
    [Fact]
    public void GameAction_Defaults_AreSensible()
    {
        var action = new EnhancedInputAction();
        action.ValueKind.Should().Be(ActionValueKind.Digital);
        action.Accumulation.Should().Be(ValueAccumulation.Strongest);
        action.ConsumeInput.Should().BeTrue();
        action.TriggerWhenPaused.Should().BeFalse();
        action.Modifiers.Should().BeEmpty();
        action.Triggers.Should().BeEmpty();
    }

    [Fact]
    public void GameAction_DisplayName_FallsBackToName()
    {
        var action = EnhancedInputAction.Create("Jump");
        action.GetDisplayName().Should().Be("Jump");
        action.DisplayName = "Leap";
        action.GetDisplayName().Should().Be("Leap");
    }

    [Theory]
    [InlineData(ActionValueKind.Digital, 5f, 0f, 0f)]
    [InlineData(ActionValueKind.Axis1D, 3f, 0f, 0f)]
    [InlineData(ActionValueKind.Axis2D, 3f, 4f, 0f)]
    [InlineData(ActionValueKind.Axis3D, 3f, 4f, 0f)]
    public void Shape_KeepsOnlyTheKindsComponents(ActionValueKind kind, float x, float y, float z)
    {
        ActionValue.Shape(kind, new Vector3(3f, 4f, 0f)).Should().Be(new Vector3(x, y, z));
    }

    [Fact]
    public void Digital_HoldsStrength_AndReadsTrueWhenNonZero()
    {
        var value = ActionValue.From(ActionValueKind.Digital, new Vector3(0.3f, 0f, 0f));
        value.AsBool().Should().BeTrue();
        value.AsFloat().Should().BeApproximately(0.3f, 1e-6f);
        ActionValue.Zero(ActionValueKind.Digital).AsBool().Should().BeFalse();
    }

    [Fact]
    public void Conversions_ReadTheRightComponents()
    {
        var value = new ActionValue(ActionValueKind.Axis3D, new Vector3(1f, 2f, 3f));
        value.AsFloat().Should().Be(1f);
        value.AsVector2().Should().Be(new Vector2(1f, 2f));
        value.AsVector3().Should().Be(new Vector3(1f, 2f, 3f));
        ((Vector2)value).Should().Be(new Vector2(1f, 2f));
        value.Magnitude.Should().BeApproximately(Mathf.Sqrt(14f), 1e-5f);
    }

    [Fact]
    public void Equality_ComparesKindAndComponents()
    {
        var a = new ActionValue(ActionValueKind.Axis2D, new Vector3(1f, 2f, 0f));
        (a == new ActionValue(ActionValueKind.Axis2D, new Vector3(1f, 2f, 0f))).Should().BeTrue();
        (a == new ActionValue(ActionValueKind.Axis3D, new Vector3(1f, 2f, 0f))).Should().BeFalse();
    }
}
