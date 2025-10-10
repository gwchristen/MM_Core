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
                    vm.OutputLines.CollectionChanged -= OutputLines_CollectionChanged;
                    vm.OutputLines.CollectionChanged += OutputLines_CollectionChanged;
                }
            }

            HookVm();
            this.DataContextChanged += (_, __) => HookVm();
        }

        private void OutputLines_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add)
            {
                // Find the active OutputList (Advanced tab) or OutputListSimple (Simple tab)
                var outputList = this.FindName("OutputList") as ListBox ?? this.FindName("OutputListSimple") as ListBox;
                if (outputList != null && outputList.Items.Count > 0)
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        outputList.ScrollIntoView(outputList.Items[^1]);
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