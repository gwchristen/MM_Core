// ViewModels/MainViewModel.cs
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CmdRunnerPro.Models;           // Assumes Template, InputPreset live here
using CmdRunnerPro.Views;            // TemplateEditor (UserControl)
using MaterialDesignThemes.Wpf;      // DialogHost.Show(...)
using CmdRunnerPro.ViewModels;       // TemplateEditorViewModel
using WinForms = System.Windows.Forms;

namespace CmdRunnerPro.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        #region Fields

        private Process _currentProcess;
        private CancellationTokenSource _runCts;

        #endregion

        #region Ctor

        public MainViewModel()
        {
            // Commands (match your snippet; lambdas avoid method-group overload ambiguity)
            LoadPresetCommand = new RelayCommand<InputPreset?>(p => LoadPreset(p ?? SelectedPreset), _ => SelectedPreset != null);
            RunCommand = new RelayCommand(async () => await RunSelectedAsync(), () => SelectedTemplate != null);
            StopCommand = new RelayCommand(Stop, () => _currentProcess != null && !_currentProcess.HasExited);
            BrowseWorkingDirectoryCommand = new RelayCommand(BrowseWorkingDirectory);
            NewTemplateCommand = new RelayCommand(NewTemplate);
            CloneTemplateCommand = new RelayCommand(CloneTemplate, () => SelectedTemplate != null);
            DeleteTemplateCommand = new RelayCommand(DeleteTemplate, () => SelectedTemplate != null);

            // Template Editor command (uses parameterless wrapper that awaits the async core)
            OpenTemplateEditorCommand = new RelayCommand(OpenTemplateEditor, () => SelectedTemplate != null);

            ApplyTheme();        // optional hook; safe no-op here
            RefreshPorts();      // populate COM ports
            SeedData();          // demo data; replace with persisted load if desired
        }

        #endregion

        #region Collections & Selection

        private ObservableCollection<Template> _templates = new();
        public ObservableCollection<Template> Templates
        {
            get => _templates;
            set => Set(ref _templates, value);
        }

        private Template _selectedTemplate;
        public Template SelectedTemplate
        {
            get => _selectedTemplate;
            set
            {
                if (Set(ref _selectedTemplate, value))
                {
                    // When template changes, command can-execute may change
                    RaiseCommandCanExecuteChanged();
                }
            }
        }

        private ObservableCollection<InputPreset> _presets = new();
        public ObservableCollection<InputPreset> Presets
        {
            get => _presets;
            set => Set(ref _presets, value);
        }

        private InputPreset _selectedPreset;
        public InputPreset SelectedPreset
        {
            get => _selectedPreset;
            set
            {
                if (Set(ref _selectedPreset, value))
                {
                    // Preset selection affects LoadPresetCommand
                    RaiseCommandCanExecuteChanged();
                }
            }
        }

        #endregion

        #region Inputs / Working Directory

        // Six inputs referenced in your README (used when expanding tokens at run-time)
        private string _selectedCom1;
        public string SelectedCom1
        {
            get => _selectedCom1;
            set => Set(ref _selectedCom1, value);
        }

        private string _selectedCom2;
        public string SelectedCom2
        {
            get => _selectedCom2;
            set => Set(ref _selectedCom2, value);
        }

        private string _username;
        public string Username
        {
            get => _username;
            set => Set(ref _username, value);
        }

        private string _password;
        public string Password
        {
            get => _password;
            set => Set(ref _password, value);
        }

        private string _opco;
        public string Opco
        {
            get => _opco;
            set => Set(ref _opco, value);
        }

        private string _program;
        public string Program
        {
            get => _program;
            set => Set(ref _program, value);
        }

        private string _workingDirectory;
        public string WorkingDirectory
        {
            get => _workingDirectory;
            set => Set(ref _workingDirectory, value);
        }

        private ObservableCollection<string> _comPortOptions = new();
        public ObservableCollection<string> ComPortOptions
        {
            get => _comPortOptions;
            set => Set(ref _comPortOptions, value);
        }

        #endregion

        #region Output

        private string _outputLog;
        public string OutputLog
        {
            get => _outputLog;
            set => Set(ref _outputLog, value);
        }

        private void AppendOutput(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            OutputLog = string.IsNullOrEmpty(OutputLog) ? text : $"{OutputLog}{Environment.NewLine}{text}";
        }

        #endregion

        #region Commands

        public ICommand LoadPresetCommand { get; }
        public ICommand RunCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand BrowseWorkingDirectoryCommand { get; }
        public ICommand OpenTemplateEditorCommand { get; }
        public ICommand NewTemplateCommand { get; }
        public ICommand CloneTemplateCommand { get; }
        public ICommand DeleteTemplateCommand { get; }


        private void RaiseCommandCanExecuteChanged()
        {
            (LoadPresetCommand as RelayCommand<InputPreset?>)?.RaiseCanExecuteChanged();
            (RunCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (StopCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (OpenTemplateEditorCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (NewTemplateCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (CloneTemplateCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (DeleteTemplateCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        #endregion

        #region Presets

        private void LoadPreset(InputPreset preset)
        {
            if (preset == null) return;

            // TODO: tie your preset fields into the six inputs and/or selected template name.
            // This is a harmless example that tries to select a matching template by name.
            if (!string.IsNullOrWhiteSpace(preset.TemplateName))
            {
                var match = Templates.FirstOrDefault(t => string.Equals(t.Name, preset.TemplateName, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                    SelectedTemplate = match;
            }

            // Example: apply input defaults from preset (if your model has them)
            Username = string.IsNullOrEmpty(preset.Username) ? Username : preset.Username;
            Opco = string.IsNullOrEmpty(preset.Opco) ? Opco : preset.Opco;
            Program = string.IsNullOrEmpty(preset.Program) ? Program : preset.Program;
            // Password: you might decrypt from DPAPI if preset stores an encrypted form.
        }

        #endregion

        #region Run / Stop

        private async Task RunSelectedAsync()
        {
            if (SelectedTemplate == null) return;
            _runCts?.Cancel();
            _runCts = new CancellationTokenSource();

            try
            {
                AppendOutput($"--- Running template: {SelectedTemplate.Name} ---");

                // Expand tokens per README (supports {Q:token} to auto-quote on spaces)  [2](https://stackoverflow.com/questions/18126559/how-can-i-download-a-single-raw-file-from-a-private-github-repo-using-the-comman)
                var command = ExpandTokens(SelectedTemplate.TemplateText, buildRuntimeTokenMap());

                // Default to WorkingDirectory if provided; otherwise current process dir
                var wd = string.IsNullOrWhiteSpace(WorkingDirectory)
                    ? Environment.CurrentDirectory
                    : WorkingDirectory;

                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/C " + command,
                    WorkingDirectory = wd,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                _currentProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };
                _currentProcess.OutputDataReceived += (_, e) => { if (e.Data != null) AppendOutput(e.Data); };
                _currentProcess.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data != null) AppendOutput("[ERR] " + e.Data);
                };
                _currentProcess.Exited += (_, __) =>
                {
                    AppendOutput($"--- Process exited (Code: {_currentProcess.ExitCode}) ---");
                    RaiseCommandCanExecuteChanged();
                };

                bool started = _currentProcess.Start();
                RaiseCommandCanExecuteChanged();

                if (started)
                {
                    _currentProcess.BeginOutputReadLine();
                    _currentProcess.BeginErrorReadLine();
                    await _currentProcess.WaitForExitAsync(_runCts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                AppendOutput("--- Run canceled ---");
            }
            catch (Exception ex)
            {
                AppendOutput("[EXCEPTION] " + ex.Message);
            }
            finally
            {
                _runCts?.Dispose();
                _runCts = null;
                _currentProcess?.Dispose();
                _currentProcess = null;
                RaiseCommandCanExecuteChanged();
            }
        }

        private void Stop()
        {
            try
            {
                _runCts?.Cancel();
                if (_currentProcess is { HasExited: false })
                {
                    _currentProcess.Kill(true);
                    AppendOutput("--- Stopped process ---");
                }
            }
            catch (Exception ex)
            {
                AppendOutput("[STOP ERROR] " + ex.Message);
            }
        }

        #endregion

        #region Working Directory (WinForms chooser)

        private void BrowseWorkingDirectory()
        {
            using var dlg = new WinForms.FolderBrowserDialog
            {
                Description = "Choose Working Directory",
                ShowNewFolderButton = true
            };

            if (!string.IsNullOrWhiteSpace(WorkingDirectory) && Directory.Exists(WorkingDirectory))
                dlg.SelectedPath = WorkingDirectory;

            if (dlg.ShowDialog() == WinForms.DialogResult.OK)
                WorkingDirectory = dlg.SelectedPath;
        }

        #endregion

        #region Template Editor (DialogHost, Identifier="RootDialog")

        // Parameterless wrapper suitable for a non-generic RelayCommand
        private async void OpenTemplateEditor()
        {
            await OpenTemplateEditorAsync(SelectedTemplate);
        }

        // Async core that opens the editor and applies on Save
        private async Task OpenTemplateEditorAsync(Template template)
        {
            if (template is null) return;

            var oldName = template.Name;

            // Build unique checker excluding this template
            var existingNames = new HashSet<string>(
                Templates.Where(t => !ReferenceEquals(t, template))
                         .Select(t => t.Name),
                StringComparer.OrdinalIgnoreCase);

            var editor = new TemplateEditor();
            var vm = new TemplateEditorViewModel(
                originalTemplate: template,
                name: template.Name,
                template: template.TemplateText,
                comPortOptions: ComPortOptions,
                nameExists: n => existingNames.Contains(n?.Trim() ?? "")
            );
            editor.DataContext = vm;

            var result = await MaterialDesignThemes.Wpf.DialogHost.Show(editor, "RootDialog");
            if (result is TemplateEditorViewModel saved)
            {
                saved.ApplyTo(template);              // writes Name + TemplateText
                OnPropertyChanged(nameof(Templates));
                OnPropertyChanged(nameof(SelectedTemplate));

                // If the name changed, fix references
                if (!string.Equals(oldName, template.Name, StringComparison.OrdinalIgnoreCase))
                {
                    RenameTemplateInReferences(oldName, template.Name);
                }
            }
        }

        #endregion

        #region Token Expansion

        private static readonly Regex TokenRegex = new(@"\{(Q:)?([^}]+)\}", RegexOptions.Compiled);

        private string ExpandTokens(string template, Func<string, string> resolver)
        {
            if (string.IsNullOrEmpty(template)) return string.Empty;

            return TokenRegex.Replace(template, m =>
            {
                bool quote = m.Groups[1].Success;
                string rawKey = m.Groups[2].Value.Trim();

                string value = resolver(rawKey);
                if (value == null)
                    return m.Value; // unknown token; leave as-is

                if (quote && value.Contains(' '))
                    return $"\"{value}\"";

                return value;
            });
        }

        private Func<string, string> buildRuntimeTokenMap()
        {
            // README tokens & aliases: {comport1},{comport2},{username},{password},{opco},{program},{wd},
            // aliases {COM1},{COM2},{FIELD3..6},{WD}, and {Q:token} variant.  [2](https://stackoverflow.com/questions/18126559/how-can-i-download-a-single-raw-file-from-a-private-github-repo-using-the-comman)

            // Precompute WD
            var wd = string.IsNullOrWhiteSpace(WorkingDirectory) ? Environment.CurrentDirectory : WorkingDirectory;

            return key =>
            {
                switch (key)
                {
                    case "comport1":
                    case "COM1":
                    case "FIELD3":
                        return SelectedCom1 ?? "";

                    case "comport2":
                    case "COM2":
                    case "FIELD4":
                        return SelectedCom2 ?? "";

                    case "username":
                    case "FIELD5":
                        return Username ?? "";

                    case "password":
                    case "FIELD6":
                        // At runtime your logs redact secrets—this returns the raw value for actual execution.
                        return Password ?? "";

                    case "opco":
                        return Opco ?? "";

                    case "program":
                        return Program ?? "";

                    case "wd":
                    case "WD":
                        return wd ?? "";

                    default:
                        return null;
                }
            };
        }

        #endregion

        #region Theme / Ports / Seed (placeholders you can replace with your services)

        private void ApplyTheme()
        {
            // Optional: your app already defines BundledTheme + MaterialDesign2.Defaults in App.xaml,
            // so nothing needed here. Keep as a hook if you change themes dynamically.  [2](https://stackoverflow.com/questions/18126559/how-can-i-download-a-single-raw-file-from-a-private-github-repo-using-the-comman)
        }

        private void RefreshPorts()
        {
            var ports = SerialPort.GetPortNames().OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
            ComPortOptions = new ObservableCollection<string>(ports);
        }

        private void SeedData()
        {
            if (Templates.Count > 0) return;

            // Demo items: safe placeholders to demonstrate flow (replace with your persistence).
            Templates.Add(new Template
            {
                Name = "Show COM1 & User",
                TemplateText = "echo COM1={comport1} USER={username}"
            });

            Templates.Add(new Template
            {
                Name = "Run Program (quoted if needed)",
                TemplateText = "{Q:program} --help"
            });

            SelectedTemplate = Templates.FirstOrDefault();

            Presets.Add(new InputPreset
            {
                Name = "Example Preset",
                TemplateName = SelectedTemplate?.Name,
                Username = "tech1",
                Opco = "OP01",
                Program = "cmd.exe"
            });
            SelectedPreset = Presets.FirstOrDefault();
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        protected bool Set<T>(ref T storage, T value, [CallerMemberName] string name = null)
        {
            if (Equals(storage, value)) return false;
            storage = value;
            OnPropertyChanged(name);
            return true;
        }

        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        #endregion

        private async void NewTemplate()
        {
            // Start with a blank model (not yet added to the collection)
            var scratch = new Template { Name = "", TemplateText = "" };

            // Build the name set (no need to exclude; this item isn't in collection yet)
            var existingNames = new HashSet<string>(Templates.Select(t => t.Name), StringComparer.OrdinalIgnoreCase);

            var editor = new TemplateEditor();
            var vm = new TemplateEditorViewModel(
                originalTemplate: scratch,
                name: scratch.Name,
                template: scratch.TemplateText,
                comPortOptions: ComPortOptions,
                nameExists: n => existingNames.Contains(n?.Trim() ?? "")
            );
            editor.DataContext = vm;

            var result = await MaterialDesignThemes.Wpf.DialogHost.Show(editor, "RootDialog");
            if (result is TemplateEditorViewModel saved)
            {
                saved.ApplyTo(scratch);
                // Add to collection
                Templates.Add(scratch);
                SelectedTemplate = scratch;
            }
        }

        private async void CloneTemplate()
        {
            if (SelectedTemplate is null) return;

            // Propose a non-conflicting name
            string baseName = SelectedTemplate.Name;
            string proposal = baseName + " (copy)";
            var existingNames = new HashSet<string>(Templates.Select(t => t.Name), StringComparer.OrdinalIgnoreCase);
            int i = 2;
            while (existingNames.Contains(proposal)) proposal = $"{baseName} (copy {i++})";

            var clone = new Template
            {
                Name = proposal,
                TemplateText = SelectedTemplate.TemplateText
            };

            var editor = new TemplateEditor();
            var vm = new TemplateEditorViewModel(
                originalTemplate: clone,
                name: clone.Name,
                template: clone.TemplateText,
                comPortOptions: ComPortOptions,
                nameExists: n => existingNames.Contains(n?.Trim() ?? "")
            );
            editor.DataContext = vm;

            var result = await MaterialDesignThemes.Wpf.DialogHost.Show(editor, "RootDialog");
            if (result is TemplateEditorViewModel saved)
            {
                saved.ApplyTo(clone);
                Templates.Add(clone);
                SelectedTemplate = clone;
            }
        }

        private void DeleteTemplate()
        {
            if (SelectedTemplate is null) return;

            // Quick confirm (use DialogHost with a custom view if you prefer)
            var answer = System.Windows.MessageBox.Show(
                $"Delete template \"{SelectedTemplate.Name}\"?",
                "Confirm Delete",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (answer != System.Windows.MessageBoxResult.Yes) return;

            // Optional: prevent delete if referenced by sequences/presets
            // if (IsTemplateReferenced(SelectedTemplate.Name)) { ... warn ... return; }

            var oldName = SelectedTemplate.Name;
            Templates.Remove(SelectedTemplate);

            // Optional: clean-up references (or prompt)
            RemoveTemplateFromReferences(oldName);

            SelectedTemplate = Templates.FirstOrDefault();


        }

        public ObservableCollection<Sequence> Sequences { get; } = new();

        public class Sequence
        {
            public List<string> TemplateNames { get; set; } = new();
        }

        #region Template reference maintenance (Presets only, safe to add even if Sequences aren’t wired yet)

        /// <summary>
        /// Update all references to a template name after a rename (e.g., in Presets).
        /// Safe no-op if nothing references the template.
        /// </summary>
        private void RenameTemplateInReferences(string oldName, string newName)
        {
            if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName))
                return;

            // Update Presets
            foreach (var preset in Presets ?? Enumerable.Empty<InputPreset>())
            {
                if (string.Equals(preset.TemplateName, oldName, StringComparison.OrdinalIgnoreCase))
                    preset.TemplateName = newName;
            }

            // Notify if your UI binds to these collections/properties
            OnPropertyChanged(nameof(Presets));
        }

        /// <summary>
        /// Remove any references to a template after delete (e.g., Presets).
        /// </summary>
        private void RemoveTemplateFromReferences(string oldName)
        {
            if (string.IsNullOrWhiteSpace(oldName))
                return;

            // Clear preset references that pointed at the deleted template
            foreach (var preset in Presets ?? Enumerable.Empty<InputPreset>())
            {
                if (string.Equals(preset.TemplateName, oldName, StringComparison.OrdinalIgnoreCase))
                    preset.TemplateName = null;
            }

            OnPropertyChanged(nameof(Presets));
        }

        #endregion

    }
}
