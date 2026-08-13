using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MissionControl;

/// <summary>
/// User-editable preferences, persisted as JSON in %APPDATA%\MissionControl\settings.json.
/// The file is hand-editable; anything missing or out of range falls back to a default,
/// so a corrupt file degrades to defaults instead of preventing startup.
/// </summary>
public sealed class Settings
{
    public const string DefaultHotkey = "Ctrl+Alt+M";
    public const double DefaultIntroDuration = 0.20;
    public const double DefaultOutroDuration = 0.20;

    /// <summary>Animation durations are clamped to this range (0 = instant, no morph).</summary>
    public const double MinDuration = 0.0;
    public const double MaxDuration = 2.0;

    public bool Enabled { get; set; } = true;
    public string Hotkey { get; set; } = DefaultHotkey;
    public double IntroDuration { get; set; } = DefaultIntroDuration;
    public double OutroDuration { get; set; } = DefaultOutroDuration;
    public bool StartWithWindows { get; set; }

    /// <summary>Raised after any successful <see cref="Save"/> so the app can re-apply live state.</summary>
    public event Action? Changed;

    /// <summary>True when no settings file existed at load time (first run / fresh install).</summary>
    [JsonIgnore]
    public bool IsFirstRun { get; private set; }

    [JsonIgnore]
    public HotKeySpec HotKeySpec =>
        HotKeySpec.TryParse(Hotkey, out var spec) ? spec : HotKeySpec.Parse(DefaultHotkey);

    // ---------------------------------------------------------------- persistence
    public static string Directory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MissionControl");

    public static string FilePath => Path.Combine(Directory, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // The default encoder escapes '+' to a unicode escape, which makes "Ctrl+Alt+M" hard
        // to read in a file meant to be hand-edited. None of this is ever emitted into HTML.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    public static Settings Load()
    {
        Settings settings;
        bool firstRun = false;
        try
        {
            if (File.Exists(FilePath))
                settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), JsonOptions) ?? new Settings();
            else
            {
                settings = new Settings();
                firstRun = true;
            }
        }
        catch (Exception)
        {
            // Unreadable or malformed file: start from defaults rather than failing to launch.
            settings = new Settings();
        }

        settings.IsFirstRun = firstRun;
        settings.Normalize();
        return settings;
    }

    public void Save()
    {
        Normalize();
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            // Write-then-replace so a crash mid-write can't leave a half-written file.
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception)
        {
            // Persisting preferences is best-effort; the in-memory settings still apply this session.
        }

        Changed?.Invoke();
    }

    private void Normalize()
    {
        if (!HotKeySpec.TryParse(Hotkey, out var spec))
            Hotkey = DefaultHotkey;
        else
            Hotkey = spec.ToString();   // canonical casing/order

        IntroDuration = Clamp(IntroDuration, DefaultIntroDuration);
        OutroDuration = Clamp(OutroDuration, DefaultOutroDuration);
    }

    private static double Clamp(double value, double fallback) =>
        double.IsNaN(value) ? fallback : Math.Clamp(value, MinDuration, MaxDuration);

    public void RestoreDefaults()
    {
        Enabled = true;
        Hotkey = DefaultHotkey;
        IntroDuration = DefaultIntroDuration;
        OutroDuration = DefaultOutroDuration;
    }
}
