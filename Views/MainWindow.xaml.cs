using System.Windows;
using CmdRunnerPro.ViewModels;
using System.Collections.Specialized;
using System.Windows;

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

    }
}