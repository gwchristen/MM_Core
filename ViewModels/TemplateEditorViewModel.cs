// ViewModels/TemplateEditorViewModel.cs
using CmdRunnerPro.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Linq;

namespace CmdRunnerPro.ViewModels
{
    public class TemplateEditorViewModel : INotifyPropertyChanged, INotifyDataErrorInfo
    {
        private readonly object _originalTemplate;   // hold reference for ApplyToOriginal()
        private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);
        private readonly StringComparer _ci = StringComparer.OrdinalIgnoreCase;
        private readonly Func<string, bool> _nameExists;
        private bool _isQuickAddTokensExpanded;
        public bool IsQuickAddTokensExpanded
        {
            get => _isQuickAddTokensExpanded;
            set => Set(ref _isQuickAddTokensExpanded, value);
        }


        public TemplateEditorViewModel(object originalTemplate,
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
            ComPortOptions = comPortOptions is null
                ? Array.Empty<string>()
                : new List<string>(comPortOptions);

            AddSnippetCommand = new RelayCommand<Snippet>(s => AddSnippet(s), s => s is not null);
            AddTokenCommand = new RelayCommand<string>(t => AddToken(t), t => !string.IsNullOrWhiteSpace(t));

            // Preload default snippets/categories
            BuildDefaultSnippets();


            ValidateAll();
            UpdatePreview();
        }

        #region Working copy properties

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

        // Preview input fields (not saved to the Template model; for Preview only)
        private string _selectedCom1;
        public string SelectedCom1
        {
            get => _selectedCom1;
            set { if (Set(ref _selectedCom1, value, validate: false)) UpdatePreview(); }
        }

        private string _selectedCom2;
        public string SelectedCom2
        {
            get => _selectedCom2;
            set { if (Set(ref _selectedCom2, value, validate: false)) UpdatePreview(); }
        }

        private string _username;
        public string Username
        {
            get => _username;
            set { if (Set(ref _username, value, validate: false)) UpdatePreview(); }
        }

        private string _password;
        public string Password
        {
            get => _password;
            set { if (Set(ref _password, value, validate: false)) UpdatePreview(); }
        }

        private string _opco;
        public string Opco
        {
            get => _opco;
            set { if (Set(ref _opco, value, validate: false)) UpdatePreview(); }
        }

        private string _program;
        public string Program
        {
            get => _program;
            set { if (Set(ref _program, value, validate: false)) UpdatePreview(); }
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

        // Minimal token expander to support README tokens + {Q:token} variant.  [1](https://github.com/gwchristen/CmdRunnerPro)



        // Canonical tokens + aliases (kept consistent with GetTokenMap and your README)

        // Alias → canonical map (kept in sync with GetTokenMap())
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

                // aliases -> canonical (match your GetTokenMap())
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
        // 2) Keep your original GetTokenMap INSIDE the class
        private Dictionary<string, string> GetTokenMap()
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
                ["password"] = Password ?? "",
                ["opco"] = Opco ?? "",
                ["program"] = Program ?? "",
                // {wd} handled elsewhere at runtime; leave empty for preview
                ["wd"] = ""
            };
            return map;
        }

        // 3) Tokens-in-use updater (reuses your existing TokenRegex)
        private void UpdateTokensInUse()
        {
            var text = Template ?? string.Empty;

            var list = TokenRegex.Matches(text)
                .Cast<Match>()
                .Select(m => m.Groups[2].Value)  // group 2 = token name ({Q:...} supported)
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
            "PASSWORD" => Password,  // or mask: string.IsNullOrEmpty(Password) ? "" : "••••"
            "OPCO" => Opco,
            "PROGRAM" => Program,
            "WD" => "",        // you intentionally preview WD as empty
            _ => null
        };

        // 4) Leave UpdatePreview as-is except for the one line at the end
        private void UpdatePreview()
        {
            Preview = string.IsNullOrWhiteSpace(Template)
                ? string.Empty
                : ExpandTokens(Template, GetTokenMap());

            UpdateTokensInUse(); // keep the “tokens in use” list in sync
        }

        private static readonly Regex TokenRegex = new(@"\{(Q:)?([^}]+)\}", RegexOptions.Compiled);

        private string ExpandTokens(string template, Dictionary<string, string> tokenMap)
        {
            return TokenRegex.Replace(template, m =>
            {
                bool quote = m.Groups[1].Success;
                string key = m.Groups[2].Value;

                // tolerate case-insensitive; trim whitespace inside braces
                string value = tokenMap.TryGetValue(key.Trim(), out var v) ? v : m.Value;

                if (quote && !string.IsNullOrEmpty(value) && value.Contains(' '))
                    return $"\"{value}\"";

                return value;
            });
        }

        #endregion

        #region Apply back

        /// <summary>Apply working copy to a provided Template-like object.</summary>

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

        // Quick Add data
        public ObservableCollection<SnippetCategory> QuickAddCategories { get; } = new();

        // When true, token buttons paste {Q:token}; otherwise {token}
        private bool _useQuotedTokens;
        public bool UseQuotedTokens
        {
            get => _useQuotedTokens;
            set => Set(ref _useQuotedTokens, value);
        }

        // Commands
        public ICommand AddSnippetCommand { get; }
        public ICommand AddTokenCommand { get; }

        private void AddSnippet(Snippet s)
        {
            if (s is null || string.IsNullOrWhiteSpace(s.Text)) return;

            // Append with a newline if needed
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
            // Build {token} or {Q:token}
            var token = UseQuotedTokens ? $"{{Q:{tokenKey}}}" : $"{{{tokenKey}}}";

            // Append with a space if we’re mid-line; newline if empty
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
            // === MeterMate Quick Add ===
            var meterMate = new SnippetCategory { Name = "MeterMate (Quick Add)", IsExpanded = true };
        
            meterMate.Items.Add(new Snippet
            {
                Name = "Set ComPort 1",
                Description = "Set MeterMate COM port to {comport1}",
                Text = "START /WAIT MeterMate {username} {password} {opco} /ComPort {comport1}"
            });

            meterMate.Items.Add(new Snippet
            {
                Name = "Set ComPort 2",
                Description = "Set MeterMate COM port to {comport2}",
                Text = "START /WAIT MeterMate {username} {password} {opco} /ComPort {comport2}"
            });

            meterMate.Items.Add(new Snippet
            {
                Name = "Demand Reset",
                Description = "Perform a demand reset at 9600 opto baud",
                Text = "START /WAIT MeterMate {username} {password} {opco} /OPTOBAUDRATE 9600 /Demand"
            });

            meterMate.Items.Add(new Snippet
            {
                Name = "Master Reset",
                Description = "Perform a master reset at 9600 opto baud",
                Text = "START /WAIT MeterMate {username} {password} {opco} /OPTOBAUDRATE 9600 /Master"
            });

            meterMate.Items.Add(new Snippet
            {
                Name = "RDC Open",
                Description = "RDC command: Open (9600 opto baud)",
                Text = "START /WAIT MeterMate {username} {password} {opco} /OPTOBAUDRATE 9600 /COMMAND /STATE Open"
            });

            meterMate.Items.Add(new Snippet
            {
                Name = "RDC Close",
                Description = "RDC command: Close (9600 opto baud)",
                Text = "START /WAIT MeterMate {username} {password} {opco} /OPTOBAUDRATE 9600 /COMMAND /STATE Close"
            });

            meterMate.Items.Add(new Snippet
            {
                Name = "Program",
                Description = "Program using /PRO {program}; {Q:program} auto‑quotes when needed",
                Text = "START /WAIT MeterMate {username} {password} {opco} /OPTOBAUDRATE 9600 /Program /PRO {Q:program} /MID 000000000000000000 /TRD Yes"
            });

            // === Existing categories (keep or tweak as you like) ===
            var basics = new SnippetCategory { Name = "Basics", IsExpanded = false};
            basics.Items.Add(new Snippet
            {
                Name = "Echo COMs",
                Description = "Echo resolved COM port tokens",
                Text = "echo COM1={comport1} COM2={comport2}"
            });
            basics.Items.Add(new Snippet
            {
                Name = "CD to Working Dir",
                Description = "Change directory to the current Working Directory",
                Text = "cd {Q:wd}"
            });
            basics.Items.Add(new Snippet
            {
                Name = "Run Program (help)",
                Description = "Run the selected program with --help",
                Text = "{Q:program} --help"
            });

            var files = new SnippetCategory { Name = "File Ops", IsExpanded = false};
            files.Items.Add(new Snippet
            {
                Name = "Copy (quoted)",
                Description = "Copy example, quoting WD path",
                Text = "copy {Q:wd}\\source.txt {Q:wd}\\dest.txt"
            });
            files.Items.Add(new Snippet
            {
                Name = "Make Dir",
                Description = "Create folder under working directory",
                Text = "mkdir {Q:wd}\\output"
            });

            var serial = new SnippetCategory { Name = "Serial/COM", IsExpanded = false};
            serial.Items.Add(new Snippet
            {
                Name = "COM1 9600N81",
                Description = "Configure COM1 with common settings",
                Text = "mode {comport1}: baud=9600 parity=n data=8 stop=1"
            });
            serial.Items.Add(new Snippet
            {
                Name = "COM2 115200N81",
                Description = "Configure COM2 with high‑speed settings",
                Text = "mode {comport2}: baud=115200 parity=n data=8 stop=1"
            });

            var diagnostics = new SnippetCategory { Name = "Diagnostics", IsExpanded = false};
            diagnostics.Items.Add(new Snippet
            {
                Name = "Ping localhost",
                Description = "One ping to loopback",
                Text = "ping -n 1 127.0.0.1"
            });

            // Bind to UI
            QuickAddCategories.Clear();
            QuickAddCategories.Add(meterMate);     // put MeterMate at the top
            QuickAddCategories.Add(basics);
            QuickAddCategories.Add(files);
            QuickAddCategories.Add(serial);
            QuickAddCategories.Add(diagnostics);
        }
        public Visibility MeterMateVisibility => Visibility.Visible;
        public Visibility OtherQuickAddVisibility => Visibility.Collapsed;


        public class MeterMateExpanderConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                return value?.ToString() == "MeterMate";
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotImplementedException();
            }
        }

        // --- Tokens-in-use model + collection ---
        public sealed class TokenUsage
        {
            public string Name { get; set; }        // canonical (e.g., COM1, USERNAME, WD)
            public string SampleValue { get; set; } // current preview value (may be empty)
        }

        public ObservableCollection<TokenUsage> UsedTokens { get; } = new();

        //private string SampleFor(string canonical)
        //{
        //   switch (canonical)
        //    {
        //        case "COM1": return SelectedCom1;
        //        case "COM2": return SelectedCom2;
        //        case "USERNAME": return Username;
        //        case "PASSWORD": return Password;   // return "••••" if you want masking
        //        case "OPCO": return Opco;
        //        case "PROGRAM": return Program;
        //        case "WD": return "";         // you explicitly leave {wd} empty for preview
        //        default: return null;
        //    }
        //}

    }
}