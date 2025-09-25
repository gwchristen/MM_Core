
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using CmdRunnerPro.Models;

namespace CmdRunnerPro.Services
{
    public static class ExportImportService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
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
            return JsonSerializer.Deserialize<List<CommandTemplate>>(json, JsonOptions) ?? new List<CommandTemplate>();
        }

        public static void ExportPresets(IEnumerable<InputPreset> presets, string path, bool includeEncryptedPasswords)
        {
            var list = new List<InputPreset>();
            foreach (var p in presets)
            {
                list.Add(new InputPreset
                {
                    Name = p.Name,
                    WorkingDirectory = p.WorkingDirectory,
                    Com1 = p.Com1,
                    Com2 = p.Com2,
                    Username = p.Username,
                    PasswordEnc = includeEncryptedPasswords ? p.PasswordEnc : "",
                    Opco = p.Opco,
                    Program = p.Program
                });
            }
            var json = JsonSerializer.Serialize(list, JsonOptions);
            File.WriteAllText(path, json);
        }

        public static List<InputPreset> ImportPresets(string path)
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<InputPreset>>(json, JsonOptions) ?? new List<InputPreset>();
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
            return JsonSerializer.Deserialize<List<CommandSequence>>(json, JsonOptions) ?? new List<CommandSequence>();
        }
    }
}
