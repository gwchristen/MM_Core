using System.Windows;
using CmdRunnerPro.ViewModels;

namespace CmdRunnerPro.Views
{
    public partial class MainWindow : System.Windows.Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();
        }
    }
}