using CmdRunnerPro.ViewModels;
using System.Collections.Specialized;
using System.Windows;
using System.Windows;
using System.Windows.Input;

namespace CmdRunnerPro.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();

            void HookVm()
            {
                if (DataContext is ViewModels.MainViewModel vm)
                {
                    vm.OutputLines.CollectionChanged -= OutputLines_CollectionChanged;
                    vm.OutputLines.CollectionChanged += OutputLines_CollectionChanged;
                }
            }

            HookVm();
            this.DataContextChanged += (_, __) => HookVm();
        }

        private void OutputLines_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action is NotifyCollectionChangedAction.Add && OutputList.Items.Count > 0)
                OutputList.ScrollIntoView(OutputList.Items[^1]);
        }

        private void ComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {

        }

        private void RevealPassword_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // show plaintext overlay while the mouse is held
            PasswordRevealTextBox.Visibility = Visibility.Visible;
            PasswordBox.Visibility = Visibility.Collapsed;
        }

        private void RevealPassword_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // re-mask when the mouse is released
            PasswordRevealTextBox.Visibility = Visibility.Collapsed;
            PasswordBox.Visibility = Visibility.Visible;
        }

        private void RevealPassword_MouseLeave(object sender, MouseEventArgs e)
        {
            // safety: also re-mask if the cursor leaves the button
            PasswordRevealTextBox.Visibility = Visibility.Collapsed;
            PasswordBox.Visibility = Visibility.Visible;
        }
    }
}