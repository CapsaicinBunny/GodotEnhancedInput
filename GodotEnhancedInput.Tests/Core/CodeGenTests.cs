using System;
using FluentAssertions;
using Godot;
using Xunit;

namespace GodotEnhancedInput.Tests.Core;

/// <summary>Typed C# classes generated from input configs.</summary>
[Collection("Godot")]
public class CodeGenTests
{
    private const string DemoConfig = "res://addons/godot_enhanced_input_demo/demo_input.tres";
    private const string DemoClassFile = "res://addons/godot_enhanced_input_demo/DemoInput.cs";

    private static string DemoCode()
    {
        var config = ResourceLoader.Load<EnhancedInputConfig>(DemoConfig);
        config.Should().NotBeNull();
        return InputCodeGenerator.Generate(config, "DemoInput", "GodotEnhancedInput.Demo", DemoConfig);
    }

    [Theory]
    [InlineData("move_left", "MoveLeft")]
    [InlineData("Look Around", "LookAround")]
    [InlineData("2d-aim", "_2dAim")]
    [InlineData("", "Unnamed")]
    [InlineData("pick_up!", "PickUp")]
    public void Identifiers_ArePascalCase(string name, string expected)
        => InputCodeGenerator.Identifier(name).Should().Be(expected);

    [Fact]
    public void ClassName_EndsInInput()
    {
        InputCodeGenerator.ClassNameFor(new EnhancedInputConfig { Name = "game" }).Should().Be("GameInput");
        InputCodeGenerator.ClassNameFor(new EnhancedInputConfig(), "res://input/player_input.tres").Should().Be("PlayerInput");
    }

    [Fact]
    public void Members_PerActionAndContext_ResolvedById()
    {
        var jump = EnhancedInputAction.Button("jump");
        var move = EnhancedInputAction.Axis2D("move");
        var honk = EnhancedInputAction.Button("honk");
        var gameplay = EnhancedInputMappingContext.Create("gameplay");
        gameplay.Bind(jump, "key:space");
        gameplay.Bind(move, "wasd");
        var vehicle = EnhancedInputMappingContext.Create("vehicle");
        vehicle.Bind(honk, "key:h");
        var config = new EnhancedInputConfig { Name = "game" };
        config.Contexts.Add(gameplay);
        config.Contexts.Add(vehicle);

        var code = InputCodeGenerator.Generate(config, configPath: "res://game.tres");
        code.Should().StartWith(InputCodeGenerator.Marker);
        code.Should().Contain("public sealed partial class GameInput");
        code.Should().Contain("public EnhancedInputAction Jump { get; }").And.Contain("public EnhancedInputAction Honk { get; }");
        code.Should().Contain("public GameplayContext Gameplay { get; }").And.Contain("public VehicleContext Vehicle { get; }");
        code.Should().Contain("Jump = Resolve(config, \"jump\");");
        code.Should().Contain("Context = ResolveContext(config, \"vehicle\");");
        code.Should().Contain("public const string ConfigPath = \"res://game.tres\";");
        InputCodeGenerator.Generate(config, configPath: "res://game.tres").Should().Be(code, "the output is deterministic");
    }

    [Fact]
    public void ClashingNames_GetNumbered()
    {
        var a = EnhancedInputAction.Button("config");
        var b = EnhancedInputAction.Button("Config");
        var context = EnhancedInputMappingContext.Create("main");
        context.Bind(a, "key:a");
        context.Bind(b, "key:b");
        var config = new EnhancedInputConfig { Name = "x" };
        config.Contexts.Add(context);

        var code = InputCodeGenerator.Generate(config);
        code.Should().Contain("public EnhancedInputAction Config2 { get; }").And.Contain("public EnhancedInputAction Config3 { get; }");
    }

    [Fact]
    public void GeneratedDemoClass_ResolvesEveryMember()
    {
        var input = new GodotEnhancedInput.Demo.DemoInput();
        input.Config.Should().NotBeNull();
        input.Gameplay.Context.Name.Should().Be("gameplay");
        input.Gameplay.Jump.Name.Should().Be("jump");
        input.Menu.Resume.Should().BeSameAs(input.Resume);
        input.Gameplay.Move.ValueKind.Should().Be(ActionValueKind.Axis2D);
    }

    [Fact]
    public void DemoClass_IsUpToDate()
    {
        var code = DemoCode();
        var path = ProjectSettings.GlobalizePath(DemoClassFile);
        if (System.Environment.GetEnvironmentVariable("GEI_WRITE_GENERATED") == "1") System.IO.File.WriteAllText(path, code);
        System.IO.File.ReadAllText(path).Replace("\r\n", "\n").Should().Be(code, "regenerate DemoInput.cs (GEI_WRITE_GENERATED=1) after changing the demo's actions or contexts");
    }
}
