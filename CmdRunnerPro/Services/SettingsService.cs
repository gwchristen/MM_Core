
using System;
using System.IO;
using System.Text.Json;
using CmdRunnerPro.Models;

namespace CmdRunnerPro.Services
{
    public static class SettingsService
    {
        public static string AppFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CmdRunnerPro");
        public static string DataFile => Path.Combine(AppFolder, "settings.json");
        public static string LogsFolder => Path.Combine(AppFolder, "Logs");

        public static UserSettings Load()
        {
            Directory.CreateDirectory(AppFolder);
            Directory.CreateDirectory(LogsFolder);

            if (!File.Exists(DataFile))
            {
                var s = new UserSettings();

                // Seed templates
                s.Templates.AddRange(new[]
                {
                    new CommandTemplate { Name = "Set ComPort 1", Template = "START /WAIT MeterMate {username} {password} {opco} /ComPort {comport1}" },
                    new CommandTemplate { Name = "Set ComPort 2", Template = "START /WAIT MeterMate {username} {password} {opco} /ComPort {comport2}" },
                    new CommandTemplate { Name = "Demand Reset",  Template = "START /WAIT MeterMate {username} {password} {opco} /OPTOBAUDRATE 9600 /Demand" },
                    new CommandTemplate { Name = "Master Reset",  Template = "START /WAIT MeterMate {username} {password} {opco} /OPTOBAUDRATE 9600 /Master" },
                    new CommandTemplate { Name = "RDC Open",      Template = "START /WAIT MeterMate {username} {password} {opco} /OPTOBAUDRATE 9600 /COMMAND /STATE Open" },
                    new CommandTemplate { Name = "RDC Close",     Template = "START /WAIT MeterMate {username} {password} {opco} /OPTOBAUDRATE 9600 /COMMAND /STATE Close" },
                    new CommandTemplate { Name = "Program",       Template = "START /WAIT MeterMate {username} {password} {opco} /OPTOBAUDRATE 9600 /Program /PRO {program} /MID 000000000000000000 /TRD Yes" },
                });

                // Auto-detect default MeterMate WD (if installed)
                var autoDir = MeterMateService.FindInstallDirectory();
                if (!string.IsNullOrWhiteSpace(autoDir))
                    s.WorkingDirectory = autoDir;

                Save(s);
                return s;
            }

            var json = File.ReadAllText(DataFile);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var settings = JsonSerializer.Deserialize<UserSettings>(json, options) ?? new UserSettings();

            // Migration: if old plain "Password" exists, encrypt it into PasswordEnc
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("Password", out var passEl) &&
                    passEl.ValueKind == JsonValueKind.String)
                {
                    var plain = passEl.GetString();
                    if (!string.IsNullOrEmpty(plain) && string.IsNullOrEmpty(settings.PasswordEnc))
                    {
                        settings.PasswordEnc = EncryptionService.Encrypt(plain!);
                        Save(settings);
                    }
                }
            }
            catch { /* ignore migration errors */ }

            // Auto-detect WD if missing/invalid
            try
            {
                if (string.IsNullOrWhiteSpace(settings.WorkingDirectory) || !Directory.Exists(settings.WorkingDirectory))
                {
                    var autoDir = MeterMateService.FindInstallDirectory();
                    if (!string.IsNullOrWhiteSpace(autoDir))
                    {
                        settings.WorkingDirectory = autoDir;
                        Save(settings);
                    }
                }
            }
            catch { /* ignore */ }

            return settings;
        }

        public static void Save(UserSettings settings)
        {
            Directory.CreateDirectory(AppFolder);
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(DataFile, json);
        }
    }
}
