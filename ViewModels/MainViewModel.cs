// ViewModels/MainViewModel.cs
using CmdRunnerPro.Models;           // Template, InputPreset
using CmdRunnerPro.Views;            // TemplateEditor (UserControl)
using MaterialDesignThemes.Wpf;      // DialogHost, BaseTheme, PaletteHelper
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
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace CmdRunnerPro.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        #region Fields
        private Process _currentProcess;
        private CancellationTokenSource _runCts;
        private readonly Dispatcher _uiDispatcher;

        // App settings (theme + timestamps)
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

            NewTemplateCommand = new RelayCommand(async () => await NewTemplateAsync());
            CloneTemplateCommand = new RelayCommand(async () => await CloneTemplateAsync(), () => SelectedTemplate != null);
            DeleteTemplateCommand = new RelayCommand(async () => await DeleteTemplateAsync(), () => SelectedTemplate != null);
            OpenTemplateEditorCommand = new RelayCommand(async () => await EditOrCreateTemplateAsync() );
            EditTemplateCommand = new RelayCommand(async () => await EditOrCreateTemplateAsync() );                                                         


            SavePresetCommand = new RelayCommand(SavePreset, () => true);
            //SavePresetAsCommand = new RelayCommand(SavePresetAs, () => true);
            DeletePresetCommand = new RelayCommand(DeletePreset, () => SelectedPreset != null);

            // in ctor
            SavePresetAsCommand = new RelayCommand(async () => await SavePresetAsAsync(), () => true);
            SavePresetCommand = new RelayCommand(SavePreset, () => true);

            // Initialize data
            RefreshPorts();
            _ = LoadTemplatesAsync();      // fire-and-forget; ctor cannot be async
            LoadPresetsFromDisk();
            LoadLastUsed();

            // Persisted theme (minimal & resilient)
            LoadThemeSettings();
            ApplyTheme();

            // Save theme when ShowTimestamps changes (kept in same file)
            Settings.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(SettingsViewModel.ShowTimestamps))
                    SaveThemeSettings();
            };

            // Initial preview
            UpdateTemplatePreview();

            // ... your existing ctor code ...
            _uiDispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
            // Initial preview
            UpdateTemplatePreview();
        }
        #endregion

        private void OnUI(Action action)
        {
            if (action == null) return;
            if (_uiDispatcher?.CheckAccess() == true) action();
            else _uiDispatcher?.Invoke(action);
        }

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
                    UpdateTemplatePreview(); // recompute preview
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

        /// <summary>Live output lines for the right-side ListBox.</summary>
        public ObservableCollection<string> OutputLines { get; } = new();

        private string _outputLog;
        /// <summary>Legacy whole-log string; kept for compatibility.</summary>
        public string OutputLog
        {
            get => _outputLog;
            set => Set(ref _outputLog, value);
        }

        private void AppendOutput(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            OnUI(() =>
            {
                string line = Settings?.ShowTimestamps == true
                    ? $"[{DateTime.Now:HH:mm:ss}] {text}"
                    : text;

                OutputLines.Add(line);
                OutputLog = string.IsNullOrEmpty(OutputLog)
                    ? line
                    : $"{OutputLog}{Environment.NewLine}{line}";
            });
        }
        #endregion

        #region Commands
        public ICommand LoadPresetCommand { get; }
        public ICommand RunCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand BrowseWorkingDirectoryCommand { get; }
        public ICommand OpenTemplateEditorCommand { get; }
        public ICommand EditTemplateCommand { get; }   // <— alias for XAML that still uses "Edit"
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
            (EditTemplateCommand as RelayCommand)?.RaiseCanExecuteChanged();   // <— add this line
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
            SaveLastUsed();
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

                var command = ExpandTokens(SelectedTemplate.TemplateText, BuildTokenResolver(maskPassword: false));
                var wd = string.IsNullOrWhiteSpace(WorkingDirectory) ? Environment.CurrentDirectory : WorkingDirectory;

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
                    if (e.Data != null) AppendOutput(e.Data); // this already marshals
                };

                _currentProcess.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data != null) AppendOutput("[ERR] " + e.Data); // marshaled
                };

                _currentProcess.Exited += (_, __) =>
                {
                    // Exited is raised on a non-UI thread; ensure both the output and the command requery are on UI
                    OnUI(() =>
                    {
                        AppendOutput($"--- Process exited (Code: {_currentProcess.ExitCode}) ---");
                        RaiseCommandCanExecuteChanged();
                    });
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
        private async Task EditOrCreateTemplateAsync()
        {
            // If nothing is selected, create an in-memory draft (not added/persisted until user saves)
            bool isNew = SelectedTemplate is null;
            var template = isNew
                ? new Template { Name = UniqueName("New Template"), TemplateText = "" }
                : SelectedTemplate;
            var originalName = template.Name;

            var vm = new TemplateEditorViewModel(
                originalTemplate: template,
                name: template.Name,
                template: template.TemplateText,
                comPortOptions: ComPortOptions,
                nameExists: name =>
                {
                    // If editing an existing item, exclude that one from duplicate checks; otherwise check all
                    if (!isNew)
                        return Templates.Any(t => !ReferenceEquals(t, template) &&
                                                  string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
                    return Templates.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
                });

            var view = new TemplateEditor { DataContext = vm };

            // Persist operations WHILE the editor stays open
            async void OnOperationRequested(TemplateEditorResult r)
            {
                switch (r.Action)
                {
                    case TemplateEditorResult.EditorAction.Saved:
                        {
                            // VM already applied its working copy to 'template'
                            await SaveTemplateAsync(template, originalName);
                            originalName = template.Name; // in case it was renamed

                            Templates = new ObservableCollection<Template>(
                                Templates.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase));
                            Reselect(template.Name);
                            break;
                        }

                    case TemplateEditorResult.EditorAction.SavedAs:
                        {
                            var newName = UniqueName(string.IsNullOrWhiteSpace(r.Name) ? "New Template" : r.Name.Trim());
                            var copy = new Template
                            {
                                Name = newName,
                                TemplateText = r.TemplateText ?? string.Empty
                            };
                            await SaveTemplateAsync(copy);
                            Templates.Add(copy);
                            Reselect(copy.Name);
                            break;
                        }

                    case TemplateEditorResult.EditorAction.Deleted:
                        {
                            if (!isNew)
                            {
                                if (!await ConfirmAsync($"Delete \"{template.Name}\"?")) return;

                                await DeleteTemplateByNameAsync(template.Name);
                                Templates.Remove(template);
                                SelectedTemplate = Templates.FirstOrDefault();
                            }
                            break;
                        }
                }
            }

            // Subscribe, show editor, then unsubscribe (no duplicate 'result' vars)
            vm.OperationRequested += OnOperationRequested;
            await MaterialDesignThemes.Wpf.DialogHost.Show(view, "RootDialog");
            vm.OperationRequested -= OnOperationRequested;

            // Optional: keep ComboBox sorted after the editor closes
            Templates = new ObservableCollection<Template>(
                Templates.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase));
        }

        #endregion

        #region Template CRUD helpers (New / Clone / Delete) - name-based storage
        private async Task NewTemplateAsync()
        {
            var draft = new Template { Name = UniqueName("New Template"), TemplateText = "" };

            var vm = new TemplateEditorViewModel(
                originalTemplate: draft,
                name: draft.Name,
                template: draft.TemplateText,
                comPortOptions: ComPortOptions,
                nameExists: name => Templates.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
            );

            var view = new TemplateEditor { DataContext = vm };
            var result = await DialogHost.Show(view, "RootDialog");

            if (result is TemplateEditorResult r)
            {
                switch (r.Action)
                {
                    case TemplateEditorResult.EditorAction.Saved:
                        {
                            await SaveTemplateAsync(draft, originalName: null);
                            Templates.Add(draft);
                            Reselect(draft.Name);
                            break;
                        }
                    case TemplateEditorResult.EditorAction.SavedAs:
                        {
                            var newName = UniqueName(string.IsNullOrWhiteSpace(r.Name) ? "New Template" : r.Name.Trim());
                            var copy = new Template { Name = newName, TemplateText = r.TemplateText ?? string.Empty };
                            await SaveTemplateAsync(copy);
                            Templates.Add(copy);
                            Reselect(copy.Name);
                            break;
                        }
                    default:
                        break;
                }
            }

            Templates = new ObservableCollection<Template>(
                Templates.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase));
        }

        private async Task CloneTemplateAsync()
        {
            if (SelectedTemplate is null) return;

            var baseName = SelectedTemplate.Name;
            var proposal = UniqueName(baseName + " (Copy)");

            var clone = new Template
            {
                Name = proposal,
                TemplateText = SelectedTemplate.TemplateText
            };

            var vm = new TemplateEditorViewModel(
                originalTemplate: clone,
                name: clone.Name,
                template: clone.TemplateText,
                comPortOptions: ComPortOptions,
                nameExists: name => Templates.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
            );

            var view = new TemplateEditor { DataContext = vm };
            var result = await DialogHost.Show(view, "RootDialog");

            if (result is TemplateEditorResult r)
            {
                switch (r.Action)
                {
                    case TemplateEditorResult.EditorAction.Saved:
                        {
                            await SaveTemplateAsync(clone, originalName: null);
                            Templates.Add(clone);
                            Reselect(clone.Name);
                            break;
                        }
                    case TemplateEditorResult.EditorAction.SavedAs:
                        {
                            var newName = UniqueName(string.IsNullOrWhiteSpace(r.Name) ? "New Template" : r.Name.Trim());
                            var copy = new Template { Name = newName, TemplateText = r.TemplateText ?? string.Empty };
                            await SaveTemplateAsync(copy);
                            Templates.Add(copy);
                            Reselect(copy.Name);
                            break;
                        }
                    default:
                        break;
                }
            }

            Templates = new ObservableCollection<Template>(
                Templates.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase));
        }

        private async Task DeleteTemplateAsync()
        {
            if (SelectedTemplate is null) return;

            if (!await ConfirmAsync($"Delete template \"{SelectedTemplate.Name}\"?")) return;

            var oldName = SelectedTemplate.Name;
            await DeleteTemplateByNameAsync(oldName);
            Templates.Remove(SelectedTemplate);
            RemoveTemplateFromReferences(oldName);
            SelectedTemplate = Templates.FirstOrDefault();
            UpdateTemplatePreview();
        }
        #endregion

        private const char MaskChar = '●';
        private static string Mask(string s) => string.IsNullOrEmpty(s) ? string.Empty : new string(MaskChar, s.Length);

        #region Token Expansion
        // Matches {token} and {Q:token}
        private static readonly Regex TokenRegex = new(@"\{(Q:)?([^\}]+)\}", RegexOptions.Compiled);

        private string ExpandTokens(string template, Func<string, string> resolver)
        {
            if (string.IsNullOrEmpty(template)) return string.Empty;

            return TokenRegex.Replace(template, m =>
            {
                bool quote = m.Groups[1].Success;
                string rawKey = m.Groups[2].Value.Trim();
                string value = resolver(rawKey);
                if (value == null) return m.Value;
                if (quote && value.Contains(' ')) return $"\"{value}\"";
                return value;
            });
        }

        private Func<string, string> BuildTokenResolver(bool maskPassword)
        {
            var wd = string.IsNullOrWhiteSpace(WorkingDirectory) ? Environment.CurrentDirectory : WorkingDirectory;

            return key =>
            {
                switch (key)
                {
                    case "comport1":
                    case "COM1":
                    case "FIELD3": return SelectedCom1 ?? "";
                    case "comport2":
                    case "COM2":
                    case "FIELD4": return SelectedCom2 ?? "";
                    case "username":
                    case "FIELD5": return Username ?? "";
                    case "password":
                    case "FIELD6": return maskPassword ? Mask(Password ?? "") : (Password ?? "");
                    case "opco": return Opco ?? "";
                    case "program": return Program ?? "";
                    case "wd":
                    case "WD": return wd ?? "";
                    default: return null;
                }
            };

        }



        private void UpdateTemplatePreview()
        {
            var t = SelectedTemplate?.TemplateText ?? string.Empty;
            TemplateContent = string.IsNullOrWhiteSpace(t) ? string.Empty
                            : ExpandTokens(t, BuildTokenResolver(maskPassword: true));
        }


        #endregion

        #region Theme (minimal + resilient)
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

        private string _primaryColor = "Blue";   // default MD Blue 500 (#2196F3)
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

        private string _secondaryColor = "Amber"; // Material Amber 500 (#FFC107)
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
                var helper = new PaletteHelper();
                var theme = helper.GetTheme();

                theme.SetBaseTheme(IsDarkTheme ? BaseTheme.Dark : BaseTheme.Light);

                // Resolve colors from known names or hex; fallback to MD defaults
                static Color ResolveColor(string name, string fallbackHex)
                {
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            if (name.StartsWith("#"))
                                return (Color)ColorConverter.ConvertFromString(name);

                            // Try WPF named colors
                            return (Color)ColorConverter.ConvertFromString(name);
                        }
                    }
                    catch { /* fall through */ }
                    return (Color)ColorConverter.ConvertFromString(fallbackHex);
                }

                // MD Blue 500 = #2196F3, MD Amber 500 = #FFC107
                var primary = ResolveColor(PrimaryColor, "#2196F3");
                var secondary = ResolveColor(SecondaryColor, "#FFC107");

                theme.SetPrimaryColor(primary);
                theme.SetSecondaryColor(secondary);

                helper.SetTheme(theme);
            }
            catch
            {
                // Swallow palette errors; keep current theme.
            }
        }

        // Theme settings persisted in a simple cfg file (local appdata)
        private static readonly string ThemeSettingsPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "CmdRunnerPro", "theme.cfg");

        private void LoadThemeSettings()
        {
            try
            {
                if (!File.Exists(ThemeSettingsPath)) return;
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
            }
            catch { /* ignore */ }
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
            catch { /* ignore */ }
        }
        #endregion

        #region Settings ViewModel (timestamps toggle)
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

        #region Theme / Ports init
        private void RefreshPorts()
        {
            var ports = SerialPort.GetPortNames()
                                  .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                                  .ToList();
            ComPortOptions = new ObservableCollection<string>(ports);
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

        #region Template reference maintenance (Presets linkage)
        private void RenameTemplateInReferences(string oldName, string newName)
        {
            if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName)) return;

            foreach (var preset in Presets ?? Enumerable.Empty<InputPreset>())
                if (string.Equals(preset.TemplateName, oldName, StringComparison.OrdinalIgnoreCase))
                    preset.TemplateName = newName;

            OnPropertyChanged(nameof(Presets));
        }

        private void RemoveTemplateFromReferences(string oldName)
        {
            if (string.IsNullOrWhiteSpace(oldName)) return;

            foreach (var preset in Presets ?? Enumerable.Empty<InputPreset>())
                if (string.Equals(preset.TemplateName, oldName, StringComparison.OrdinalIgnoreCase))
                    preset.TemplateName = null;

            OnPropertyChanged(nameof(Presets));
        }
        #endregion

        #region Preset persistence (unchanged)
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
            catch { /* ignore */ }
        }

        private void SavePresetsToDisk()
        {
            try
            {
                EnsureDataDir();
                File.WriteAllText(PresetsPath,
                    JsonSerializer.Serialize(Presets, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* ignore */ }
        }

        private void DeletePreset()
        {
            if (SelectedPreset is null) return;

            var removedName = SelectedPreset.Name;
            Presets.Remove(SelectedPreset);
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

                if (!string.IsNullOrWhiteSpace(state.PresetName))
                {
                    var match = Presets?.FirstOrDefault(p =>
                        string.Equals(p.Name, state.PresetName, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                    {
                        SelectedPreset = match;
                        LoadPreset(match);
                        return;
                    }
                }

                // Fallback to raw tokens
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

        private async Task SavePresetAsAsync()
        {
            // Suggest a name
            var suggestion =
                !string.IsNullOrWhiteSpace(SelectedPreset?.Name)
                ? UniquePresetName($"{SelectedPreset.Name} (Copy)")
                : GenerateUniquePresetName();

            // Show the prompt
            var prompt = new CmdRunnerPro.Views.NamePrompt
            {
                Title = "Save Preset As",
                Prompt = "Preset name",
                Text = suggestion
            };
            var result = await MaterialDesignThemes.Wpf.DialogHost.Show(prompt, "RootDialog");

            // If user cancelled -> result is null; ignore
            if (result is not string raw) return;

            var name = raw.Trim();
            if (string.IsNullOrWhiteSpace(name)) return;

            // Ensure unique (in case user typed a duplicate)
            name = UniquePresetName(name);

            // Create + persist new preset with that name
            var pnew = new InputPreset { Name = name };
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
        #endregion

        #region Template storage (JSON per file, name-based, rename-aware)
        private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };

        // %AppData%\CmdRunnerPro_v2\Templates
        private static string TemplatesFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "CmdRunnerPro_v2", "Templates");

        private static string PathForName(string name) =>
            Path.Combine(TemplatesFolder, $"{ToSafeFileName(name)}.json");

        private static string ToSafeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string((name ?? "Template")
                .Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
            var normalized = string.Join(" ", cleaned.Split(new[] { ' ' },
                StringSplitOptions.RemoveEmptyEntries));
            return string.IsNullOrWhiteSpace(normalized) ? "Template" : normalized;
        }

        /// <summary>Save or rename a template. Pass originalName when Name changed.</summary>
        private async Task SaveTemplateAsync(Template t, string? originalName = null)
        {
            if (t is null) return;
            Directory.CreateDirectory(TemplatesFolder);

            if (!string.IsNullOrWhiteSpace(originalName) &&
                !originalName.Equals(t.Name, StringComparison.OrdinalIgnoreCase))
            {
                var oldPath = PathForName(originalName);
                if (File.Exists(oldPath)) File.Delete(oldPath);
            }

            var path = PathForName(t.Name);
            using var fs = File.Create(path);
            await JsonSerializer.SerializeAsync(fs, t, _json);
        }

        private Task DeleteTemplateByNameAsync(string name)
        {
            var path = PathForName(name);
            if (File.Exists(path)) File.Delete(path);
            return Task.CompletedTask;
        }

        /// <summary>Load all templates from disk into the ComboBox (sorted by Name).</summary>
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
                    if (model is not null) items.Add(model);
                }
                catch { /* ignore bad files */ }
            }

            Templates = new ObservableCollection<Template>(
                items.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase));

            SelectedTemplate = Templates.FirstOrDefault();
        }

        /// <summary>Ensure a unique name against the current in-memory list.</summary>
        private string UniqueName(string baseName)
        {
            if (!Templates.Any(t => t.Name.Equals(baseName, StringComparison.OrdinalIgnoreCase)))
                return baseName;

            int n = 1;
            while (true)
            {
                var candidate = n == 1 ? $"{baseName} (Copy)" : $"{baseName} (Copy {n})";
                if (!Templates.Any(t => t.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
                    return candidate;
                n++;
            }
        }

        private Task<bool> ConfirmAsync(string message)
        {
            var result = MessageBox.Show(message, "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            return Task.FromResult(result == MessageBoxResult.Yes);
        }

        private void Reselect(string name)
        {
            SelectedTemplate = Templates.FirstOrDefault(t =>
                t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }
        #endregion
        private string UniquePresetName(string baseName)
        {
            var existing = new HashSet<string>(Presets.Select(x => x?.Name ?? ""), StringComparer.OrdinalIgnoreCase);
            if (!existing.Contains(baseName)) return baseName;
            int i = 1;
            string candidate;
            do { candidate = $"{baseName} ({i++})"; } while (existing.Contains(candidate));
            return candidate;
        }
    }
}