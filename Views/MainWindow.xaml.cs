using System.Diagnostics;
using System.Windows;
// Alias WinForms namespace ONLY (do not import it unaliased)
using WinForms = System.Windows.Forms;
// Bring the VM into scope
using CmdRunnerPro.ViewModels;

namespace CmdRunnerPro.Views
{
    public partial class MainWindow : Window
    {
        public MainViewModel VM { get; } = new MainViewModel();

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new WinForms.FolderBrowserDialog();
            if (dlg.ShowDialog() == WinForms.DialogResult.OK)
            {
                VM.WorkingDirectory = dlg.SelectedPath;
            }
        }

        private void AutoDetectWD_Click(object sender, RoutedEventArgs e) => VM.AutoDetectWorkingDirectory();
        private void TestMeterMate_Click(object sender, RoutedEventArgs e) => VM.TestMeterMate();

        private void RefreshPorts_Click(object sender, RoutedEventArgs e) => VM.RefreshPorts();
        private void AddToQueue_Click(object sender, RoutedEventArgs e) => VM.AddSelectedTemplateToQueue();
        private void Run_Click(object sender, RoutedEventArgs e) => VM.RunQueueAsync();
        private void Stop_Click(object sender, RoutedEventArgs e) => VM.Stop();

        private void Up_Click(object sender, RoutedEventArgs e)
        {
            int idx = QueueList.SelectedIndex;
            VM.MoveQueueItem(idx, -1);
        }

        private void Down_Click(object sender, RoutedEventArgs e)
        {
            int idx = QueueList.SelectedIndex;
            VM.MoveQueueItem(idx, 1);
        }

        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            int idx = QueueList.SelectedIndex;
            VM.RemoveQueueItem(idx);
        }

        private void Clear_Click(object sender, RoutedEventArgs e) => VM.ClearQueue();

        private void SavePreset_Click(object sender, RoutedEventArgs e)
        {
            var name = PresetNameBox.Text?.Trim();
            VM.SavePreset(name ?? "");
        }

        private void LoadPreset_Click(object sender, RoutedEventArgs e) => VM.LoadPreset(VM.SelectedPreset);

        private void Templates_Click(object sender, RoutedEventArgs e) => VM.OpenTemplateEditor();
        private void SaveSettings_Click(object sender, RoutedEventArgs e) => VM.SaveAll();

        private void ShowLog_Click(object sender, RoutedEventArgs e)
        {
            try { Process.Start("explorer.exe", System.IO.Path.GetDirectoryName(VM.LogFile)!); } catch { }
        }

        // --- Export / Import ---

        private void ExportTemplates_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json",
                FileName = "templates.json"
            };
            if (dlg.ShowDialog() == true)
            {
                VM.ExportTemplatesTo(dlg.FileName);
            }
        }

        private void ImportTemplates_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "JSON files (*.json)|*.json"
            };
            if (dlg.ShowDialog() == true)
            {
                VM.ImportTemplatesFrom(dlg.FileName);
            }
        }

        private void ExportPresets_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json",
                FileName = "presets.portable.json"
            };
            if (dlg.ShowDialog() == true)
            {
                VM.ExportPresetsTo(dlg.FileName, includeEncryptedPasswords: false);
            }
        }

        private void ExportPresetsWithPw_Click(object sender, RoutedEventArgs e)
        {
            var msg =
                "Export presets WITH encrypted passwords?\n\n" +
                "Warning: The exported passwords are protected with Windows DPAPI and can only be decrypted by the same Windows user on the same machine profile.\n\n" +
                "If you need portability, export WITHOUT passwords instead.";

            var result = System.Windows.MessageBox.Show(
                msg,
                "Confirm",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json",
                FileName = "presets.with-passwords.json"
            };
            if (dlg.ShowDialog() == true)
            {
                VM.ExportPresetsTo(dlg.FileName, includeEncryptedPasswords: true);
            }
        }

        private void ImportPresets_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "JSON files (*.json)|*.json"
            };
            if (dlg.ShowDialog() == true)
            {
                VM.ImportPresetsFrom(dlg.FileName);
            }
        }

        private void ExportSequences_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json",
                FileName = "sequences.json"
            };
            if (dlg.ShowDialog() == true)
            {
                VM.ExportSequencesTo(dlg.FileName);
            }
        }

        private void ImportSequences_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "JSON files (*.json)|*.json"
            };
            if (dlg.ShowDialog() == true)
            {
                VM.ImportSequencesFrom(dlg.FileName);
            }
        }

        private void Exit_Click(object sender, RoutedEventArgs e) => Close();

        private void OpenWD_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(VM.WorkingDirectory) &&
                System.IO.Directory.Exists(VM.WorkingDirectory))
            {
                try { Process.Start("explorer.exe", VM.WorkingDirectory); } catch { }
            }
        }

        private void Templates_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => VM.AddSelectedTemplateToQueue();

        public MainWindow()
        {
            InitializeComponent();
            DataContext = VM;

            VM.OutputLines.CollectionChanged += (_, __) =>
            {
                Dispatcher.InvokeAsync(() => OutputScroll?.ScrollToEnd());
            };
        }

        protected override void OnClosed(System.EventArgs e)
        {
            VM.SaveAll();
            base.OnClosed(e);
        }
    }
}