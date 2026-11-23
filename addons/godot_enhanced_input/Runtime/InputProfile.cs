using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// One player's rebinding choices: for each rebind id, the source that replaces the binding's own.
/// A null source means "cleared" (the binding reads nothing). Ids that no current binding uses are
/// kept, so a profile survives contexts that are not loaded yet.
/// </summary>
public sealed class InputProfile
{
    /// <summary>The JSON format this version writes and the newest it reads.</summary>
    public const int FormatVersion = 1;

    private readonly Dictionary<string, InputSource> _overrides = new(StringComparer.Ordinal);

    /// <summary>Free-form profile name.</summary>
    public string Name { get; set; } = "default";

    /// <summary>Raised whenever an override is added, changed or removed.</summary>
    public event Action Changed;

    /// <summary>The rebind ids that have an override.</summary>
    public IEnumerable<string> OverriddenIds => _overrides.Keys;

    /// <summary>Number of overrides.</summary>
    public int Count => _overrides.Count;

    /// <summary>Whether an id has an override.</summary>
    public bool HasOverride(string id) => id != null && _overrides.ContainsKey(id);

    /// <summary>The override for an id. <paramref name="source"/> is null when the slot was cleared.</summary>
    public bool TryGetOverride(string id, out InputSource source)
    {
        if (id == null) { source = null; return false; }
        return _overrides.TryGetValue(id, out source);
    }

    /// <summary>Replaces the source for an id (a copy is stored). Null clears the slot.</summary>
    public void SetOverride(string id, InputSource source)
    {
        if (string.IsNullOrEmpty(id)) throw new ArgumentException("A rebind id is required.", nameof(id));
        _overrides[id] = source == null || source.Kind == SourceKind.None ? null : (InputSource)source.Duplicate();
        Changed?.Invoke();
    }

    /// <summary>Removes an id's override, so the binding's own source applies again.</summary>
    public bool RemoveOverride(string id)
    {
        if (id == null || !_overrides.Remove(id)) return false;
        Changed?.Invoke();
        return true;
    }

    /// <summary>Removes every override.</summary>
    public void Clear()
    {
        if (_overrides.Count == 0) return;
        _overrides.Clear();
        Changed?.Invoke();
    }

    /// <summary>Moves an override saved under an old id to a new id (unless the new id has one).</summary>
    internal bool Migrate(string formerId, string id)
    {
        if (formerId == null || id == null || _overrides.ContainsKey(id) || !_overrides.TryGetValue(formerId, out var source)) return false;
        _overrides.Remove(formerId);
        _overrides[id] = source;
        return true;
    }

    // ------------------------------------------------------------------
    //  JSON
    // ------------------------------------------------------------------

    /// <summary>The profile as JSON text.</summary>
    public string ToJson()
    {
        var overrides = new JsonArray();
        var ids = new List<string>(_overrides.Keys);
        ids.Sort(StringComparer.Ordinal);
        foreach (var id in ids)
            overrides.Add(new JsonObject { ["id"] = id, ["source"] = InputSourceJson.ToJson(_overrides[id]) });

        var root = new JsonObject
        {
            ["format"] = FormatVersion,
            ["name"] = Name,
            ["overrides"] = overrides,
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Reads a profile from JSON text. Returns null when the text is not a profile or was written by a
    /// newer format. Malformed entries are skipped and reported through <paramref name="warnings"/>.
    /// </summary>
    public static InputProfile FromJson(string text, List<string> warnings = null)
    {
        JsonNode root;
        try { root = JsonNode.Parse(text ?? string.Empty); }
        catch (JsonException e) { warnings?.Add($"not JSON: {e.Message}"); return null; }

        if (root is not JsonObject obj || obj["format"] is not JsonValue formatValue || !formatValue.TryGetValue<int>(out var format))
        {
            warnings?.Add("not an input profile (no format number)");
            return null;
        }
        if (format > FormatVersion || format < 1)
        {
            warnings?.Add($"profile format {format} is not supported (this version reads up to {FormatVersion})");
            return null;
        }

        var profile = new InputProfile();
        if (obj["name"] is JsonValue nameValue && nameValue.TryGetValue<string>(out var name)) profile.Name = name;

        if (obj["overrides"] is JsonArray entries)
        {
            foreach (var entry in entries)
            {
                if (entry is not JsonObject item || item["id"] is not JsonValue idValue || !idValue.TryGetValue<string>(out var id) || string.IsNullOrEmpty(id))
                {
                    warnings?.Add("skipped an entry without an id");
                    continue;
                }
                var source = InputSourceJson.FromJson(item["source"], out var error);
                if (source == null)
                {
                    warnings?.Add($"skipped '{id}': {error}");
                    continue;
                }
                profile._overrides[id] = source.Kind == SourceKind.None ? null : source;
            }
        }
        return profile;
    }

    /// <summary>Writes the profile to a file (folders are created). <c>user://</c> paths work.</summary>
    public Error Save(string path)
    {
        var folder = path.GetBaseDir();
        if (!string.IsNullOrEmpty(folder))
        {
            var absolute = ProjectSettings.GlobalizePath(folder);
            if (!DirAccess.DirExistsAbsolute(absolute))
            {
                var made = DirAccess.MakeDirRecursiveAbsolute(absolute);
                if (made != Error.Ok) return made;
            }
        }

        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        if (file == null) return FileAccess.GetOpenError();
        file.StoreString(ToJson());
        return Error.Ok;
    }

    /// <summary>Reads a profile file, or returns null if it is missing or unreadable (warnings are pushed to Godot's log).</summary>
    public static InputProfile Load(string path)
    {
        if (!FileAccess.FileExists(path)) return null;
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null) return null;

        var warnings = new List<string>();
        var profile = FromJson(file.GetAsText(), warnings);
        foreach (var warning in warnings) GD.PushWarning($"[GodotEnhancedInput] {path}: {warning}");
        return profile;
    }
}
