using FluentAssertions;
using Godot;

namespace GodotEnhancedInput.Tests.Core;

/// <summary>MOD-1 … MOD-13.</summary>
[Collection("Godot")]
public class ModifierTests
{
    private static Vector3 Run(InputModifier modifier, Vector3 value, double delta = 1.0 / 60.0, InputPlayer player = null, ModifierMemory memory = null)
        => modifier.Run(value, new ModifierContext(ActionValueKind.Axis3D, delta, player, memory ?? new ModifierMemory()));

    private static void Near(Vector3 actual, Vector3 expected)
    {
        actual.X.Should().BeApproximately(expected.X, 1e-4f);
        actual.Y.Should().BeApproximately(expected.Y, 1e-4f);
        actual.Z.Should().BeApproximately(expected.Z, 1e-4f);
    }

    [Fact]
    public void Deadzone_Axial_ZeroesAndRescalesPerComponent()
    {
        var dz = new DeadzoneModifier { Shape = DeadzoneShape.Axial, Lower = 0.2f, Upper = 1f };
        Near(Run(dz, new Vector3(0.1f, -0.6f, 1f)), new Vector3(0f, -0.5f, 1f));
    }

    [Fact]
    public void Deadzone_Radial_KeepsDirection()
    {
        var dz = new DeadzoneModifier { Shape = DeadzoneShape.Radial, Lower = 0.2f, Upper = 1f };
        Near(Run(dz, new Vector3(0.1f, 0.1f, 0f)), Vector3.Zero);
        Near(Run(dz, new Vector3(0.6f, 0f, 0f)), new Vector3(0.5f, 0f, 0f));
        var diagonal = Run(dz, new Vector3(0.6f, 0.6f, 0f));
        diagonal.X.Should().BeApproximately(diagonal.Y, 1e-6f);
    }

    [Fact]
    public void Deadzone_ClampsAboveUpper()
    {
        var dz = new DeadzoneModifier { Shape = DeadzoneShape.Axial, Lower = 0.1f, Upper = 0.9f };
        Near(Run(dz, new Vector3(0.95f, -1f, 0f)), new Vector3(1f, -1f, 0f));
    }

    [Fact]
    public void Scale_MultipliesPerComponent()
        => Near(Run(new ScaleModifier { Scale = new Vector3(2, -1, 0.5f) }, new Vector3(1, 2, 4)), new Vector3(2, -2, 2));

    [Fact]
    public void DeltaTimeScale_MultipliesByDelta()
        => Near(Run(new DeltaTimeScaleModifier(), new Vector3(10, 0, 0), delta: 0.5), new Vector3(5, 0, 0));

    [Fact]
    public void Negate_FlipsChosenComponents()
        => Near(Run(new NegateModifier { X = false, Y = true, Z = false }, new Vector3(1, 2, 3)), new Vector3(1, -2, 3));

    [Theory]
    [InlineData(SwizzleOrder.XYZ, 1, 2, 3)]
    [InlineData(SwizzleOrder.YXZ, 2, 1, 3)]
    [InlineData(SwizzleOrder.XZY, 1, 3, 2)]
    [InlineData(SwizzleOrder.ZYX, 3, 2, 1)]
    [InlineData(SwizzleOrder.YZX, 2, 3, 1)]
    [InlineData(SwizzleOrder.ZXY, 3, 1, 2)]
    public void Swizzle_Reorders(SwizzleOrder order, float x, float y, float z)
        => Near(Run(new SwizzleModifier { Order = order }, new Vector3(1, 2, 3)), new Vector3(x, y, z));

    [Fact]
    public void Smoothing_Average_OverLastSamples()
    {
        var smooth = new SmoothingModifier { Mode = SmoothingMode.Average, Samples = 2 };
        var memory = new ModifierMemory();
        Near(Run(smooth, new Vector3(1, 0, 0), memory: memory), new Vector3(1, 0, 0));
        Near(Run(smooth, new Vector3(3, 0, 0), memory: memory), new Vector3(2, 0, 0));
        Near(Run(smooth, new Vector3(5, 0, 0), memory: memory), new Vector3(4, 0, 0));
    }

    [Fact]
    public void Smoothing_Exponential_ApproachesInput()
    {
        var smooth = new SmoothingModifier { Mode = SmoothingMode.Exponential, Rate = 10f };
        var memory = new ModifierMemory();
        Run(smooth, Vector3.Zero, memory: memory);
        var step = Run(smooth, new Vector3(1, 0, 0), delta: 0.1, memory: memory);
        step.X.Should().BeApproximately(1f - Mathf.Exp(-1f), 1e-4f);
    }

    [Fact]
    public void Smoothing_MemoryIsPerInstance()
    {
        var smooth = new SmoothingModifier { Mode = SmoothingMode.Average, Samples = 4 };
        Run(smooth, new Vector3(4, 0, 0), memory: new ModifierMemory());
        Near(Run(smooth, new Vector3(1, 0, 0), memory: new ModifierMemory()), new Vector3(1, 0, 0));
    }

    [Fact]
    public void ExponentCurve_KeepsSign()
        => Near(Run(new ExponentCurveModifier { Exponent = new Vector3(2, 3, 1) }, new Vector3(-0.5f, 0.5f, -0.25f)),
            new Vector3(-0.25f, 0.125f, -0.25f));

    [Fact]
    public void Curve_SampledAtMagnitude_SignKept()
    {
        var curve = new Curve();
        curve.AddPoint(new Vector2(0f, 0f));
        curve.AddPoint(new Vector2(1f, 0.5f));
        var modifier = new CurveModifier { CurveX = curve };
        Near(Run(modifier, new Vector3(1f, 0.7f, 0f)), new Vector3(0.5f, 0.7f, 0f));
        Near(Run(modifier, new Vector3(-1f, 0f, 0f)), new Vector3(-0.5f, 0f, 0f));
    }

    [Fact]
    public void Normalize_UnitLength_TinyBecomesZero()
    {
        Near(Run(new NormalizeModifier(), new Vector3(3, 4, 0)), new Vector3(0.6f, 0.8f, 0));
        Near(Run(new NormalizeModifier { MinLength = 0.1f }, new Vector3(0.05f, 0, 0)), Vector3.Zero);
        Near(Run(new NormalizeModifier { OnlyIfLonger = true }, new Vector3(0.5f, 0, 0)), new Vector3(0.5f, 0, 0));
    }

    [Fact]
    public void Clamp_PerComponent()
        => Near(Run(new ClampModifier { Min = new Vector3(-1, 0, 0), Max = new Vector3(1, 0.5f, 0) }, new Vector3(-2, 2, 1)),
            new Vector3(-1, 0.5f, 0));

    [Fact]
    public void WorldAxes_GroundPlane_UpBecomesForward()
    {
        Near(Run(new WorldAxesModifier(), new Vector3(0.5f, -1f, 0f)), new Vector3(0.5f, 0f, -1f));
        Near(Run(new WorldAxesModifier { Plane = WorldPlane.UprightXY }, new Vector3(0.5f, -1f, 0f)), new Vector3(0.5f, 1f, 0f));
    }

    [Fact]
    public void ViewRelative_RotatesByViewYaw()
    {
        using var rig = new Rig();
        // View turned 90° to the left: forward (−Z) now points to −X.
        rig.Player.ViewBasisOverride = new Basis(Vector3.Up, Mathf.Pi / 2f) * new Basis(Vector3.Right, -0.4f);
        Near(Run(new ViewRelativeModifier(), new Vector3(0, 0, -1), player: rig.Player), new Vector3(-1, 0, 0));
    }

    [Fact]
    public void ViewRelative_FullBasis_WhenNotYawOnly()
    {
        using var rig = new Rig();
        rig.Player.ViewBasisOverride = new Basis(Vector3.Right, Mathf.Pi / 2f);
        Near(Run(new ViewRelativeModifier { YawOnly = false }, new Vector3(0, 0, -1), player: rig.Player), new Vector3(0, 1, 0));
    }

    [Fact]
    public void FovScale_ScalesByFieldOfView()
    {
        using var rig = new Rig();
        rig.Player.ViewFovOverride = 37.5f;
        float expected = Mathf.Tan(Mathf.DegToRad(18.75f)) / Mathf.Tan(Mathf.DegToRad(37.5f));
        Near(Run(new FovScaleModifier { ReferenceFov = 75f }, new Vector3(1, 1, 0), player: rig.Player), new Vector3(expected, expected, 0));
        Near(Run(new FovScaleModifier(), new Vector3(1, 1, 0)), new Vector3(1, 1, 0));
    }

    [Fact]
    public void DisabledModifier_PassesThrough()
        => Near(Run(new ScaleModifier { Scale = Vector3.Zero, Enabled = false }, new Vector3(1, 2, 3)), new Vector3(1, 2, 3));
}
