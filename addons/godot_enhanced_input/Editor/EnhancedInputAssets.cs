#if TOOLS
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Godot;

namespace GodotEnhancedInput;

/// <summary>Finds the plugin's assets (actions, mapping contexts, configs) among the project's files.</summary>
[ExcludeFromCodeCoverage]
internal static class EnhancedInputAssets
{
    /// <summary>The class names of the three assets.</summary>
    public static readonly string[] Classes = { nameof(EnhancedInputMappingContext), nameof(EnhancedInputAction), nameof(EnhancedInputConfig) };

    /// <summary>Every asset file with its class, in path order.</summary>
    public static List<(string Path, string Class)> All()
    {
        var files = new List<(string Path, string Class)>();
        Scan(EditorInterface.Singleton.GetResourceFilesystem()?.GetFilesystem(), files);
        files.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
        return files;
    }

    /// <summary>The paths of the asset files of one class.</summary>
    public static List<string> Paths(string className)
    {
        var paths = new List<string>();
        foreach (var (path, cls) in All())
            if (cls == className) paths.Add(path);
        return paths;
    }

    private static void Scan(EditorFileSystemDirectory dir, List<(string Path, string Class)> files)
    {
        if (dir == null) return;
        for (int i = 0; i < dir.GetFileCount(); i++)
        {
            var cls = ClassOf(dir.GetFilePath(i));
            if (cls != null) files.Add((dir.GetFilePath(i), cls));
        }
        for (int i = 0; i < dir.GetSubdirCount(); i++) Scan(dir.GetSubdir(i), files);
    }

    /// <summary>
    /// Which of the three assets a text resource is, from the script class its header names (the editor's
    /// file index only knows script classes of script files, not of resources); null if none.
    /// </summary>
    public static string ClassOf(string path)
    {
        if (!path.EndsWith(".tres", StringComparison.OrdinalIgnoreCase)) return null;
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null) return null;
        var header = file.GetLine();
        foreach (var cls in Classes)
            if (header.Contains($"script_class=\"{cls}\"", StringComparison.Ordinal)) return cls;
        return null;
    }
}
#endif
