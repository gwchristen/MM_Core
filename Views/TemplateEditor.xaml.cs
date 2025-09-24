using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

using CmdRunnerPro.Models;
using CmdRunnerPro.Services;

namespace CmdRunnerPro.Views
{
    public partial class TemplateEditor : Window
    {
        private readonly ObservableCollection<CommandTemplate> _templates;

        public TemplateEditor(ObservableCollection<CommandTemplate> templates)
        {
            InitializeComponent();
            _templates = templates;
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
            if (List.SelectedItem is CommandTemplate t)
            {
                _templates.Remove(t);
                if (_templates.Count > 0) List.SelectedIndex = 0;
                LoadSelected();
            }
        }

        private void Up_Click(object sender, RoutedEventArgs e)
        {
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
            if (System.Windows.Application.Current.MainWindow is not CmdRunnerPro.Views.MainWindow main) return;
            var vm = main.VM;
            string masked = string.IsNullOrEmpty(vm.Password) ? "" : "******";
            var tokens = new System.Collections.Generic.Dictionary<string, string?>
            {
                ["comport1"] = vm.SelectedCom1,
                ["comport2"] = vm.SelectedCom2,
                ["username"] = vm.Username,
                ["password"] = masked,
                ["opco"]     = vm.Opco,
                ["program"]  = vm.Program,
                ["wd"]       = vm.WorkingDirectory
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
