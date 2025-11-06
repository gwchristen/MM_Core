using MMCore.ViewModels;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;  // Add this for ListBox
using System.Windows.Input;
using System.Windows.Threading;

namespace MMCore.Views
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
                    vm.PropertyChanged -= Vm_PropertyChanged;
                    vm.PropertyChanged += Vm_PropertyChanged;
                }
            }

            HookVm();
            this.DataContextChanged += (_, __) => HookVm();
        }

        private void Vm_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.OutputText))
            {
                // Auto-scroll the active output TextBox when output is appended
                var outputTextBox = this.FindName("OutputTextBox") as TextBox ?? this.FindName("OutputTextBoxSimple") as TextBox;
                if (outputTextBox != null)
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        outputTextBox.ScrollToEnd();
                    }), DispatcherPriority.Background);
                }
            }
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

        private void RevealPasswordSimple_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            PasswordRevealTextBoxSimple.Visibility = Visibility.Visible;
            PasswordBoxSimple.Visibility = Visibility.Collapsed;
        }

        private void RevealPasswordSimple_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            PasswordRevealTextBoxSimple.Visibility = Visibility.Collapsed;
            PasswordBoxSimple.Visibility = Visibility.Visible;
        }

        private void RevealPasswordSimple_MouseLeave(object sender, MouseEventArgs e)
        {
            PasswordRevealTextBoxSimple.Visibility = Visibility.Collapsed;
            PasswordBoxSimple.Visibility = Visibility.Visible;
        }
    }
}