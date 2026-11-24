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
    private readonly Dictionary<string, double> _settings = new(StringComparer.Ordinal);

    /// <summary>Free-form profile name.</summary>
    public string Name { get; set; } = "default";

    /// <summary>Raised whenever an override is added, changed or removed.</summary>
    public event Action Changed
    {
        add => _changed += value;
        remove => _changed -= value;
    }

    private Action _changed;

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
        RaiseChanged();
    }

    /// <summary>Removes an id's override, so the binding's own source applies again.</summary>
    public bool RemoveOverride(string id)
    {
        if (id == null || !_overrides.Remove(id)) return false;
        RaiseChanged();
        return true;
    }

    /// <summary>Removes every override.</summary>
    public void Clear()
    {
        if (_overrides.Count == 0) return;
        _overrides.Clear();
        RaiseChanged();
    }

    // ------------------------------------------------------------------
    //  Settings (sensitivity, inversion, accessibility…)
    // ------------------------------------------------------------------

    /// <summary>The names of the settings this profile stores.</summary>
    public IEnumerable<string> SettingNames => _settings.Keys;

    /// <summary>A numeric setting, or <paramref name="fallback"/> when it is not set.</summary>
    public double GetSetting(string name, double fallback = 0)
        => name != null && _settings.TryGetValue(name, out var v) ? v : fallback;

    /// <summary>A yes/no setting (stored as 1 or 0).</summary>
    public bool GetFlag(string name, bool fallback = false)
        => name != null && _settings.TryGetValue(name, out var v) ? v != 0 : fallback;

    /// <summary>Whether a setting is stored.</summary>
    public bool HasSetting(string name) => name != null && _settings.ContainsKey(name);

    /// <summary>Stores a numeric setting.</summary>
    public void SetSetting(string name, double value)
    {
        if (string.IsNullOrEmpty(name)) throw new ArgumentException("A setting name is required.", nameof(name));
        _settings[name] = value;
        RaiseChanged();
    }

    /// <summary>Stores a yes/no setting.</summary>
    public void SetFlag(string name, bool value) => SetSetting(name, value ? 1 : 0);

    /// <summary>Removes a setting, so its default applies again.</summary>
    public bool RemoveSetting(string name)
    {
        if (name == null || !_settings.Remove(name)) return false;
        RaiseChanged();
        return true;
    }

    /// <summary>Copies another profile's overrides and settings over this one's (a preset), as one change.</summary>
    public void Apply(InputProfile preset, bool replace = false)
    {
        if (preset == null) return;
        if (replace) { _overrides.Clear(); _settings.Clear(); }
        foreach (var pair in preset._overrides) _overrides[pair.Key] = pair.Value == null ? null : (InputSource)pair.Value.Duplicate();
        foreach (var pair in preset._settings) _settings[pair.Key] = pair.Value;
        RaiseChanged();
    }

    /// <summary>
    /// Runs several changes as one: <see cref="Changed"/> is raised once at the end (if anything changed),
    /// so listeners never see a half-applied swap.
    /// </summary>
    public void Batch(Action changes)
    {
        _batchDepth++;
        try { changes?.Invoke(); }
        finally
        {
            _batchDepth--;
            if (_batchDepth == 0 && _batchDirty) { _batchDirty = false; _changed?.Invoke(); }
        }
    }

    private int _batchDepth;

    private void RaiseChanged()
    {
        if (_batchDepth > 0) _batchDirty = true;
        else _changed?.Invoke();
    }
    private bool _batchDirty;

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

        var settings = new JsonObject();
        var names = new List<string>(_settings.Keys);
        names.Sort(StringComparer.Ordinal);
        foreach (var name in names) settings[name] = _settings[name];

        var root = new JsonObject
        {
            ["format"] = FormatVersion,
            ["name"] = Name,
            ["overrides"] = overrides,
            ["settings"] = settings,
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

        if (obj["settings"] is JsonObject stored)
        {
            foreach (var pair in stored)
            {
                if (pair.Value is JsonValue value && value.TryGetValue<double>(out var number)) profile._settings[pair.Key] = number;
                else if (pair.Value is JsonValue flag && flag.TryGetValue<bool>(out var yes)) profile._settings[pair.Key] = yes ? 1 : 0;
                else warnings?.Add($"skipped setting '{pair.Key}': not a number");
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

        // Write a temporary file, then move it over the old one, so a crash never leaves half a profile.
        string temporary = path + ".tmp";
        using (var file = FileAccess.Open(temporary, FileAccess.ModeFlags.Write))
        {
            if (file == null) return FileAccess.GetOpenError();
            file.StoreString(ToJson());
        }
        string target = ProjectSettings.GlobalizePath(path);
        string source = ProjectSettings.GlobalizePath(temporary);
        var moved = DirAccess.RenameAbsolute(source, target);
        if (moved != Error.Ok && FileAccess.FileExists(target))
        {
            DirAccess.RemoveAbsolute(target);
            moved = DirAccess.RenameAbsolute(source, target);
        }
        return moved;
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
