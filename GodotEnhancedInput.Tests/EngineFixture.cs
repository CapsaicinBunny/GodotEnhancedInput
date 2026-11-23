using System.Reflection;
using Godot;
using Engine = twodog.Engine;

namespace GodotEnhancedInput.Tests;

/// <summary>Starts one headless Godot (through 2dog) for the whole test run.</summary>
public sealed class EngineFixture : IDisposable
{
    private readonly Engine _engine;
    private readonly GodotInstance _godot;

    public SceneTree Tree => _engine.Tree;

    public EngineFixture()
    {
        var projectDir = Assembly
            .GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "GodotProjectDir")
            ?.Value ?? "..";

        _engine = new Engine(
            "GodotEnhancedInputTests",
            projectDir,
            "--headless",
            "--rendering-driver", "dummy",
            "--audio-driver", "Dummy");
        _godot = _engine.Start();
    }

    public void Dispose()
    {
        _godot.Dispose();
        _engine.Dispose();
    }
}

[CollectionDefinition("Godot", DisableParallelization = true)]
public sealed class GodotCollection : ICollectionFixture<EngineFixture> { }
