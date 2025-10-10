using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MMCore.Models;

namespace MMCore.Services
{
    public static class ExportImportService
    {
        // Tidy JSON, ignore nulls if any are present in DTOs
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public static void ExportTemplates(IEnumerable<CommandTemplate> templates, string path)
        {
            var list = new List<CommandTemplate>(templates);
            var json = JsonSerializer.Serialize(list, JsonOptions);
            File.WriteAllText(path, json);
        }

        public static List<CommandTemplate> ImportTemplates(string path)
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<CommandTemplate>>(json, JsonOptions)
                   ?? new List<CommandTemplate>();
        }

        /// <summary>
        /// Exports presets WITHOUT any password field (safe route).
        /// The includeEncryptedPasswords parameter is intentionally ignored for safety.
        /// </summary>
        public static void ExportPresets(IEnumerable<InputPreset> presets, string path, bool includeEncryptedPasswords /* ignored */)
        {
            var list = new List<ExportPresetDto>();
            foreach (var p in presets)
            {
                list.Add(new ExportPresetDto
                {
                    Name = p.Name,
                    WorkingDirectory = p.WorkingDirectory,
                    Com1 = p.Com1,
                    Com2 = p.Com2,
                    Username = p.Username,
                    Opco = p.Opco,
                    Program = p.Program,
                    TemplateName = p.TemplateName
                    // NOTE: No password written by design.
                });
            }

            var json = JsonSerializer.Serialize(list, JsonOptions);
            File.WriteAllText(path, json);
        }

        public static List<InputPreset> ImportPresets(string path)
        {
            var json = File.ReadAllText(path);

            // Import directly into your model. If a legacy file contained a password field
            // that matches the property name in InputPreset (e.g., PasswordPlain), it will be
            // populated automatically by System.Text.Json. New exports won't include it.
            return JsonSerializer.Deserialize<List<InputPreset>>(json, JsonOptions)
                   ?? new List<InputPreset>();
        }

        public static void ExportSequences(IEnumerable<CommandSequence> sequences, string path)
        {
            var list = new List<CommandSequence>(sequences);
            var json = JsonSerializer.Serialize(list, JsonOptions);
            File.WriteAllText(path, json);
        }

        public static List<CommandSequence> ImportSequences(string path)
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<CommandSequence>>(json, JsonOptions)
                   ?? new List<CommandSequence>();
        }

        // Internal DTO used only for export to ensure no password is serialized.
        private sealed class ExportPresetDto
        {
            public string? Name { get; set; }
            public string? WorkingDirectory { get; set; }
            public string? Com1 { get; set; }
            public string? Com2 { get; set; }
            public string? Username { get; set; }
            public string? Opco { get; set; }
            public string? Program { get; set; }
            public string? TemplateName { get; set; }
        }
    }
}