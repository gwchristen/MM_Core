// ViewModels/MainViewModel.cs
using CmdRunnerPro.Models;             // Template, InputPreset
using CmdRunnerPro.Views;              // TemplateEditor (UserControl)
using MaterialDesignColors;            // SwatchHelper
using MaterialDesignThemes.Wpf;        // PaletteHelper, DialogHost
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TrayNotify;
using MC = MaterialDesignColors;
using MD = MaterialDesignThemes.Wpf;
using WinForms = System.Windows.Forms;
using System.Windows;


namespace CmdRunnerPro.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        #region Fields
        private Process _currentProcess;
        private CancellationTokenSource _runCts;

        // Settings persistence (theme + timestamps)
        private readonly string _configDir;
        private readonly string _themeSettingsPath;
        #endregion

        #region Ctor
        public MainViewModel()
        {
            // Resolve settings paths
            _configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CmdRunnerPro");
            _themeSettingsPath = Path.Combine(_configDir, "theme.json");

            // Commands
            LoadPresetCommand = new RelayCommand<InputPreset?>(p => LoadPreset(p ?? SelectedPreset), _ => SelectedPreset != null);
            RunCommand = new RelayCommand(async () => await RunSelectedAsync(), () => SelectedTemplate != null);
            StopCommand = new RelayCommand(Stop, () => _currentProcess != null && !_currentProcess.HasExited);
            BrowseWorkingDirectoryCommand = new RelayCommand(BrowseWorkingDirectory);

            NewTemplateCommand = new RelayCommand(NewTemplate);
            CloneTemplateCommand = new RelayCommand(CloneTemplate, () => SelectedTemplate != null);
            DeleteTemplateCommand = new RelayCommand(DeleteTemplate, () => SelectedTemplate != null);

            OpenTemplateEditorCommand = new RelayCommand(OpenTemplateEditor, () => SelectedTemplate != null);

            SavePresetCommand = new RelayCommand(SavePreset, () => true);
            SavePresetAsCommand = new RelayCommand(SavePresetAs, () => true);
            DeletePresetCommand = new RelayCommand(DeletePreset, () => SelectedPreset != null);

            // Initialize data
            RefreshPorts();
            SeedData();


            LoadPresetsFromDisk();
            LoadLastUsed();

            // Load persisted theme + ShowTimestamps (or defaults), then apply theme
            LoadThemeSettings();   // sets backing fields + Settings.ShowTimestamps
            ApplyTheme();

            // Save theme when ShowTimestamps changes (keep in same file)
            Settings.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(SettingsViewModel.ShowTimestamps))
                    SaveThemeSettings();
            };

            // Make preview reflect current defaults
            UpdateTemplatePreview();
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
                    RaiseCommandCanExecuteChanged();
                    UpdateTemplatePreview();   // recompute preview
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
                    RaiseCommandCanExecuteChanged();
                }
            }
        }
        #endregion

        #region Inputs / Working Directory
        private string _selectedCom1;
        private string _selectedCom2;
        private string _username;
        private string _password;
        private string _opco;
        private string _program;
        private string _workingDirectory;

        private ObservableCollection<string> _comPortOptions = new();
        public ObservableCollection<string> ComPortOptions
        {
            get => _comPortOptions;
            set => Set(ref _comPortOptions, value);
        }
        #endregion

        #region Preview + Output
        private string _templateContent = string.Empty;
        /// <summary>
        /// Expanded template with current token inputs; bound to the right-side "Command Preview" TextBox.
        /// </summary>
        public string TemplateContent
        {
            get => _templateContent;
            private set => Set(ref _templateContent, value);
        }

        /// <summary>
        /// Live output lines for the right-side ListBox.
        /// </summary>
        public ObservableCollection<string> OutputLines { get; } = new();

        private string _outputLog;
        /// <summary>
        /// Legacy whole-log string; kept for compatibility if other parts of the app rely on it.
        /// </summary>
        public string OutputLog
        {
            get => _outputLog;
            set => Set(ref _outputLog, value);
        }

        private void AppendOutput(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            string line = Settings?.ShowTimestamps == true
                ? $"[{DateTime.Now:HH:mm:ss}] {text}"
                : text;

            OutputLines.Add(line);

            OutputLog = string.IsNullOrEmpty(OutputLog)
                ? line
                : $"{OutputLog}{Environment.NewLine}{line}";
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
        public ICommand SavePresetCommand { get; }
        public ICommand SavePresetAsCommand { get; }
        public ICommand DeletePresetCommand { get; }

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

            // template preference
            if (!string.IsNullOrWhiteSpace(preset.TemplateName))
            {
                var match = Templates.FirstOrDefault(t =>
                    string.Equals(t.Name, preset.TemplateName, StringComparison.OrdinalIgnoreCase));
                if (match != null) SelectedTemplate = match;
            }

            // tokens
            SelectedCom1 = preset.Com1;
            SelectedCom2 = preset.Com2;
            Username = preset.Username;
            Password = preset.Password;
            Opco = preset.Opco;
            Program = preset.Program;
            WorkingDirectory = preset.WorkingDirectory;

            UpdateTemplatePreview();
            SaveLastUsed(); // remember what we just loaded
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

                var command = ExpandTokens(SelectedTemplate.TemplateText, buildRuntimeTokenMap());

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

                _currentProcess.OutputDataReceived += (_, e) =>
                {
                    if (e.Data != null) AppendOutput(e.Data);
                };

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
        private async void OpenTemplateEditor() => await OpenTemplateEditorAsync(SelectedTemplate);

        private async Task OpenTemplateEditorAsync(Template template)
        {
            if (template is null) return;

            var oldName = template.Name;

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

            var result = await DialogHost.Show(editor, "RootDialog");
            if (result is TemplateEditorViewModel saved)
            {
                saved.ApplyTo(template);
                OnPropertyChanged(nameof(Templates));
                OnPropertyChanged(nameof(SelectedTemplate));

                if (!string.Equals(oldName, template.Name, StringComparison.OrdinalIgnoreCase))
                {
                    RenameTemplateInReferences(oldName, template.Name);
                }

                UpdateTemplatePreview();
            }
        }
        #endregion

        // Example: adjust type/props to your real Template model
        public class Template
        {
            public Guid Id { get; set; }
            public string Name { get; set; }
            public string TemplateText { get; set; }
        }

        // Where you open the editor dialog:
        public async Task EditSelectedTemplateAsync()
        {
            if (SelectedTemplate is null) return;

            var vm = new TemplateEditorViewModel(
                originalTemplate: SelectedTemplate,
                name: SelectedTemplate.Name,
                template: SelectedTemplate.TemplateText,
                //comPortOptions: AvailableComPorts,
                nameExists: name => Templates.Any(t =>
                    !ReferenceEquals(t, SelectedTemplate) &&
                    string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
            );

            var view = new TemplateEditor { DataContext = vm };
            var result = await MaterialDesignThemes.Wpf.DialogHost.Show(view, "RootDialog");

            if (result is TemplateEditorResult r)
            {
                switch (r.Action)
                {
                    case TemplateEditorResult.EditorAction.Saved:
                        // Save back the edited original
                        await SaveTemplateAsync(SelectedTemplate);
                        break;

                    case TemplateEditorResult.EditorAction.SavedAs:
                        {
                            // Create a *new* template with new Id + the provided name + text
                            var copy = new Template
                            {
                                Id = Guid.NewGuid(),
                                Name = r.Name?.Trim() ?? "New Template",
                                TemplateText = r.TemplateText ?? string.Empty
                            };

                            Templates.Add(copy);
                            await SaveTemplateAsync(copy);
                            SelectedTemplate = copy;      // select the new one
                            break;
                        }

                    case TemplateEditorResult.EditorAction.Deleted:
                        if (await ConfirmAsync($"Delete \"{r.Name}\"?"))
                        {
                            await DeleteTemplateAsync(SelectedTemplate);
                            Templates.Remove(SelectedTemplate);
                            SelectedTemplate = Templates.FirstOrDefault();
                        }
                        break;

                    case TemplateEditorResult.EditorAction.Cancelled:
                    default:
                        break;
                }
            }
        }

        #region Token Expansion
        private static readonly Regex TokenRegex = new(@"\{(Q:)?([^\}]+)\}", RegexOptions.Compiled);

        private string ExpandTokens(string template, Func<string, string> resolver)
        {
            if (string.IsNullOrEmpty(template)) return string.Empty;

            return TokenRegex.Replace(template, m =>
            {
                bool quote = m.Groups[1].Success;
                string rawKey = m.Groups[2].Value.Trim();
                string value = resolver(rawKey);
                if (value == null)
                    return m.Value;

                if (quote && value.Contains(' '))
                    return $"\"{value}\"";

                return value;
            });
        }

        private Func<string, string> buildRuntimeTokenMap()
        {
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

        private void UpdateTemplatePreview()
        {
            var t = SelectedTemplate?.TemplateText ?? string.Empty;
            if (string.IsNullOrWhiteSpace(t))
            {
                TemplateContent = string.Empty;
                return;
            }

            var expanded = ExpandTokens(t, buildRuntimeTokenMap());
            TemplateContent = expanded;
        }
        #endregion

        #region Theme
        private bool _isDarkTheme;
        public bool IsDarkTheme
        {
            get => _isDarkTheme;
            set
            {
                if (_isDarkTheme != value)
                {
                    _isDarkTheme = value;

                    OnPropertyChanged(nameof(IsDarkTheme));
                    ApplyTheme(); // <- critical
                }
            }
        }

        private string _primaryColor = "Blue";
        public string PrimaryColor
        {
            get => _primaryColor;
            set
            {
                if (Set(ref _primaryColor, value))
                {
                    ApplyTheme();
                    SaveThemeSettings();
                }
            }
        }

        private string _secondaryColor = "Amber";
        public string SecondaryColor
        {
            get => _secondaryColor;
            set
            {
                if (Set(ref _secondaryColor, value))
                {
                    ApplyTheme();
                    SaveThemeSettings();
                }
            }
        }

        // Lists for your top-bar ComboBoxes
        public ObservableCollection<string> MdixPrimaryColors { get; } = new(new[]
        {
            "Red","Pink","Purple","DeepPurple","Indigo","Blue","LightBlue","Cyan","Teal",
            "Green","LightGreen","Lime","Yellow","Amber","Orange","DeepOrange","Brown","BlueGrey","Grey"
        });

        public ObservableCollection<string> MdixSecondaryColors { get; } = new(new[]
        {
            "Red","Pink","Purple","DeepPurple","Indigo","Blue","LightBlue","Cyan","Teal",
            "Green","LightGreen","Lime","Yellow","Amber","Orange","DeepOrange"
        });

        private void ApplyTheme()
        {
            try
            {
                var helper = new MaterialDesignThemes.Wpf.PaletteHelper();
                var theme = helper.GetTheme();

                // v5 API: BaseTheme.Dark/Light (no Theme.Dark/Light)
                theme.SetBaseTheme(
                    IsDarkTheme
                        ? MaterialDesignThemes.Wpf.BaseTheme.Dark
                        : MaterialDesignThemes.Wpf.BaseTheme.Light);

                // Use your bound strings (also supports hex like #FF2196F3)
                var primaryName = string.IsNullOrWhiteSpace(PrimaryColor) ? "Blue" : PrimaryColor;
                var secondaryName = string.IsNullOrWhiteSpace(SecondaryColor) ? "Amber" : SecondaryColor;

                var primaryColor = ResolvePrimaryColor(primaryName);
                var secondaryColor = ResolveSecondaryColor(secondaryName);

                theme.SetPrimaryColor(primaryColor);     // v5: set Color directly
                theme.SetSecondaryColor(secondaryColor);

                helper.SetTheme(theme);
            }
            catch
            {
                // Swallow palette errors; keep current theme.
            }
        }

        private static System.Windows.Media.Color ResolvePrimaryColor(string name)
        {
            // Allow hex (#RRGGBB or #AARRGGBB)
            if (!string.IsNullOrWhiteSpace(name) && name.StartsWith("#") &&
                System.Windows.Media.ColorConverter.ConvertFromString(name) is System.Windows.Media.Color hex)
                return hex;

            // Parse enum value from MaterialDesignColors.PrimaryColor
            if (System.Enum.TryParse<MaterialDesignColors.PrimaryColor>(RemoveSpaces(name), true, out var primaryEnum))
                return MaterialDesignColors.SwatchHelper.Lookup[
                    (MaterialDesignColors.MaterialDesignColor)primaryEnum];

            // Fallback
            return MaterialDesignColors.SwatchHelper.Lookup[
                (MaterialDesignColors.MaterialDesignColor)MaterialDesignColors.PrimaryColor.Blue];
        }

        private static System.Windows.Media.Color ResolveSecondaryColor(string name)
        {
            if (!string.IsNullOrWhiteSpace(name) && name.StartsWith("#") &&
                System.Windows.Media.ColorConverter.ConvertFromString(name) is System.Windows.Media.Color hex)
                return hex;

            if (System.Enum.TryParse<MaterialDesignColors.SecondaryColor>(RemoveSpaces(name), true, out var secondaryEnum))
                return MaterialDesignColors.SwatchHelper.Lookup[
                    (MaterialDesignColors.MaterialDesignColor)secondaryEnum];

            return MaterialDesignColors.SwatchHelper.Lookup[
                (MaterialDesignColors.MaterialDesignColor)MaterialDesignColors.SecondaryColor.Amber];
        }

        // ========= THEME SETTINGS (minimal file persistence) =========

        // %LOCALAPPDATA%\CmdRunnerPro\theme.cfg
        private static readonly string ThemeSettingsPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "CmdRunnerPro", "theme.cfg");

        private void LoadThemeSettings()
        {
            try
            {
                if (!File.Exists(ThemeSettingsPath))
                    return;

                foreach (var raw in File.ReadAllLines(ThemeSettingsPath))
                {
                    var line = raw.Trim();
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;

                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;

                    var key = line.Substring(0, eq).Trim();
                    var value = line.Substring(eq + 1).Trim();

                    switch (key)
                    {
                        case "IsDarkTheme":
                            if (bool.TryParse(value, out var dark)) IsDarkTheme = dark;
                            break;
                        case "PrimaryColor":
                            if (!string.IsNullOrWhiteSpace(value)) PrimaryColor = value;
                            break;
                        case "SecondaryColor":
                            if (!string.IsNullOrWhiteSpace(value)) SecondaryColor = value;
                            break;
                    }
                }
                // Note: setting those properties triggers ApplyTheme() via your setters.
            }
            catch
            {
                // Ignore read/parse errors to avoid blocking startup.
            }
        }

        private void SaveThemeSettings()
        {
            try
            {
                var dir = Path.GetDirectoryName(ThemeSettingsPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var lines = new[]
                {
            $"IsDarkTheme={IsDarkTheme}",
            $"PrimaryColor={PrimaryColor}",
            $"SecondaryColor={SecondaryColor}"
        };
                File.WriteAllLines(ThemeSettingsPath, lines);
            }
            catch
            {
                // Ignore write errors; theme change still applies for this session.
            }
        }

        private static string RemoveSpaces(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
                if (!char.IsWhiteSpace(s[i])) sb.Append(s[i]);
            return sb.ToString();
        }
        #endregion

        #region Theme / Ports / Seed (data init)
        private void RefreshPorts()
        {
            var ports = SerialPort.GetPortNames()
                                  .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                                  .ToList();
            ComPortOptions = new ObservableCollection<string>(ports);
        }

        private void SeedData()
        {
            if (Templates.Count > 0) return;

            // Demo items: replace with your persistence
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

        #region Template CRUD Helpers
        private async void NewTemplate()
        {
            var scratch = new Template { Name = "", TemplateText = "" };

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

            var result = await DialogHost.Show(editor, "RootDialog");
            if (result is TemplateEditorViewModel saved)
            {
                saved.ApplyTo(scratch);
                Templates.Add(scratch);
                SelectedTemplate = scratch;
                UpdateTemplatePreview();
            }
        }

        private async void CloneTemplate()
        {
            if (SelectedTemplate is null) return;

            string baseName = SelectedTemplate.Name;
            string proposal = baseName + " (copy)";
            var existingNames = new HashSet<string>(Templates.Select(t => t.Name), StringComparer.OrdinalIgnoreCase);
            int i = 2;
            while (existingNames.Contains(proposal))
                proposal = $"{baseName} (copy {i++})";

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

            var result = await DialogHost.Show(editor, "RootDialog");
            if (result is TemplateEditorViewModel saved)
            {
                saved.ApplyTo(clone);
                Templates.Add(clone);
                SelectedTemplate = clone;
                UpdateTemplatePreview();
            }
        }

        private void DeleteTemplate()
        {
            if (SelectedTemplate is null) return;

            var answer = System.Windows.MessageBox.Show(
                $"Delete template \"{SelectedTemplate.Name}\"?",
                "Confirm Delete",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (answer != System.Windows.MessageBoxResult.Yes) return;

            var oldName = SelectedTemplate.Name;
            Templates.Remove(SelectedTemplate);

            RemoveTemplateFromReferences(oldName);

            SelectedTemplate = Templates.FirstOrDefault();
            UpdateTemplatePreview();
        }
        #endregion

        #region Template reference maintenance
        private void RenameTemplateInReferences(string oldName, string newName)
        {
            if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName))
                return;

            foreach (var preset in Presets ?? Enumerable.Empty<InputPreset>())
            {
                if (string.Equals(preset.TemplateName, oldName, StringComparison.OrdinalIgnoreCase))
                    preset.TemplateName = newName;
            }
            OnPropertyChanged(nameof(Presets));
        }

        private void RemoveTemplateFromReferences(string oldName)
        {
            if (string.IsNullOrWhiteSpace(oldName)) return;

            foreach (var preset in Presets ?? Enumerable.Empty<InputPreset>())
            {
                if (string.Equals(preset.TemplateName, oldName, StringComparison.OrdinalIgnoreCase))
                    preset.TemplateName = null;
            }
            OnPropertyChanged(nameof(Presets));
        }
        #endregion

        #region App Settings (timestamps)
        public SettingsViewModel Settings { get; } = new SettingsViewModel();

        public class SettingsViewModel : INotifyPropertyChanged
        {
            private bool _showTimestamps = true;
            public bool ShowTimestamps
            {
                get => _showTimestamps;
                set { if (_showTimestamps != value) { _showTimestamps = value; OnPropertyChanged(); } }
            }

            public event PropertyChangedEventHandler PropertyChanged;
            private void OnPropertyChanged([CallerMemberName] string name = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
        #endregion

        // Preset persistence
        private static readonly string PresetsPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "CmdRunnerPro", "presets.json");

        private static readonly string LastUsedPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "CmdRunnerPro", "lastused.json");

        // small DTOs for persistence
        private sealed class TokenSnapshot
        {
            public string SelectedCom1 { get; set; }
            public string SelectedCom2 { get; set; }
            public string Username { get; set; }
            public string Password { get; set; }
            public string Opco { get; set; }
            public string Program { get; set; }
            public string WorkingDirectory { get; set; }
        }

        private sealed class LastUsedState
        {
            public string PresetName { get; set; }
            public TokenSnapshot Tokens { get; set; }
        }

        private void EnsureDataDir()
        {
            var dir = Path.GetDirectoryName(PresetsPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        private void LoadPresetsFromDisk()
        {
            try
            {
                EnsureDataDir();
                if (!File.Exists(PresetsPath)) return;

                var list = JsonSerializer.Deserialize<List<InputPreset>>(File.ReadAllText(PresetsPath))
                           ?? new List<InputPreset>();

                Presets = new ObservableCollection<InputPreset>(list);
                OnPropertyChanged(nameof(Presets));
            }
            catch
            {
                // ignore
            }
        }

        private void SavePresetsToDisk()
        {
            try
            {
                EnsureDataDir();
                File.WriteAllText(PresetsPath,
                    JsonSerializer.Serialize(Presets, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
                // ignore
            }
        }
        private void DeletePreset()
        {
            if (SelectedPreset is null) return;

            var removedName = SelectedPreset.Name;
            Presets.Remove(SelectedPreset);

            // choose next available preset (if any)
            SelectedPreset = Presets.FirstOrDefault();

            SavePresetsToDisk();
            SaveLastUsed();
            RaiseCommandCanExecuteChanged();
        }

        private TokenSnapshot CaptureTokens() => new TokenSnapshot
        {
            SelectedCom1 = SelectedCom1,
            SelectedCom2 = SelectedCom2,
            Username = Username,
            Password = Password,
            Opco = Opco,
            Program = Program,
            WorkingDirectory = WorkingDirectory
        };

        private void ApplyTokens(TokenSnapshot t)
        {
            if (t == null) return;
            SelectedCom1 = t.SelectedCom1;
            SelectedCom2 = t.SelectedCom2;
            Username = t.Username;
            Password = t.Password;
            Opco = t.Opco;
            Program = t.Program;
            WorkingDirectory = t.WorkingDirectory;
            UpdateTemplatePreview();
        }

        private void SaveLastUsed()
        {
            try
            {
                EnsureDataDir();
                var state = new LastUsedState
                {
                    PresetName = SelectedPreset?.Name,
                    Tokens = CaptureTokens()
                };
                File.WriteAllText(LastUsedPath,
                    JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* ignore */ }
        }

        private void LoadLastUsed()
        {
            try
            {
                if (!File.Exists(LastUsedPath)) return;
                var state = JsonSerializer.Deserialize<LastUsedState>(File.ReadAllText(LastUsedPath));
                if (state == null) return;

                // If a preset name was saved and exists now, use it; otherwise use the tokens snapshot.
                if (!string.IsNullOrWhiteSpace(state.PresetName))
                {
                    var match = Presets?.FirstOrDefault(p => string.Equals(p.Name, state.PresetName,
                                         StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                    {
                        SelectedPreset = match;
                        LoadPreset(match);
                        return;
                    }
                }

                // Fallback to raw tokens (ad-hoc last session)
                ApplyTokens(state.Tokens);
            }
            catch { /* ignore */ }
        }
        private void SavePreset()
        {
            // Update currently selected preset if any; otherwise create a new one with a generated name
            if (SelectedPreset is InputPreset p)
            {
                FillPresetFromCurrent(p);
            }
            else
            {
                var pnew = new InputPreset { Name = GenerateUniquePresetName() };
                FillPresetFromCurrent(pnew);
                Presets.Add(pnew);
                SelectedPreset = pnew;
            }

            SavePresetsToDisk();
            SaveLastUsed();
            RaiseCommandCanExecuteChanged();
        }

        private void SavePresetAs()
        {
            // Save a copy regardless of SelectedPreset (create new)
            var pnew = new InputPreset { Name = GenerateUniquePresetName() };
            FillPresetFromCurrent(pnew);
            Presets.Add(pnew);
            SelectedPreset = pnew;

            SavePresetsToDisk();
            SaveLastUsed();
            RaiseCommandCanExecuteChanged();
        }

        private void FillPresetFromCurrent(InputPreset p)
        {
            p.TemplateName = SelectedTemplate?.Name;
            p.Com1 = SelectedCom1;
            p.Com2 = SelectedCom2;
            p.Username = Username;
            p.Password = Password;
            p.Opco = Opco;
            p.Program = Program;
            p.WorkingDirectory = WorkingDirectory;
        }

        private string GenerateUniquePresetName()
        {
            var baseName = "Preset";
            int i = 1;
            var existing = new HashSet<string>(Presets.Select(x => x.Name ?? ""), StringComparer.OrdinalIgnoreCase);
            string name = baseName;
            while (existing.Contains(name))
                name = $"{baseName} {++i}";
            return name;
        }
        public string SelectedCom1
        {
            get => _selectedCom1;
            set { if (Set(ref _selectedCom1, value)) { UpdateTemplatePreview(); SaveLastUsed(); } }
        }

        public string SelectedCom2
        {
            get => _selectedCom2;
            set { if (Set(ref _selectedCom2, value)) { UpdateTemplatePreview(); SaveLastUsed(); } }
        }

        public string Username
        {
            get => _username;
            set { if (Set(ref _username, value)) { UpdateTemplatePreview(); SaveLastUsed(); } }
        }

        public string Password
        {
            get => _password;
            set { if (Set(ref _password, value)) { UpdateTemplatePreview(); SaveLastUsed(); } }
        }

        public string Opco
        {
            get => _opco;
            set { if (Set(ref _opco, value)) { UpdateTemplatePreview(); SaveLastUsed(); } }
        }

        public string Program
        {
            get => _program;
            set { if (Set(ref _program, value)) { UpdateTemplatePreview(); SaveLastUsed(); } }
        }

        public string WorkingDirectory
        {
            get => _workingDirectory;
            set { if (Set(ref _workingDirectory, value)) { UpdateTemplatePreview(); SaveLastUsed(); } }
        }

        private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };

        // Choose where to store the template files:
        private static string TemplatesFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "CmdRunnerPro_v2", "Templates");

        private static string PathFor(Template t) => Path.Combine(TemplatesFolder, $"{t.Id}.json");

        private async Task SaveTemplateAsync(Template t)
        {
            Directory.CreateDirectory(TemplatesFolder);
            var path = PathFor(t);
            using var fs = File.Create(path);
            await JsonSerializer.SerializeAsync(fs, t, _json);
        }

        private Task DeleteTemplateAsync(Template t)
        {
            var path = PathFor(t);
            if (File.Exists(path)) File.Delete(path);
            return Task.CompletedTask;
        }

        private Task<bool> ConfirmAsync(string message)
        {
            var result = MessageBox.Show(message, "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            return Task.FromResult(result == MessageBoxResult.Yes);
        }

        // Optional: call at startup to load/refresh the list
        private async Task LoadTemplatesAsync()
        {
            Directory.CreateDirectory(TemplatesFolder);
            var items = new ObservableCollection<Template>();

            foreach (var file in Directory.EnumerateFiles(TemplatesFolder, "*.json"))
            {
                try
                {
                    await using var fs = File.OpenRead(file);
                    var model = await JsonSerializer.DeserializeAsync<Template>(fs);
                    if (model is not null)
                        items.Add(model);
                }
                catch { /* ignore bad files */ }
            }

            // Sort by Name (optional)
            Templates = new ObservableCollection<Template>(items.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase));
            SelectedTemplate = Templates.FirstOrDefault();
        }

    }
}
