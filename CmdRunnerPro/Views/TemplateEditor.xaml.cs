using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CmdRunnerPro.ViewModels;
using CmdRunnerPro.Models;
using CmdRunnerPro.Services;

namespace CmdRunnerPro.Views
{

    public partial class TemplateEditor : Window
    {
        private readonly MainViewModel? _owner;
        private readonly ObservableCollection<CommandTemplate>? _templates;
        public TemplateEditorViewModel VM { get; }   // <— add this


        public TemplateEditor(MainViewModel owner)
        {
            InitializeComponent();
            _owner = owner;              // set in this ctor
            VM = new TemplateEditorViewModel();
            DataContext = VM;
        }

        public TemplateEditor()
        {
            InitializeComponent();
            VM = new TemplateEditorViewModel();
            DataContext = VM;
        }


        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            // If you use a PasswordBox in XAML (cannot bind directly), capture it here:
            // VM.Password = PasswordBoxRef.Password;

            // Option 1: hand results back to owner via a method
            // _owner.ApplyTemplateEdits(VM);

            // Option 2: just close with DialogResult and let caller read editor.VM
            DialogResult = true;
            Close();
        }

        public TemplateEditor(ObservableCollection<CommandTemplate> templates)
        {
            InitializeComponent();

            // Ensure VM/DataContext are set in this ctor too:
            VM = new TemplateEditorViewModel();
            DataContext = VM;

            _templates = templates;      // set here
            List.ItemsSource = _templates;
            if (_templates.Count > 0) List.SelectedIndex = 0;
            LoadSelected();
        }

        private void LoadSelected()
        {
            if (List.SelectedItem is CommandTemplate t)
            {
                NameBox.Text = t.Name;
                TemplateBox.Text = t.Template;
            }
            else
            {
                NameBox.Text = string.Empty;
                TemplateBox.Text = string.Empty;
            }
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            if (_templates is null) return; // this instance doesn’t use the list
            var t = new CommandTemplate { Name = "New Template", Template = "echo hello {username}" };
            _templates.Add(t);
            List.SelectedItem = t;
            LoadSelected();
        }


        private void Edit_Click(object sender, RoutedEventArgs e)
        {
            LoadSelected();
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (_templates is null) return;
            if (List.SelectedItem is CommandTemplate t)
            {
                _templates.Remove(t);
                if (_templates.Count > 0) List.SelectedIndex = 0;
                LoadSelected();
            }
        }

        private void Up_Click(object sender, RoutedEventArgs e)
        {
            if (_templates is null) return;
            var idx = List.SelectedIndex;
            if (idx <= 0) return;
            var item = _templates[idx];
            _templates.RemoveAt(idx);
            _templates.Insert(idx - 1, item);
            List.SelectedIndex = idx - 1;
            LoadSelected();
        }

        private void Down_Click(object sender, RoutedEventArgs e)
        {
            if (_templates is null) return;
            var idx = List.SelectedIndex;
            if (idx < 0 || idx >= _templates.Count - 1) return;
            var item = _templates[idx];
            _templates.RemoveAt(idx);
            _templates.Insert(idx + 1, item);
            List.SelectedIndex = idx + 1;
            LoadSelected();
        }


        private void InsertToken_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string token)
            {
                var caret = TemplateBox.CaretIndex;
                TemplateBox.Text = TemplateBox.Text.Insert(caret, token);
                TemplateBox.CaretIndex = caret + token.Length;
                TemplateBox.Focus();
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (List.SelectedItem is not CommandTemplate t) return;
            var name = NameBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                System.Windows.MessageBox.Show("Name cannot be empty.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            t.Name = name;
            t.Template = TemplateBox.Text ?? string.Empty;
            List.Items.Refresh();
        }

        private void Preview_Click(object sender, RoutedEventArgs e)
        {
            // Owner is optional now — only used to fetch WorkingDirectory if available
            var owner = _owner ?? (System.Windows.Application.Current.MainWindow as CmdRunnerPro.Views.MainWindow)?.VM;

            // Secret stays in the editor VM
            string masked = string.IsNullOrEmpty(VM.Password) ? "" : "******";

            // Read preview-related fields from the TemplateEditorViewModel (VM), not MainViewModel
            var tokens = new System.Collections.Generic.Dictionary<string, string?>
            {
                ["comport1"] = VM.SelectedCom1,
                ["comport2"] = VM.SelectedCom2,
                ["username"] = VM.Username,
                ["password"] = masked, // from VM.Password
                ["opco"] = VM.Opco,
                ["program"] = VM.Program,
                ["wd"] = owner?.WorkingDirectory // keep from MainViewModel if you still store it there
            };

            var preview = TemplateEngine.Expand(TemplateBox.Text ?? string.Empty, tokens);
            PreviewText.Text = preview;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void List_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => LoadSelected();
    }
}
