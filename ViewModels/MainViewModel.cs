// ViewModels/MainViewModel.cs
using MMCore.Models;           // Template, InputPreset
using MMCore.Services;
using MMCore.Utilities;
using MMCore.Views;            // TemplateEditor (UserControl)
using MaterialDesignThemes.Wpf;      // DialogHost, BaseTheme, PaletteHelper
using Ookii.Dialogs.Wpf;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Data.Common;
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

namespace MMCore.ViewModels
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

        // Simple tab fields
        private string _selectedCom1Left = "";
        private string _selectedCom1Center = "";
        private string _selectedCom2Center = "";
        private string _selectedCom2Right = "";
        private string _programLeft = "";
        private string _programCenter = "";
        private string _programRight = "";
        #endregion

        #region Ctor
        public MainViewModel()
        {
            // Resolve settings paths
            _configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MMCore");
            _themeSettingsPath = Path.Combine(_configDir, "theme.json");

            // Commands
            LoadPresetCommand = new RelayCommand<InputPreset?>(p => LoadPreset(p ?? SelectedPreset), _ => SelectedPreset != null);
            RunCommand = new RelayCommand(async () => await RunSelectedAsync(), () => SelectedTemplate != null);
            StopCommand = new RelayCommand(Stop, () => _currentProcess != null && !_currentProcess.HasExited);
            BrowseWorkingDirectoryCommand = new RelayCommand(BrowseWorkingDirectory);

            NewTemplateCommand = new RelayCommand(async () => await NewTemplateAsync());
            CloneTemplateCommand = new RelayCommand(async () => await CloneTemplateAsync(), () => SelectedTemplate != null);
            DeleteTemplateCommand = new RelayCommand(async () => await DeleteTemplateAsync(), () => SelectedTemplate != null);
            OpenTemplateEditorCommand = new RelayCommand(async () => await EditOrCreateTemplateAsync());
            EditTemplateCommand = new RelayCommand(async () => await EditOrCreateTemplateAsync());

            SavePresetCommand = new RelayCommand(SavePreset, () => true);
            DeletePresetCommand = new RelayCommand(DeletePreset, () => SelectedPreset != null);
            SavePresetAsCommand = new RelayCommand(async () => await SavePresetAsAsync(), () => true);
            SavePresetCommand = new RelayCommand(SavePreset, () => true);

            // View toggle
            ToggleViewModeCommand = new RelayCommand(() => IsAdvancedMode = !IsAdvancedMode);

            ClearOutputCommand = new RelayCommand(ClearOutput);
            ClearCommand = new RelayCommand(ClearPreview);
            ResetCommand = new RelayCommand<string>(ExecuteReset);

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

        private bool _showDetailedOutput = false; // default to detailed
        public bool ShowDetailedOutput
        {
            get => _showDetailedOutput;
            set => Set(ref _showDetailedOutput, value);
        }

        private string _currentCommand;
        public string CurrentCommand
        {
            get => _currentCommand;
            set => Set(ref _currentCommand, value);
        }

        #endregion

        #region Simple Tab Properties
        public string SelectedCom1Left
        {
            get => _selectedCom1Left;
            set
            {
                if (_selectedCom1Left != value)
                {
                    _selectedCom1Left = value;
                    OnPropertyChanged();
                }
            }
        }

        public string SelectedCom1Center
        {
            get => _selectedCom1Center;
            set
            {
                if (_selectedCom1Center != value)
                {
                    _selectedCom1Center = value;
                    OnPropertyChanged();
                }
            }
        }

        public string SelectedCom2Center
        {
            get => _selectedCom2Center;
            set
            {
                if (_selectedCom2Center != value)
                {
                    _selectedCom2Center = value;
                    OnPropertyChanged();
                }
            }
        }

        public string SelectedCom2Right
        {
            get => _selectedCom2Right;
            set
            {
                if (_selectedCom2Right != value)
                {
                    _selectedCom2Right = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ProgramLeft
        {
            get => _programLeft;
            set
            {
                if (_programLeft != value)
                {
                    _programLeft = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ProgramCenter
        {
            get => _programCenter;
            set
            {
                if (_programCenter != value)
                {
                    _programCenter = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ProgramRight
        {
            get => _programRight;
            set
            {
                if (_programRight != value)
                {
                    _programRight = value;
                    OnPropertyChanged();
                }
            }
        }

        private void ClearOutput()
        {
            OutputLines.Clear();
        }

        private void ClearPreview()
        {
            SelectedTemplate = null;  // Clears the preview by deselecting the template
        }

        // Simple Tab Button Controls
        private async void ExecuteReset(string param)
        {
            var parts = param.Split(',');
            if (parts.Length != 2) return;
            string buttonType = parts[0];
            string column = parts[1];

            string com1 = "", com2 = "", prog = "";
            switch (column)
            {
                case "Left":
                    com1 = SelectedCom1Left;
                    prog = ProgramLeft;
                    break;
                case "Center":
                    com1 = SelectedCom1Center;
                    com2 = SelectedCom2Center;
                    prog = ProgramCenter;
                    break;
                case "Right":
                    com2 = SelectedCom2Right;
                    prog = ProgramRight;
                    break;
            }

            var tokens = new Dictionary<string, string?>
            {
                {"comport1", com1},
                {"comport2", com2},
                {"username", Username},
                {"password", Password},
                {"opco", Opco},
                {"program", prog},
                {"wd", WorkingDirectory}
            };


            List<string> commands = new();
            string key = buttonType + (column == "Center" ? "Both" : column);
            switch (key)
            {
                case "MasterResetLeft":
                    commands.AddRange(new[] {
                "START /WAIT MeterMate {username} {password} {opco} /ComPort {comport1}",
                "START /WAIT MeterMate {username} {password} {opco} /OPTOBAUDRATE 9600 /Master"
            });
                    break;
                case "DemandResetLeft":
                    commands.AddRange(new[] {
                "START /WAIT MeterMate {username} {password} {opco} /ComPort {comport1}",
                "START /WAIT MeterMate {username} {password} {opco} /OPTOBAUDRATE 9600 /Demand"
            });
                    break;
                case "MasterDemandResetLeft":
                    commands.AddRange(new[] {
                "START /WAIT MeterMate {username} {password} {opco} /ComPort {comport1}",
                "START /WAIT MeterMate {username} {password} {opco} /OPTOBAUDRATE 9600 /Master",
                "START /WAIT MeterMate {username} {password} {opco} /ComPort {comport1}",
                "START /WAIT MeterMate {username} {password} {opco} /OPTOBAUDRATE 9600 /Demand"
            });
                    break;
                case "SwitchOpenLeft":
                    commands.AddRange(new[] {
                "START /WAIT MeterMate {username} {password} {opco} /ComPort {comport1}",
                "START /WAIT MeterMate {username} {password} {opco} /OPTOBAUDRATE 9600 /COMMAND /STATE Open"
            });
                    break;
                case "SwitchClosedLeft":
                    commands.AddRange(new[] {
                "START /WAIT MeterMate {username} {password} {opco} /ComPort {comport1}",
                "START /WAIT MeterMate {username} {password} {opco} /OPTOBAUDRATE 9600 /COMMAND /STATE Close"
            });
                    break;
                case "ProgramLeft":
                    commands.AddRange(new[] {
                "START /WAIT MeterMate {username} {password} {opco} /ComPort {comport1}",
                "START /WAIT MeterMate {username} {password} {opco} /OPTOBAUDRATE 9600 /Program /PRO {Q:program} /MID 000000000000000000 /TRD Yes"
            });
                    break;
                case "MasterResetBoth":
                    commands.AddRange(new[] {
                "START /WAIT MeterMate {username} {password} {opco} /ComPort {comport1}",
                "START /WAIT MeterMate {username} {password} {opco} /OPTOBAUDRATE 9600 /Master",
                "START /WAIT MeterMate {username} {password} {opco} /ComPort {comport2}",
                "START /WAIT MeterMate {username} {password} {opco} /OPTOBAUDRATE 9600 /Master"
            });
                    break;
                case "DemandResetBoth":
                    commands.AddRange(new[] {
                "START /WAIT MeterMate {username} {password} {opco} /ComPort {comport1}",
                "START /WAIT MeterMate {username} {password} {opco} /OPTOBAUDRATE 9600 /Demand",
                "START /WAIT MeterMate {username} {password} {opco} /ComPort {comport2}",
                "START /WAIT MeterMate {username} {password} {opco} /OPTOBAUDRATE 9600 /Demand"
            });
                    break;
                case "MasterDemandResetBoth":
                    commands.AddRange(new[] {
                "START /WAIT MeterMate {username} {password} {opco} /ComPort {comport1}",
                "START /WAIT MeterMate {username} {password} {opco} /OPTOBAUDRATE 9600 /Master",
                "START /WAIT MeterMate {username} {password} {opco} /ComPort {comport1}",
                "START /WAIT MeterMate {username} {password} {opco} /OPTOBAUDRATE 9600 /Demand",
                "START /WAIT MeterMate {username} {password} {opco} /ComPort {comport2}",
                "START /WAIT MeterMate {username} {password} {opco} /OPTOBAUDRATE 9600 /Master",
                "START /WAIT MeterMate {username} {password} {opco} /ComPort {comport2}",
                "START /WAIT MeterMate {username} {password} {opco} /OPTOBAUDRATE 9600 /Demand"
            });
                    break;
                case "SwitchOpenBoth":
                    commands.AddRange(new[] {
                "START /WAIT MeterMate {username} {password} {opco} /ComPort {comport1}",
                "START /WAIT MeterMate {username} {password} {opco} /OPTOBAUDRATE 9600 /COMMAND /STATE Open",
                "START /WAIT MeterMate {username} {password} {opco} /ComPort {comport2}",
                "START /WAIT MeterMate {username} {password} {opco} /OPTOBAUDRATE 9600 /COMMAND /STATE Open"
            });
                    break;
                case "SwitchClosedBoth":
                    commands.AddRange(new[] {
                "START /WAIT MeterMate {username} {password} {opco} /ComPort {comport1}",
                "START /WAIT MeterMate {username} {password} {opco} /OPTOBAUDRATE 9600 /COMMAND /STATE Close",
                "START /WAIT MeterMate {username} {password} {opco} /ComPort {comport2}",
                "START /WAIT MeterMate {username} {password} {opco} /OPTOBAUDRATE 9600 /COMMAND /STATE Close"
            });
                    break;
                case "ProgramBoth":
                    commands.AddRange(new[] {
                "START /WAIT MeterMate {username} {password} {opco} /ComPort {comport1}",
                "START /WAIT MeterMate {username} {password} {opco} /OPTOBAUDRATE 9600 /Program /PRO {Q:program} /MID 000000000000000000 /TRD Yes",
                "START /WAIT MeterMate {username} {password} {opco} /ComPort {comport2}",
                "START /WAIT MeterMate {username} {password} {opco} /OPTOBAUDRATE 9600 /Program /PRO {Q:program} /MID 000000000000000000 /TRD Yes"
            });
                    break;
                case "MasterResetRight":
                    commands.AddRange(new[] {
                "START /WAIT MeterMate {username} {password} {opco} /ComPort {comport2}",
                "START /WAIT MeterMate {username} {password} {opco} /OPTOBAUDRATE 9600 /Master"
            });
                    break;
                case "DemandResetRight":
                    commands.AddRange(new[] {
                "START /WAIT MeterMate {username} {password} {opco} /ComPort {comport2}",
                "START /WAIT MeterMate {username} {password} {opco} /OPTOBAUDRATE 9600 /Demand"
            });
                    break;
                case "MasterDemandResetRight":
                    commands.AddRange(new[] {
                "START /WAIT MeterMate {username} {password} {opco} /ComPort {comport2}",
                "START /WAIT MeterMate {username} {password} {opco} /OPTOBAUDRATE 9600 /Master",
                "START /WAIT MeterMate {username} {password} {opco} /ComPort {comport2}",
                "START /WAIT MeterMate {username} {password} {opco} /OPTOBAUDRATE 9600 /Demand"
            });
                    break;
                case "SwitchOpenRight":
                    commands.AddRange(new[] {
                "START /WAIT MeterMate {username} {password} {opco} /ComPort {comport2}",
                "START /WAIT MeterMate {username} {password} {opco} /OPTOBAUDRATE 9600 /COMMAND /STATE Open"
            });
                    break;
                case "SwitchClosedRight":
                    commands.AddRange(new[] {
                "START /WAIT MeterMate {username} {password} {opco} /ComPort {comport2}",
                "START /WAIT MeterMate {username} {password} {opco} /OPTOBAUDRATE 9600 /COMMAND /STATE Close"
            });
                    break;
                case "ProgramRight":
                    commands.AddRange(new[] {
                "START /WAIT MeterMate {username} {password} {opco} /ComPort {comport2}",
                "START /WAIT MeterMate {username} {password} {opco} /OPTOBAUDRATE 9600 /Program /PRO {Q:program} /MID 000000000000000000 /TRD Yes"
            });
                    break;
                default:
                    commands.Add("echo 'Command not defined'");
                    break;
            }

            if (!ShowDetailedOutput)
            {
                AppendOutput($"{buttonType.Replace("Reset", " Reset").Replace("Program", " Program").Replace("Switch", " Switch")} {(column == "Center" ? "Both" : column)}");
            }

            foreach (var cmdTemplate in commands)
            {
                string expandedCmd = TemplateEngine.Expand(cmdTemplate, tokens);
                await RunCustomAsync(expandedCmd, ShowDetailedOutput);
            }
        }

        private async Task RunCustomAsync(string command, bool showDetailed = true)
        {
            if (string.IsNullOrWhiteSpace(command)) return;

            try
            {
                // Show the command in detailed mode (like CommandRunner)
                if (showDetailed)
                {
                    OutputLines.Add("> " + command);
                }

                _runCts?.Cancel();
                _runCts = new CancellationTokenSource();

                _currentProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = "/c " + command,
                        WorkingDirectory = WorkingDirectory,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                _currentProcess.OutputDataReceived += (s, e) =>
                {
                    if (showDetailed) OnUI(() => OutputLines.Add(e.Data ?? ""));
                };
                _currentProcess.ErrorDataReceived += (s, e) =>
                {
                    if (showDetailed) OnUI(() => OutputLines.Add($"ERROR: {e.Data ?? ""}"));
                };

                _currentProcess.Start();
                _currentProcess.BeginOutputReadLine();
                _currentProcess.BeginErrorReadLine();

                await _currentProcess.WaitForExitAsync(_runCts.Token);

                // Show exit code in detailed mode (like CommandRunner)
                if (showDetailed)
                {
                    OutputLines.Add($"[exit {_currentProcess.ExitCode}]");
                }
            }
            catch (Exception ex)
            {
                if (showDetailed || ex.Message.Contains("cannot find the file", StringComparison.OrdinalIgnoreCase))
                {
                    OutputLines.Add($"Error: {ex.Message}");
                }
            }
            finally
            {
                _currentProcess?.Dispose();
                _currentProcess = null;
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

                OutputLines.Add(line); // ✅ FIXED: no nested OnUI
                OutputLog = string.IsNullOrEmpty(OutputLog)
                    ? line
                    : $"{OutputLog}{Environment.NewLine}{line}";
            });
        }

        private bool _isAdvancedMode = true;
        public bool IsAdvancedMode
        {
            get => _isAdvancedMode;
            set => Set(ref _isAdvancedMode, value);
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
        public ICommand ToggleViewModeCommand { get; }

        public RelayCommand ClearOutputCommand { get; private set; }
        public RelayCommand ClearCommand { get; private set; }
        public RelayCommand<string> ResetCommand { get; private set; }

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
            ClearOutputCommand?.RaiseCanExecuteChanged();
            ClearCommand?.RaiseCanExecuteChanged();
            ResetCommand?.RaiseCanExecuteChanged();
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

        // Build (Command, Display) pairs per line from the template
        /// <summary>
        /// Turns a template into a sequence of items: the command to run, a verbose display (full command),
        /// and a friendly display (description = masked command). Skips blanks and comment lines.
        /// Supports "Description = command" syntax per line.
        /// </summary>
        private IEnumerable<(string Command, string DisplayVerbose, string DisplayFriendly)>
            BuildCommandQueueFromTemplate(Template template)
        {
            var text = template?.TemplateText ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
                yield break;

            // Expanders: one for actual run (unmasked), one for display (masked password)
            var expandRun = BuildTokenResolver(maskPassword: false);
            var expandShown = BuildTokenResolver(maskPassword: true);

            var lines = text.Replace("\r", "").Split('\n', StringSplitOptions.None);
            foreach (var raw in lines)
            {
                var line = raw?.Trim();
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                // Skip comments
                var head = line.TrimStart();
                if (head.StartsWith("#") ||
                    head.StartsWith("//", StringComparison.Ordinal) ||
                    head.StartsWith("::", StringComparison.Ordinal) ||
                    head.StartsWith("REM ", StringComparison.OrdinalIgnoreCase))
                    continue;

                string description = null;
                string cmdText = line;

                // If author provided "Description = command"
                int eq = line.IndexOf('=');
                if (eq > 0)
                {
                    description = line.Substring(0, eq).Trim();
                    cmdText = line.Substring(eq + 1).Trim();
                }

                // Build both flavors

                if (TrySplitFriendly(line, out var desc, out var cmd))
                {
                    description = desc;
                    cmdText = cmd;
                }

                var expandedForRun = ExpandTokens(cmdText, BuildTokenResolver(maskPassword: false));
                var expandedForDisplay = ExpandTokens(cmdText, BuildTokenResolver(maskPassword: true));

                string displayVerbose = expandedForRun;
                string displayFriendly = displayFriendly = description ?? expandedForDisplay;

                yield return (expandedForRun, displayVerbose, displayFriendly);

            }
        }

        #region Run / Stop

        private async Task RunSelectedAsync()
        {
            if (SelectedTemplate == null) return;
            _runCts?.Cancel();
            _runCts = new CancellationTokenSource();

            try
            {
                AppendOutput($"--- Running template: {SelectedTemplate.Name} ---");

                // Build per-line queue (command + two display modes)
                var commandItems = BuildCommandQueueFromTemplate(SelectedTemplate).ToList();
                if (commandItems.Count == 0)
                {
                    AppendOutput("[warn] Template contains no runnable commands.");
                    return;
                }

                AppendOutput($"[info] {commandItems.Count} command(s) to run…");

                var wd = string.IsNullOrWhiteSpace(WorkingDirectory)
                    ? Environment.CurrentDirectory
                    : WorkingDirectory;

                var runner = new CommandRunner();

                bool success = await runner.RunQueueAsync(
                    commandItems,
                    wd,
                    stopOnError: false,
                    showDetailed: ShowDetailedOutput,               // 👈 pass the toggle
                    new Progress<CommandOutputWithState>(report =>
                    {
                        if (ShowDetailedOutput)
                        {
                            CurrentCommand = report.CurrentCommand;
                            AppendOutput(report.Output.Line);
                        }
                        else
                        {
                            // Simple mode: show command once per unique command
                            if (CurrentCommand != report.CurrentCommand)
                            {
                                CurrentCommand = report.CurrentCommand;
                                AppendOutput($"Running command: {report.CurrentCommand}");
                            }
                        }
                    }),
                    _runCts.Token
                );

                // Show completion message based on detailed mode
                if (ShowDetailedOutput)
                {
                    AppendOutput(success ? "--- All commands completed ---" : "--- Execution stopped due to error ---");
                }
                else
                {
                    AppendOutput(success ? "--- Commands completed ---" : "--- Execution stopped ---");
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

        public ObservableCollection<string> MdixPrimaryColors { get; } = new(new[] { "Red", "Pink", "Purple", "DeepPurple", "Indigo", "Blue", "LightBlue", "Cyan", "Teal", "Green", "LightGreen", "Lime", "Yellow", "Amber", "Orange", "DeepOrange", "Brown", "BlueGrey", "Grey" });
        public ObservableCollection<string> MdixSecondaryColors { get; } = new(new[] { "Red", "Pink", "Purple", "DeepPurple", "Indigo", "Blue", "LightBlue", "Cyan", "Teal", "Green", "LightGreen", "Lime", "Yellow", "Amber", "Orange", "DeepOrange" });

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
        private static readonly string ThemeSettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MMCore", "theme.cfg");

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

                var lines = new[] { $"IsDarkTheme={IsDarkTheme}", $"PrimaryColor={PrimaryColor}", $"SecondaryColor={SecondaryColor}" };
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
            var ports = SerialPort.GetPortNames().OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
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
        private static readonly string PresetsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MMCore", "presets.json");
        private static readonly string LastUsedPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MMCore", "lastused.json");

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

                var list = JsonSerializer.Deserialize<List<InputPreset>>(File.ReadAllText(PresetsPath)) ?? new List<InputPreset>();
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
                File.WriteAllText(PresetsPath, JsonSerializer.Serialize(Presets, new JsonSerializerOptions { WriteIndented = true }));
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
                File.WriteAllText(LastUsedPath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
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
                    var match = Presets?.FirstOrDefault(p => string.Equals(p.Name, state.PresetName, StringComparison.OrdinalIgnoreCase));
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
            var suggestion = !string.IsNullOrWhiteSpace(SelectedPreset?.Name) ? UniquePresetName($"{SelectedPreset.Name} (Copy)") : GenerateUniquePresetName();

            // Show the prompt
            var prompt = new MMCore.Views.NamePrompt
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

        // %AppData%\MMCore_v2\Templates
        private static string TemplatesFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MMCore_v2", "Templates");

        private static string PathForName(string name) => Path.Combine(TemplatesFolder, $"{ToSafeFileName(name)}.json");

        private static string ToSafeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string((name ?? "Template").Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
            var normalized = string.Join(" ", cleaned.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            return string.IsNullOrWhiteSpace(normalized) ? "Template" : normalized;
        }

        /// <summary>Save or rename a template. Pass originalName when Name changed.</summary>
        private async Task SaveTemplateAsync(Template t, string? originalName = null)
        {
            if (t is null) return;
            Directory.CreateDirectory(TemplatesFolder);

            if (!string.IsNullOrWhiteSpace(originalName) && !originalName.Equals(t.Name, StringComparison.OrdinalIgnoreCase))
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

            Templates = new ObservableCollection<Template>(items.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase));
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
            SelectedTemplate = Templates.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
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

        public class CommandOutputWithState
        {
            public CommandOutput Output { get; set; }
            public string CurrentCommand { get; set; } = "";
        }

        // Put inside MainViewModel
        private static bool TrySplitFriendly(string line, out string description, out string command)
        {
            description = null;
            command = line;

            if (string.IsNullOrWhiteSpace(line)) return false;

            bool inQuotes = false;
            for (int i = 0; i < line.Length - 2; i++)
            {
                char c = line[i];
                if (c == '"') inQuotes = !inQuotes;

                // Look for " = " (space, equals, space) when not inside quotes
                if (!inQuotes && line[i] == ' ' && line[i + 1] == '=' && line[i + 2] == ' ')
                {
                    description = line.Substring(0, i).Trim();
                    command = line.Substring(i + 3).Trim();
                    return !string.IsNullOrEmpty(description) && !string.IsNullOrEmpty(command);
                }
            }
            return false;
        }
    }
}