// ViewModels/TemplateEditorViewModel.cs
using MaterialDesignThemes.Wpf;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.IO; // for Path.GetInvalidFileNameChars
using System.Threading.Tasks;
using MMCore.Utilities;

namespace MMCore.ViewModels
{
    /// <summary>
    /// The result object sent back to the opener via DialogHost.Close("RootDialog", result)
    /// so the caller can persist (Save), create (Save As), or delete the template.
    /// </summary>
    public sealed class TemplateEditorResult
    {
        public enum EditorAction { Saved, SavedAs, Deleted, Cancelled }
        public EditorAction Action { get; init; }

        // Payload for caller:
        public string Name { get; init; } = "";
        public string TemplateText { get; init; } = "";

        // Optional: original Id (if your template objects have one; we read it via reflection)
        public Guid? OriginalId { get; init; }
    }

    /// <summary>
    /// Working-copy Template Editor VM with preview/token support and Save/Save As/Delete commands.
    /// </summary>

    public class TemplateEditorViewModel : INotifyPropertyChanged, INotifyDataErrorInfo
    {
        private readonly object _originalTemplate;               // original object reference for ApplyToOriginal()
        private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);
        private readonly StringComparer _ci = StringComparer.OrdinalIgnoreCase;
        private readonly Func<string, bool> _nameExists;
        private bool _isQuickAddTokensExpanded;

        public event Action<TemplateEditorResult>? OperationRequested;

        public bool IsQuickAddTokensExpanded
        {
            get => _isQuickAddTokensExpanded;
            set => Set(ref _isQuickAddTokensExpanded, value);
        }

        public TemplateEditorViewModel(
            object originalTemplate,
            string name,
            string template,
            IEnumerable<string> comPortOptions = null,
            Func<string, bool> nameExists = null)
        {
            _nameExists = nameExists ?? (_ => false);
            _originalTemplate = originalTemplate ?? throw new ArgumentNullException(nameof(originalTemplate));

            // Working copy
            Name = name;
            Template = template;

            // Optional UI helpers
            ComPortOptions = comPortOptions is null ? Array.Empty<string>() : new List<string>(comPortOptions);

            // Commands
            AddSnippetCommand = new RelayCommand<Snippet>(s => AddSnippet(s), s => s is not null);
            AddTokenCommand = new RelayCommand<string>(t => AddToken(t), t => !string.IsNullOrWhiteSpace(t));
            SaveCommand = new RelayCommand<object>(_ => Save(), _ => CanSave);
            SaveAsCommand = new RelayCommand<object>(async _ => await SaveAsAsync(), _ => CanSave);
            DeleteCommand = new RelayCommand<object>(_ => Delete(), _ => true);
            BeginRenameCommand = new RelayCommand<object>(_ => BeginRename(), _ => true);
            ConfirmRenameCommand = new RelayCommand<object>(_ => ConfirmRename(), _ => CanConfirmRename);
            CancelRenameCommand = new RelayCommand<object>(_ => CancelRename(), _ => true);
            ClearSequenceCommand = new RelayCommand<object>(_ => ClearSequence(), _ => true);

            ConfirmSaveAsPromptCommand = new RelayCommand<object>(_ => ConfirmSaveAsPrompt(), _ => true);
            CancelSaveAsPromptCommand = new RelayCommand<object>(_ => CancelSaveAsPrompt(), _ => true);



            // Preload snippets/categories + initial validation/preview
            BuildDefaultSnippets();
            ValidateAll();
            UpdatePreview();
        }

        #region Working copy properties

        private const char MaskChar = '●';
        private static string Mask(string s) => string.IsNullOrEmpty(s) ? string.Empty : new string(MaskChar, s.Length);

        private string _name;
        public string Name
        {
            get => _name;
            set { if (Set(ref _name, value, validate: true)) UpdatePreview(); }
        }

        private string _template;
        public string Template
        {
            get => _template;
            set { if (Set(ref _template, value, validate: true)) UpdatePreview(); }
        }

        // Preview input fields (not persisted; used for Preview only)
        private string _selectedCom1;
        public string SelectedCom1
        {
            get => _selectedCom1;
            set { if (Set(ref _selectedCom1, value)) UpdatePreview(); }
        }

        private string _selectedCom2;
        public string SelectedCom2
        {
            get => _selectedCom2;
            set { if (Set(ref _selectedCom2, value)) UpdatePreview(); }
        }

        private string _username;
        public string Username
        {
            get => _username;
            set { if (Set(ref _username, value)) UpdatePreview(); }
        }

        private string _password;
        public string Password
        {
            get => _password;
            set { if (Set(ref _password, value)) UpdatePreview(); }
        }

        private string _opco;
        public string Opco
        {
            get => _opco;
            set { if (Set(ref _opco, value)) UpdatePreview(); }
        }

        private string _program;
        public string Program
        {
            get => _program;
            set { if (Set(ref _program, value)) UpdatePreview(); }
        }

        public IEnumerable<string> ComPortOptions { get; }

        private string _preview;
        public string Preview
        {
            get => _preview;
            private set => Set(ref _preview, value);
        }
        #endregion

        #region Validation (INotifyDataErrorInfo)
        public bool HasErrors => _errors.Count > 0;
        public event EventHandler<DataErrorsChangedEventArgs> ErrorsChanged;
        public IEnumerable GetErrors(string propertyName)
            => string.IsNullOrEmpty(propertyName) ? null
                                                  : _errors.TryGetValue(propertyName, out var list) ? list : null;

        private void ValidateAll()
        {
            ValidateProperty(nameof(Name), Name);
            ValidateProperty(nameof(Template), Template);
            OnPropertyChanged(nameof(CanSave));
        }

        private void ValidateProperty(string propertyName, object value)
        {
            var errs = new List<string>();
            switch (propertyName)
            {
                case nameof(Name):
                    if (string.IsNullOrWhiteSpace(Name)) errs.Add("Name is required.");
                    break;
                case nameof(Template):
                    if (string.IsNullOrWhiteSpace(Template)) errs.Add("Template is required.");
                    break;
            }

            bool had = _errors.ContainsKey(propertyName);
            if (errs.Count > 0) _errors[propertyName] = errs;
            else if (had) _errors.Remove(propertyName);

            if (had || errs.Count > 0)
                ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
        }

        public bool CanSave => !HasErrors;
        #endregion

        #region Preview expansion
        // Canonical tokens + aliases (keep consistent with GetTokenMap and README)
        private static readonly Dictionary<string, string> Canon =
            new(StringComparer.OrdinalIgnoreCase)
            {
                // canonical
                ["COM1"] = "COM1",
                ["COM2"] = "COM2",
                ["USERNAME"] = "USERNAME",
                ["PASSWORD"] = "PASSWORD",
                ["OPCO"] = "OPCO",
                ["PROGRAM"] = "PROGRAM",
                ["WD"] = "WD",
                // aliases -> canonical
                ["comport1"] = "COM1",
                ["comport2"] = "COM2",
                ["FIELD3"] = "COM1",
                ["FIELD4"] = "COM2",
                ["username"] = "USERNAME",
                ["password"] = "PASSWORD",
                ["opco"] = "OPCO",
                ["program"] = "PROGRAM",
                ["wd"] = "WD",
            };

        private Dictionary<string, string> GetTokenMap(bool maskPassword)
        {
            var map = new Dictionary<string, string>(_ci)
            {
                ["comport1"] = SelectedCom1 ?? "",
                ["comport2"] = SelectedCom2 ?? "",
                ["COM1"] = SelectedCom1 ?? "",
                ["COM2"] = SelectedCom2 ?? "",
                ["FIELD3"] = SelectedCom1 ?? "",
                ["FIELD4"] = SelectedCom2 ?? "",
                ["username"] = Username ?? "",
                ["password"] = maskPassword ? Mask(Password ?? "") : (Password ?? ""),
                ["opco"] = Opco ?? "",
                ["program"] = Program ?? "",
                ["wd"] = "" // preview leaves {wd} empty by design
            };
            return map;
        }


        private void UpdateTokensInUse()
        {
            var text = Template ?? string.Empty;

            var list = TokenRegex.Matches(text)
                .Cast<System.Text.RegularExpressions.Match>()
                .Select(m => m.Groups[2].Value) // group 2 = token name ({Q:...} supported)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(raw => Canon.TryGetValue(raw.Trim(), out var c) ? c : raw.ToUpperInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .Select(name => new TokenUsage { Name = name, SampleValue = SampleFor(name) })
                .ToList();

            for (int i = UsedTokens.Count - 1; i >= 0; i--)
                if (!list.Any(t => t.Name.Equals(UsedTokens[i].Name, StringComparison.OrdinalIgnoreCase)))
                    UsedTokens.RemoveAt(i);

            foreach (var t in list)
            {
                var existing = UsedTokens.FirstOrDefault(u => u.Name.Equals(t.Name, StringComparison.OrdinalIgnoreCase));
                if (existing == null) UsedTokens.Add(t);
                else existing.SampleValue = t.SampleValue;
            }

            OnPropertyChanged(nameof(UsedTokens));
        }

        private string SampleFor(string canonical) => canonical switch
        {
            "COM1" => SelectedCom1,
            "COM2" => SelectedCom2,
            "USERNAME" => Username,
            "PASSWORD" => Mask(Password ?? ""), // show masked in the token usage panel
            "OPCO" => Opco,
            "PROGRAM" => Program,
            "WD" => "",
            _ => null
        };

        private void UpdatePreview()
        {
            Preview = string.IsNullOrWhiteSpace(Template)
                ? string.Empty
                : ExpandTokens(Template, GetTokenMap(maskPassword: true));
            UpdateTokensInUse();
        }

        // Matches {token} and {Q:token}
        private static readonly Regex TokenRegex = new(@"\{(Q:)?([^\}]+)\}", RegexOptions.Compiled);

        private string ExpandTokens(string template, Dictionary<string, string> tokenMap)
        {
            return TokenRegex.Replace(template, m =>
            {
                bool quote = m.Groups[1].Success;
                string key = m.Groups[2].Value;
                string value = tokenMap.TryGetValue(key.Trim(), out var v) ? v : m.Value;
                if (quote && !string.IsNullOrEmpty(value) && value.Contains(' '))
                    return $"\"{value}\"";
                return value;
            });
        }
        #endregion

        #region Apply back to original
        /// <summary>Apply working copy to a provided Template-like object (writes Name & TemplateText/Template).</summary>
        public void ApplyTo(object target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            var t = target.GetType();
            t.GetProperty("Name")?.SetValue(target, Name?.Trim());
            var pTemplate = t.GetProperty("TemplateText") ?? t.GetProperty("Template");
            pTemplate?.SetValue(target, Template ?? "");
        }

        public void ApplyToOriginal() => ApplyTo(_originalTemplate);
        #endregion

        #region INotifyPropertyChanged helpers
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        protected bool Set<T>(ref T storage, T value, bool validate = false, [CallerMemberName] string name = null)
        {
            if (EqualityComparer<T>.Default.Equals(storage, value)) return false;
            storage = value;
            if (validate)
            {
                ValidateProperty(name, value);
                OnPropertyChanged(nameof(CanSave));
            }
            OnPropertyChanged(name);
            return true;
        }
        #endregion

        #region Quick Add model + commands
        public class Snippet
        {
            public string Name { get; init; }
            public string Text { get; init; }
            public string Description { get; init; }
        }

        public class SnippetCategory
        {
            public string Name { get; init; }
            public ObservableCollection<Snippet> Items { get; init; } = new();
            public bool IsExpanded { get; set; }
        }

        public ObservableCollection<SnippetCategory> QuickAddCategories { get; } = new();

        private bool _useQuotedTokens;
        public bool UseQuotedTokens
        {
            get => _useQuotedTokens;
            set => Set(ref _useQuotedTokens, value);
        }

        public ICommand AddSnippetCommand { get; }
        public ICommand AddTokenCommand { get; }


        private void AddSnippet(Snippet s)
        {
            if (s is null || string.IsNullOrWhiteSpace(s.Text)) return;

            if (string.IsNullOrWhiteSpace(Template))
                Template = s.Text;
            else
                Template = Template.TrimEnd() + Environment.NewLine + s.Text;

            UpdatePreview();
            ValidateProperty(nameof(Template), Template);
            OnPropertyChanged(nameof(CanSave));
        }

        private void AddToken(string tokenKey)
        {
            var token = UseQuotedTokens ? $"{{Q:{tokenKey}}}" : $"{{{tokenKey}}}";

            if (string.IsNullOrWhiteSpace(Template))
                Template = token;
            else
            {
                char last = Template[^1];
                string sep = (last == '\n' || last == '\r') ? "" : " ";
                Template += sep + token;
            }

            UpdatePreview();
            ValidateProperty(nameof(Template), Template);
            OnPropertyChanged(nameof(CanSave));
        }

        private void BuildDefaultSnippets()
        {
            var meterMate = new SnippetCategory { Name = "MeterMate (Quick Add)", IsExpanded = true };
            meterMate.Items.Add(new Snippet { Name = "Set ComPort 1", Description = "Set MeterMate COM port to {comport1}", Text = "START /WAIT MeterMate {username} {password} {opco} /ComPort {comport1}" });
            meterMate.Items.Add(new Snippet { Name = "Set ComPort 2", Description = "Set MeterMate COM port to {comport2}", Text = "START /WAIT MeterMate {username} {password} {opco} /ComPort {comport2}" });
            meterMate.Items.Add(new Snippet { Name = "Demand Reset", Description = "Perform a demand reset at 9600 opto baud", Text = "START /WAIT MeterMate {username} {password} {opco} /OPTOBAUDRATE 9600 /Demand" });
            meterMate.Items.Add(new Snippet { Name = "Master Reset", Description = "Perform a master reset at 9600 opto baud", Text = "START /WAIT MeterMate {username} {password} {opco} /OPTOBAUDRATE 9600 /Master" });
            meterMate.Items.Add(new Snippet { Name = "RDC Open", Description = "RDC command: Open (9600 opto baud)", Text = "START /WAIT MeterMate {username} {password} {opco} /OPTOBAUDRATE 9600 /COMMAND /STATE Open" });
            meterMate.Items.Add(new Snippet { Name = "RDC Close", Description = "RDC command: Close (9600 opto baud)", Text = "START /WAIT MeterMate {username} {password} {opco} /OPTOBAUDRATE 9600 /COMMAND /STATE Close" });
            meterMate.Items.Add(new Snippet { Name = "Program", Description = "Program using /PRO {program}; {Q:program} auto‑quotes when needed", Text = "START /WAIT MeterMate {username} {password} {opco} /OPTOBAUDRATE 9600 /Program /PRO {Q:program} /MID 000000000000000000 /TRD Yes" });

            var basics = new SnippetCategory { Name = "Basics", IsExpanded = false };
            basics.Items.Add(new Snippet { Name = "Echo COMs", Description = "Echo resolved COM port tokens", Text = "echo COM1={comport1} COM2={comport2}" });
            //basics.Items.Add(new Snippet { Name = "CD to Working Dir", Description = "Change directory to the current Working Directory", Text = "cd {Q:wd}" });
            //basics.Items.Add(new Snippet { Name = "Run Program (help)", Description = "Run the selected program with --help", Text = "{Q:program} --help" });

            //var files = new SnippetCategory { Name = "File Ops", IsExpanded = false };
            //files.Items.Add(new Snippet { Name = "Copy (quoted)", Description = "Copy example, quoting WD path", Text = "copy {Q:wd}\\source.txt {Q:wd}\\dest.txt" });
            //files.Items.Add(new Snippet { Name = "Make Dir", Description = "Create folder under working directory", Text = "mkdir {Q:wd}\\output" });

           // var serial = new SnippetCategory { Name = "Serial/COM", IsExpanded = false };
            //serial.Items.Add(new Snippet { Name = "COM1 9600N81", Description = "Configure COM1 with common settings", Text = "mode {comport1}: baud=9600 parity=n data=8 stop=1" });
            //serial.Items.Add(new Snippet { Name = "COM2 115200N81", Description = "Configure COM2 with high‑speed settings", Text = "mode {comport2}: baud=115200 parity=n data=8 stop=1" });

           // var diagnostics = new SnippetCategory { Name = "Diagnostics", IsExpanded = false };
            //diagnostics.Items.Add(new Snippet { Name = "Ping localhost", Description = "One ping to loopback", Text = "ping -n 1 127.0.0.1" });

            QuickAddCategories.Clear();
            QuickAddCategories.Add(meterMate);
            QuickAddCategories.Add(basics);
            //QuickAddCategories.Add(files);
            //QuickAddCategories.Add(serial);
            //QuickAddCategories.Add(diagnostics);
        }

        public Visibility MeterMateVisibility => Visibility.Visible;
        public Visibility OtherQuickAddVisibility => Visibility.Collapsed;

        public class MeterMateExpanderConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
                => value?.ToString() == "MeterMate";

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
                => throw new NotImplementedException();
        }

        public sealed class TokenUsage
        {
            public string Name { get; set; }         // canonical (e.g., COM1, USERNAME, WD)
            public string SampleValue { get; set; }  // current preview value (may be empty)
        }

        public ObservableCollection<TokenUsage> UsedTokens { get; } = new();
        #endregion

        #region Save / Save As… / Delete commands
        public ICommand SaveCommand { get; }
        public ICommand SaveAsCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand BeginRenameCommand { get; }
        public ICommand ConfirmRenameCommand { get; }
        public ICommand CancelRenameCommand { get; }
        public ICommand ClearSequenceCommand { get; }
        public ICommand ConfirmSaveAsPromptCommand { get; }
        public ICommand CancelSaveAsPromptCommand { get; }



        private static readonly StringComparer _nameComparer = StringComparer.OrdinalIgnoreCase;

        private string? GetOriginalName()
        {
            var t = _originalTemplate.GetType();
            return t.GetProperty("Name")?.GetValue(_originalTemplate) as string;
        }

        private Guid? GetOriginalId()
        {
            var t = _originalTemplate.GetType();
            var p = t.GetProperty("Id");
            if (p is not null && p.PropertyType == typeof(Guid))
                return (Guid)p.GetValue(_originalTemplate);
            return null;
        }

        private void CloseDialog(object payload) =>
            DialogHost.Close("RootDialog", payload);

        // Save: overwrite same object and close
        private void Save()
        {
            if (!CanSave) return;

            // Keep the editor in sync with what the user typed.
            ApplyToOriginal();

            // Notify host to persist; DO NOT close the editor
            OperationRequested?.Invoke(new TemplateEditorResult
            {
                Action = TemplateEditorResult.EditorAction.Saved,
                Name = Name?.Trim() ?? "",
                TemplateText = Template ?? "",
                OriginalId = GetOriginalId()
            });
        }

        // Save As: request caller to create a new template (we generate a unique name if needed)

        private async Task<string?> PromptForTemplateNameAsync(string suggestion)
        {
            var prompt = new MMCore.Views.NamePrompt
            {
                Title = "Save Template As",
                Prompt = "Template name",
                Text = suggestion
            };
            var result = await DialogHost.Show(prompt, "RootDialog");
            return result as string; // null on cancel
        }

        private async Task SaveAsAsync()
        {
            if (!CanSave) return;

            var originalName = GetOriginalName() ?? string.Empty;
            var entered = SanitizeFileishName(Name);

            string suggestion = _nameComparer.Equals(entered, originalName)
                ? MakeUniqueName($"{originalName} (Copy)")
                : (_nameExists(entered) ? MakeUniqueName(entered) : entered);

            // Inline child prompt (the one you added) – await the result
            var raw = await OpenSaveAsPromptAsync(suggestion);
            if (raw is null) return; // cancelled

            var chosen = SanitizeFileishName(raw);
            if (_nameExists(chosen))
                chosen = MakeUniqueName(chosen);

            // Tell host to create the new template; KEEP editor open
            OperationRequested?.Invoke(new TemplateEditorResult
            {
                Action = TemplateEditorResult.EditorAction.SavedAs,
                Name = chosen,
                TemplateText = Template ?? string.Empty
            });
        }

        // Delete: ask caller to delete the original item
        private void Delete()
        {
            OperationRequested?.Invoke(new TemplateEditorResult
            {
                Action = TemplateEditorResult.EditorAction.Deleted,
                Name = GetOriginalName() ?? Name ?? "",
                TemplateText = Template ?? "",
                OriginalId = GetOriginalId()
            });
        }

        private static readonly char[] _invalidNameChars = Path.GetInvalidFileNameChars();

        private static string SanitizeFileishName(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "New Template";
            var trimmed = input.Trim();
            return new string(trimmed.Select(c => _invalidNameChars.Contains(c) ? '_' : c).ToArray());
        }

        private string MakeUniqueName(string baseName)
        {
            if (!_nameExists(baseName)) return baseName;
            int n = 1;
            string candidate;
            do
            {
                candidate = n == 1 ? $"{baseName} (Copy)" : $"{baseName} (Copy {++n})";
            } while (_nameExists(candidate));
            return candidate;
        }

        private bool ValidateNewName(string? name, bool allowSameAsCurrent, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                error = "Name cannot be empty.";
                return false;
            }
            var trimmed = name.Trim();
            if (trimmed.IndexOfAny(_invalidNameChars) >= 0)
            {
                error = "Name contains invalid characters.";
                return false;
            }
            if (!allowSameAsCurrent && string.Equals(trimmed, GetOriginalName() ?? "", StringComparison.OrdinalIgnoreCase))
            {
                error = "That’s the current name.";
                return false;
            }
            // allow picking the same as current for rename? flip the flag if you want that allowed
            return true;
        }
        private string _proposedName = "";
        public string ProposedName
        {
            get => _proposedName;
            set
            {
                if (Set(ref _proposedName, value))
                    OnPropertyChanged(nameof(CanConfirmRename));
            }
        }

        public bool CanConfirmRename
            => ValidateNewName(ProposedName, allowSameAsCurrent: false, out _);
        #endregion
        // If you drive a small inline dialog from XAML, you can toggle a bool here.
        // Keeping VM-agnostic, we just preload ProposedName and let the view show a dialog.
        private void BeginRename()
        {
            ProposedName = Name ?? "";
            IsRenameDialogOpen = true;
        }


        private void ConfirmRename()
        {
            if (!ValidateNewName(ProposedName, allowSameAsCurrent: false, out _)) return;
            Name = SanitizeFileishName(ProposedName);
            IsRenameDialogOpen = false;
        }


        private void CancelRename()
        {
            IsRenameDialogOpen = false;
            // No-op; the view can just close the dialog.
        }

        private bool _isRenameDialogOpen;
        public bool IsRenameDialogOpen
        {
            get => _isRenameDialogOpen;
            set => Set(ref _isRenameDialogOpen, value);
        }
        // --- LOAD FROM SELECTED TEMPLATE ---
        public void LoadFrom(object source)
        {
            if (source is null) return;

            var t = source.GetType();

            // Read Name
            var name = t.GetProperty("Name")?.GetValue(source) as string ?? string.Empty;

            // Read Template text (support both "TemplateText" and "Template" property names)
            var text = t.GetProperty("TemplateText")?.GetValue(source) as string
                       ?? t.GetProperty("Template")?.GetValue(source) as string
                       ?? string.Empty;

            // Assign to working copy (these setters already run validation and preview)
            Name = name;
            Template = text;
        }

        // --- CLEAR SEQUENCE (ensures the TextBox updates) ---
        private void ClearSequence()
        {
            if (!string.IsNullOrEmpty(Template))
                Template = string.Empty;   // setter raises PropertyChanged & revalidates
            UpdatePreview();
            ValidateProperty(nameof(Template), Template);
            OnPropertyChanged(nameof(CanSave));
        }

        // --- Save As prompt state ---
        private TaskCompletionSource<string?> _saveAsTcs;

        private bool _isSaveAsPromptOpen;
        public bool IsSaveAsPromptOpen
        {
            get => _isSaveAsPromptOpen;
            set => Set(ref _isSaveAsPromptOpen, value);
        }

        private string _saveAsProposedName = "";
        public string SaveAsProposedName
        {
            get => _saveAsProposedName;
            set => Set(ref _saveAsProposedName, value);
        }
        private Task<string?> OpenSaveAsPromptAsync(string suggestion)
        {
            _saveAsTcs = new TaskCompletionSource<string?>();
            SaveAsProposedName = suggestion ?? "";
            IsSaveAsPromptOpen = true;
            return _saveAsTcs.Task;
        }

        private void ConfirmSaveAsPrompt()
        {
            var input = SaveAsProposedName?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(input))
            {
                // Optional: add validation feedback (Snackbar, error text) and keep dialog open
                return;
            }
            IsSaveAsPromptOpen = false;
            _saveAsTcs?.TrySetResult(input);
        }

        private void CancelSaveAsPrompt()
        {
            IsSaveAsPromptOpen = false;
            _saveAsTcs?.TrySetResult(null);
        }

    }
}
