using System.Text.Json;
using WeightliftingCompWarmup.Core.Models;

namespace WeightliftingCompWarmup.Core.Services;

/// <summary>
/// Serialized envelope containing all user profiles and the active profile name.
/// </summary>
public class SavedAppState
{
    public string LastUsedProfileName { get; set; } = "Demo Profile";
    public bool HasSeenWelcomeHelp { get; set; }
    public string ThemePreference { get; set; } = "System";
    public List<Profile> Profiles { get; set; } = new();
}

/// <summary>
/// Manages persistence and CRUD operations for warmup profiles,
/// porting <c>ManagedSavedSettings</c> and <c>SaveSettings</c> from the original WinForms application.
/// </summary>
public class ProfileStorageService
{
    public const string ThemeSystem = "System";
    public const string ThemeDark = "Dark";
    public const string ThemeLight = "Light";

    private readonly string _storageFilePath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public List<Profile> Profiles { get; private set; } = new();
    public Profile ActiveProfile { get; private set; } = Defaults.CreateDemoProfile();
    public bool HasSeenWelcomeHelp { get; private set; }
    public string ThemePreference { get; private set; } = "System";

    public ProfileStorageService(string? storageFilePath = null)
    {
        if (string.IsNullOrWhiteSpace(storageFilePath))
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string dir = Path.Combine(appData, "WeightliftingCompWarmup");
            Directory.CreateDirectory(dir);
            _storageFilePath = Path.Combine(dir, "profiles.json");
        }
        else
        {
            string? dir = Path.GetDirectoryName(storageFilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            _storageFilePath = storageFilePath;
        }
    }

    /// <summary>
    /// Loads all saved profiles from disk, or initializes with "Demo Profile" if none exist.
    /// </summary>
    public Profile Load()
    {
        try
        {
            if (File.Exists(_storageFilePath))
            {
                string json = File.ReadAllText(_storageFilePath);
                var state = JsonSerializer.Deserialize<SavedAppState>(json, JsonOptions);
                if (state != null && state.Profiles.Count > 0)
                {
                    Profiles = state.Profiles;
                    foreach (var profile in Profiles)
                    {
                        EnsureBaselineRules(profile);
                    }
                    HasSeenWelcomeHelp = state.HasSeenWelcomeHelp;
                    ThemePreference = NormalizeThemePreference(state.ThemePreference);
                    ActiveProfile = Profiles.FirstOrDefault(p =>
                        string.Equals(p.Name, state.LastUsedProfileName, StringComparison.OrdinalIgnoreCase))
                        ?? Profiles[0];
                    return ActiveProfile;
                }
            }
        }
        catch
        {
            // Fall back to Demo Profile if file is corrupted or unreadable
        }

        return ResetAllSettings();
    }

    /// <summary>
    /// Marks the welcome help screen as seen and persists the flag to disk.
    /// </summary>
    public void MarkWelcomeHelpSeen()
    {
        if (!HasSeenWelcomeHelp)
        {
            HasSeenWelcomeHelp = true;
            Save();
        }
    }

    /// <summary>
    /// Sets and persists the app theme preference ("System", "Dark", or "Light").
    /// </summary>
    public void SetThemePreference(string? preference)
    {
        string normalized = NormalizeThemePreference(preference);
        if (!string.Equals(ThemePreference, normalized, StringComparison.Ordinal))
        {
            ThemePreference = normalized;
            Save();
        }
    }

    public static string NormalizeThemePreference(string? preference)
    {
        if (string.Equals(preference?.Trim(), "Dark", StringComparison.OrdinalIgnoreCase))
        {
            return "Dark";
        }

        if (string.Equals(preference?.Trim(), "Light", StringComparison.OrdinalIgnoreCase))
        {
            return "Light";
        }

        return "System";
    }

    /// <summary>
    /// Saves all profiles and the currently active profile name to disk.
    /// </summary>
    public void Save()
    {
        EnsureBaselineRules(ActiveProfile);
        var state = new SavedAppState
        {
            LastUsedProfileName = ActiveProfile.Name,
            HasSeenWelcomeHelp = HasSeenWelcomeHelp,
            ThemePreference = ThemePreference,
            Profiles = Profiles
        };

        string json = JsonSerializer.Serialize(state, JsonOptions);
        File.WriteAllText(_storageFilePath, json);
    }

    /// <summary>
    /// Exports all saved profiles and app settings to a formatted JSON backup string.
    /// </summary>
    public string ExportBackupJson()
    {
        foreach (var profile in Profiles)
        {
            EnsureBaselineRules(profile);
        }

        var state = new SavedAppState
        {
            LastUsedProfileName = ActiveProfile.Name,
            HasSeenWelcomeHelp = HasSeenWelcomeHelp,
            ThemePreference = ThemePreference,
            Profiles = Profiles
        };

        return JsonSerializer.Serialize(state, JsonOptions);
    }

    /// <summary>
    /// Imports profiles and settings from a JSON backup string and persists them to disk.
    /// </summary>
    public (bool Success, string? ErrorMessage, int ImportedCount) ImportBackupJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return (false, "The selected backup file is empty.", 0);
        }

        try
        {
            var state = JsonSerializer.Deserialize<SavedAppState>(json, JsonOptions);
            if (state == null || state.Profiles == null || state.Profiles.Count == 0)
            {
                return (false, "No valid Comp OWL profiles were found in the selected file.", 0);
            }

            var validProfiles = new List<Profile>();
            foreach (var profile in state.Profiles)
            {
                if (profile == null)
                {
                    continue;
                }

                profile.Name = string.IsNullOrWhiteSpace(profile.Name)
                    ? $"Imported Profile {validProfiles.Count + 1}"
                    : profile.Name.Trim();

                EnsureBaselineRules(profile);
                validProfiles.Add(profile);
            }

            if (validProfiles.Count == 0)
            {
                return (false, "No valid Comp OWL profiles were found in the selected file.", 0);
            }

            Profiles = validProfiles;
            if (!string.IsNullOrWhiteSpace(state.ThemePreference))
            {
                ThemePreference = NormalizeThemePreference(state.ThemePreference);
            }

            ActiveProfile = Profiles.FirstOrDefault(p =>
                string.Equals(p.Name, state.LastUsedProfileName, StringComparison.OrdinalIgnoreCase))
                ?? Profiles[0];

            Save();
            return (true, null, Profiles.Count);
        }
        catch
        {
            return (false, "The selected file is not a valid Comp OWL JSON backup.", 0);
        }
    }

    /// <summary>
    /// Switches the active profile by name.
    /// </summary>
    public Profile SelectProfile(string name)
    {
        var found = Profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (found != null)
        {
            ActiveProfile = found;
            EnsureBaselineRules(ActiveProfile);
            Save();
        }

        return ActiveProfile;
    }

    /// <summary>
    /// Creates and activates a new blank profile.
    /// </summary>
    public (bool Success, string? ErrorMessage, Profile Profile) AddNewProfile(string name)
    {
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return (false, "Profile name cannot be empty.", ActiveProfile);
        }

        if (Profiles.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return (false, $"A profile named '{name}' already exists.", ActiveProfile);
        }

        var created = Defaults.CreateBlankProfile(name);
        Profiles.Add(created);
        ActiveProfile = created;
        Save();
        return (true, null, created);
    }

    /// <summary>
    /// Duplicates the currently active profile under a new name.
    /// </summary>
    public (bool Success, string? ErrorMessage, Profile Profile) DuplicateActiveProfile(string newName)
    {
        newName = newName.Trim();
        if (string.IsNullOrWhiteSpace(newName))
        {
            return (false, "Profile name cannot be empty.", ActiveProfile);
        }

        if (Profiles.Any(p => string.Equals(p.Name, newName, StringComparison.OrdinalIgnoreCase)))
        {
            return (false, $"A profile named '{newName}' already exists.", ActiveProfile);
        }

        var clone = ActiveProfile.Clone(newName);
        Profiles.Add(clone);
        ActiveProfile = clone;
        Save();
        return (true, null, clone);
    }

    /// <summary>
    /// Renames the currently active profile.
    /// </summary>
    public (bool Success, string? ErrorMessage) RenameActiveProfile(string newName)
    {
        newName = newName.Trim();
        if (string.IsNullOrWhiteSpace(newName))
        {
            return (false, "Profile name cannot be empty.");
        }

        if (Profiles.Any(p => !ReferenceEquals(p, ActiveProfile) &&
                              string.Equals(p.Name, newName, StringComparison.OrdinalIgnoreCase)))
        {
            return (false, $"A profile named '{newName}' already exists.");
        }

        ActiveProfile.Name = newName;
        Save();
        return (true, null);
    }

    /// <summary>
    /// Deletes the currently active profile. If the last profile is deleted, recreates "Demo Profile".
    /// </summary>
    public Profile DeleteActiveProfile()
    {
        Profiles.Remove(ActiveProfile);
        if (Profiles.Count == 0)
        {
            var demo = Defaults.CreateDemoProfile();
            Profiles.Add(demo);
            ActiveProfile = demo;
        }
        else
        {
            ActiveProfile = Profiles[0];
        }

        Save();
        return ActiveProfile;
    }

    /// <summary>
    /// Clears all saved profiles and restores the initial "Demo Profile".
    /// Matches "Clear all settings" in the original WinForms application.
    /// </summary>
    public Profile ResetAllSettings()
    {
        var demo = Defaults.CreateDemoProfile();
        Profiles = new List<Profile> { demo };
        ActiveProfile = demo;
        ThemePreference = "System";
        Save();
        return ActiveProfile;
    }

    private static void EnsureBaselineRules(Profile profile)
    {
        if (profile.SnatchJumps.Count == 0 || !profile.SnatchJumps.ContainsKey(1))
        {
            profile.SnatchJumps.TryAdd(1, 20);
        }

        if (profile.SnatchTimes.Count == 0 || !profile.SnatchTimes.ContainsKey(1))
        {
            profile.SnatchTimes.TryAdd(1, 140);
        }

        if (profile.CjJumps.Count == 0 || !profile.CjJumps.ContainsKey(1))
        {
            profile.CjJumps.TryAdd(1, 30);
        }

        if (profile.CjTimes.Count == 0 || !profile.CjTimes.ContainsKey(1))
        {
            profile.CjTimes.TryAdd(1, 160);
        }
    }
}
