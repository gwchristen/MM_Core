using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MMCore.Models;

namespace MMCore.Services
{
    public static class SettingsService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private static readonly string AppDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MMCore");

        private static readonly string SettingsPath = Path.Combine(AppDir, "user-settings.json");
        private static readonly string TemplatesPath = Path.Combine(AppDir, "templates.json");
        private static readonly string PresetsPath = Path.Combine(AppDir, "presets.json");
        private static readonly string SequencesPath = Path.Combine(AppDir, "sequences.json");

        // ---------- User settings ----------
        public static UserSettings LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    return JsonSerializer.Deserialize<UserSettings>(json, JsonOptions) ?? new UserSettings();
                }
            }
            catch { /* log if you have a logger */ }
            return new UserSettings();
        }

        public static void SaveSettings(UserSettings settings)
        {
            Directory.CreateDirectory(AppDir);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(SettingsPath, json);
        }

        // ---------- Templates ----------
        public static List<CommandTemplate> LoadTemplates()
        {
            try
            {
                if (File.Exists(TemplatesPath))
                {
                    var json = File.ReadAllText(TemplatesPath);
                    return JsonSerializer.Deserialize<List<CommandTemplate>>(json, JsonOptions)
                           ?? new List<CommandTemplate>();
                }
            }
            catch { }
            return new List<CommandTemplate>();
        }

        public static void SaveTemplates(IEnumerable<CommandTemplate> templates)
        {
            Directory.CreateDirectory(AppDir);
            var list = new List<CommandTemplate>(templates);
            var json = JsonSerializer.Serialize(list, JsonOptions);
            File.WriteAllText(TemplatesPath, json);
        }

        // ---------- Presets ----------
        public static List<InputPreset> LoadPresets()
        {
            try
            {
                if (File.Exists(PresetsPath))
                {
                    var json = File.ReadAllText(PresetsPath);
                    return JsonSerializer.Deserialize<List<InputPreset>>(json, JsonOptions)
                           ?? new List<InputPreset>();
                }
            }
            catch { }
            return new List<InputPreset>();
        }

        public static void SavePresets(IEnumerable<InputPreset> presets)
        {
            Directory.CreateDirectory(AppDir);
            var list = new List<InputPreset>(presets);
            var json = JsonSerializer.Serialize(list, JsonOptions);
            File.WriteAllText(PresetsPath, json);
        }

        // ---------- Sequences ----------
        public static List<CommandSequence> LoadSequences()
        {
            try
            {
                if (File.Exists(SequencesPath))
                {
                    var json = File.ReadAllText(SequencesPath);
                    return JsonSerializer.Deserialize<List<CommandSequence>>(json, JsonOptions)
                           ?? new List<CommandSequence>();
                }
            }
            catch { }
            return new List<CommandSequence>();
        }

        public static void SaveSequences(IEnumerable<CommandSequence> sequences)
        {
            Directory.CreateDirectory(AppDir);
            var list = new List<CommandSequence>(sequences);
            var json = JsonSerializer.Serialize(list, JsonOptions);
            File.WriteAllText(SequencesPath, json);
        }
        //// ---------- Log Folder ----------
        public static string LogsFolder
        {
            get
            {
                var path = Path.Combine(AppDir, "Logs");
                Directory.CreateDirectory(path);
                return path;
            }
        }

    }
}