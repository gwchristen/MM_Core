using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using CmdRunnerPro.Models;
using CmdRunnerPro.Services;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CmdRunnerPro.ViewModels;

namespace CmdRunnerPro.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        void OnChanged([CallerMemberName] string? p = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));

        private readonly CommandRunner _runner = new();
        private CancellationTokenSource? _cts;

        public ObservableCollection<PortInfo> AvailablePorts { get; } = new();
        public ObservableCollection<CommandTemplate> Templates { get; } = new();
        public ObservableCollection<QueueItem> Queue { get; } = new();
        public ObservableCollection<CommandOutput> OutputLines { get; } = new();
        public ObservableCollection<InputPreset> Presets { get; } = new();
        public ObservableCollection<CommandSequence> Sequences { get; } = new();

        private UserSettings _settings = SettingsService.Load();

        public string WorkingDirectory { get => _settings.WorkingDirectory; set { _settings.WorkingDirectory = value; OnChanged(); } }
        public string SelectedCom1 { get => _settings.SelectedCom1; set { _settings.SelectedCom1 = value; OnChanged(); } }
        public string SelectedCom2 { get => _settings.SelectedCom2; set { _settings.SelectedCom2 = value; OnChanged(); } }

        public string Username { get => _settings.Username; set { _settings.Username = value; OnChanged(); } }

        // In-memory password (masked in UI, encrypted at rest on Save)
        private string _password = "";
        public string Password { get => _password; set { _password = value; OnChanged(); } }

        public string Opco { get => _settings.Opco; set { _settings.Opco = value; OnChanged(); } }
        public string Program { get => _settings.Program; set { _settings.Program = value; OnChanged(); } }

        public bool StopOnError { get => _settings.StopOnError; set { _settings.StopOnError = value; OnChanged(); } }

        private CommandTemplate? _selectedTemplate;
        public CommandTemplate? SelectedTemplate { get => _selectedTemplate; set { _selectedTemplate = value; OnChanged(); } }

        private InputPreset? _selectedPreset;
        public InputPreset? SelectedPreset { get => _selectedPreset; set { _selectedPreset = value; OnChanged(); } }

        private CommandSequence? _selectedSequence;
        public CommandSequence? SelectedSequence { get => _selectedSequence; set { _selectedSequence = value; OnChanged(); } }

        public string LogFile => LoggingService.CurrentLogFile;
        public bool IsRunning { get; private set; }
        void SetRunning(bool v) { IsRunning = v; OnChanged(nameof(IsRunning)); }

        public MainViewModel()
        {
            // Populate collections from settings (no new MainViewModel anywhere here)
            foreach (var t in _settings.Templates) Templates.Add(t);
            foreach (var p in _settings.Presets) Presets.Add(p);
            foreach (var s in _settings.Sequences) Sequences.Add(s);

            // Decrypt stored password to in-memory property
            try { Password = EncryptionService.Decrypt(_settings.PasswordEnc); } catch { Password = ""; }

            // Discover COM ports
            RefreshPorts();

            // Restore last selections (no creation of other VMs)
            if (!string.IsNullOrWhiteSpace(_settings.LastPresetName))
                SelectedPreset = Presets.FirstOrDefault(p => p.Name == _settings.LastPresetName);

            if (!string.IsNullOrWhiteSpace(_settings.LastSequenceName))
                SelectedSequence = Sequences.FirstOrDefault(s => s.Name == _settings.LastSequenceName);
        }

        public void RefreshPorts()
        {
            AvailablePorts.Clear();
            foreach (var p in ComPortService.GetPorts()) AvailablePorts.Add(p);
        }

        Dictionary<string, string?> Tokens() => new()
        {
            ["comport1"] = string.IsNullOrWhiteSpace(SelectedCom1) ? null : SelectedCom1,
            ["comport2"] = string.IsNullOrWhiteSpace(SelectedCom2) ? null : SelectedCom2,
            ["username"] = Username,
            ["password"] = Password,
            ["opco"] = Opco,
            ["program"] = Program,
            ["wd"] = WorkingDirectory
        };

        private IEnumerable<string?> Secrets() => new[] { Password };

        private void Info(string line) => OutputLines.Add(new CommandOutput { IsError = false, Line = line });
        private void Err(string line) => OutputLines.Add(new CommandOutput { IsError = true, Line = line });

        private List<string> ValidateForTemplate(CommandTemplate template)
        {
            var issues = new List<string>();

            if (string.IsNullOrWhiteSpace(WorkingDirectory) || !Directory.Exists(WorkingDirectory))
                issues.Add("Working directory is not set or does not exist.");

            var used = TemplateEngine.GetTokensUsed(template.Template);
            var map = Tokens();

            foreach (var u in used)
            {
                if (!map.TryGetValue(u, out var val) || string.IsNullOrWhiteSpace(val))
                    issues.Add($"Token {{{u}}} is required by template \"{template.Name}\" but is empty.");
            }

            if (template.Template.Contains("MeterMate", StringComparison.OrdinalIgnoreCase))
            {
                var exe = MeterMateService.FindExecutable(WorkingDirectory);
                if (string.IsNullOrWhiteSpace(exe))
                    issues.Add("MeterMate.exe was not found in Working Directory, install folder, or PATH.");
            }

            return issues;
        }

        public void AddSelectedTemplateToQueue()
        {
            if (SelectedTemplate == null) return;

            var problems = ValidateForTemplate(SelectedTemplate);
            if (problems.Count > 0)
            {
                foreach (var msg in problems) Info("[VALIDATION] " + msg);
                return;
            }

            var real = TemplateEngine.Expand(SelectedTemplate.Template, Tokens());
            if (string.IsNullOrWhiteSpace(real)) return;

            var display = SecurityService.Redact(real, Secrets());
            Queue.Add(new QueueItem { Command = real, Display = display, TemplateName = SelectedTemplate.Name });
        }

        public async void RunQueueAsync()
        {
            if (IsRunning || Queue.Count == 0) return;

            if (string.IsNullOrWhiteSpace(WorkingDirectory) || !Directory.Exists(WorkingDirectory))
            {
                Info("[VALIDATION] Working directory is not set or does not exist.");
                return;
            }

            _cts = new CancellationTokenSource();
            SetRunning(true);
            OutputLines.Clear();

            var progress = new Progress<CommandOutput>(o =>
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() => OutputLines.Add(o));
            });

            try
            {
                var items = Queue.Select(q => (q.Command, q.Display));
                var ok = await _runner.RunQueueAsync(items, WorkingDirectory, StopOnError, progress, _cts.Token);
                Info(ok ? "[DONE]" : "[STOPPED/ERROR]");
            }
            catch (Exception ex)
            {
                Err("[EXCEPTION] " + ex.Message);
                LoggingService.Log("[EXCEPTION] " + ex);
            }
            finally
            {
                SetRunning(false);
                _cts?.Dispose();
                _cts = null;
            }
        }

        public void Stop() => _cts?.Cancel();

        public void MoveQueueItem(int index, int delta)
        {
            var newIdx = index + delta;
            if (index < 0 || newIdx < 0 || newIdx >= Queue.Count) return;
            var item = Queue[index];
            Queue.RemoveAt(index);
            Queue.Insert(newIdx, item);
        }

        public void RemoveQueueItem(int index)
        {
            if (index >= 0 && index < Queue.Count) Queue.RemoveAt(index);
        }

        public void ClearQueue() => Queue.Clear();

        public void SavePreset(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            var existing = Presets.FirstOrDefault(p => p.Name == name);

            var p = new InputPreset
            {
                Name = name,
                WorkingDirectory = WorkingDirectory,
                Com1 = SelectedCom1 ?? "",
                Com2 = SelectedCom2 ?? "",
                Username = Username,
                PasswordEnc = EncryptionService.Encrypt(Password),
                Opco = Opco,
                Program = Program
            };

            if (existing != null)
            {
                Presets.Remove(existing);
                _settings.Presets.RemoveAll(x => x.Name == name);
            }
            Presets.Add(p);
            _settings.Presets.Add(p);
            _settings.LastPresetName = name;
            SaveAll();
        }

        public void LoadPreset(InputPreset? p)
        {
            if (p == null) return;
            WorkingDirectory = p.WorkingDirectory;
            SelectedCom1 = p.Com1;
            SelectedCom2 = p.Com2;
            Username = p.Username;
            try { Password = EncryptionService.Decrypt(p.PasswordEnc); } catch { Password = ""; }
            Opco = p.Opco;
            Program = p.Program;
            _settings.LastPresetName = p.Name;
            SaveAll();
        }

        public void SaveSequence(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || Queue.Count == 0) return;

            // Save as template names only (no secrets persisted)
            var templateNames = Queue
                .Select(q => q.TemplateName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Cast<string>()
                .ToList();

            if (templateNames.Count == 0)
            {
                Info("[INFO] Sequence not saved: no template references in queue.");
                return;
            }

            var existing = Sequences.FirstOrDefault(s => s.Name == name);
            var seq = new CommandSequence { Name = name, TemplateNames = templateNames };

            if (existing != null)
            {
                Sequences.Remove(existing);
                _settings.Sequences.RemoveAll(x => x.Name == name);
            }
            Sequences.Add(seq);
            _settings.Sequences.Add(seq);
            _settings.LastSequenceName = name;
            SaveAll();
        }

        public void LoadSequence(CommandSequence? seq)
        {
            if (seq == null) return;

            Queue.Clear();

            foreach (var name in seq.TemplateNames)
            {
                var t = Templates.FirstOrDefault(x => x.Name == name);
                if (t == null)
                {
                    Info($"[INFO] Template not found: {name}");
                    continue;
                }

                var real = TemplateEngine.Expand(t.Template, Tokens());
                if (string.IsNullOrWhiteSpace(real)) continue;

                var display = SecurityService.Redact(real, Secrets());
                Queue.Add(new QueueItem { Command = real, Display = display, TemplateName = t.Name });
            }

            _settings.LastSequenceName = seq.Name;
            SaveAll();
        }

        public void SaveAll()
        {
            _settings.Templates = Templates.ToList();
            _settings.PasswordEnc = EncryptionService.Encrypt(Password);
            SettingsService.Save(_settings);
        }

        // --- MeterMate helpers ---
        public void AutoDetectWorkingDirectory()
        {
            var dir = MeterMateService.FindInstallDirectory();
            if (!string.IsNullOrWhiteSpace(dir))
            {
                WorkingDirectory = dir;
                Info($"[INFO] Working Directory set to detected MeterMate install: {dir}");
                SaveAll();
            }
            else
            {
                Info("[INFO] Could not detect MeterMate install under Program Files.");
            }
        }

        public void TestMeterMate()
        {
            var exe = MeterMateService.FindExecutable(WorkingDirectory);
            if (string.IsNullOrWhiteSpace(exe))
            {
                Err("MeterMate.exe not found (WD/install PATH).");
                return;
            }

            var ver = MeterMateService.GetFileVersion(exe) ?? "unknown";
            Info($"MeterMate found: \"{exe}\" (version {ver})");
        }

        // --- Template editor ---
        public void OpenTemplateEditor()
        {
            var win = new CmdRunnerPro.Views.TemplateEditor(Templates);
            win.Owner = System.Windows.Application.Current.MainWindow;
            if (win.ShowDialog() == true)
            {
                _settings.Templates = Templates.ToList();
                SettingsService.Save(_settings);
            }
        }

        // --- EXPORT / IMPORT ---
        public bool ExportTemplatesTo(string path)
        {
            try { ExportImportService.ExportTemplates(Templates, path); return true; }
            catch (Exception ex) { Err("Export Templates failed: " + ex.Message); return false; }
        }

        public bool ImportTemplatesFrom(string path)
        {
            try
            {
                var incoming = ExportImportService.ImportTemplates(path);
                foreach (var t in incoming)
                {
                    var existing = Templates.FirstOrDefault(x => x.Name.Equals(t.Name, StringComparison.OrdinalIgnoreCase));
                    if (existing != null) Templates.Remove(existing);
                    Templates.Add(t);
                }
                _settings.Templates = Templates.ToList();
                SettingsService.Save(_settings);
                Info($"[INFO] Imported {incoming.Count} template(s).");
                return true;
            }
            catch (Exception ex) { Err("Import Templates failed: " + ex.Message); return false; }
        }

        public bool ExportPresetsTo(string path, bool includeEncryptedPasswords)
        {
            try
            {
                ExportImportService.ExportPresets(Presets, path, includeEncryptedPasswords);
                if (!includeEncryptedPasswords) Info("[INFO] Exported presets WITHOUT passwords (recommended).");
                else Info("[INFO] Exported presets WITH encrypted passwords (DPAPI user/machine bound).");
                return true;
            }
            catch (Exception ex) { Err("Export Presets failed: " + ex.Message); return false; }
        }

        public bool ImportPresetsFrom(string path)
        {
            try
            {
                var incoming = ExportImportService.ImportPresets(path);
                foreach (var p in incoming)
                {
                    var existing = Presets.FirstOrDefault(x => x.Name.Equals(p.Name, StringComparison.OrdinalIgnoreCase));
                    if (existing != null) Presets.Remove(existing);
                    Presets.Add(p);
                }
                _settings.Presets = Presets.ToList();
                SettingsService.Save(_settings);
                Info($"[INFO] Imported {incoming.Count} preset(s).");
                return true;
            }
            catch (Exception ex) { Err("Import Presets failed: " + ex.Message); return false; }
        }

        public bool ExportSequencesTo(string path)
        {
            try { ExportImportService.ExportSequences(Sequences, path); return true; }
            catch (Exception ex) { Err("Export Sequences failed: " + ex.Message); return false; }
        }

        public bool ImportSequencesFrom(string path)
        {
            try
            {
                var incoming = ExportImportService.ImportSequences(path);
                foreach (var s in incoming)
                {
                    var existing = Sequences.FirstOrDefault(x => x.Name.Equals(s.Name, StringComparison.OrdinalIgnoreCase));
                    if (existing != null) Sequences.Remove(existing);
                    Sequences.Add(s);
                }
                _settings.Sequences = Sequences.ToList();
                SettingsService.Save(_settings);
                Info($"[INFO] Imported {incoming.Count} sequence(s).");
                return true;
            }
            catch (Exception ex) { Err("Import Sequences failed: " + ex.Message); return false; }
        }
    }
}
