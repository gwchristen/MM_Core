using System.Windows.Controls;

namespace CmdRunnerPro.Views
{
    public partial class TemplateEditor : UserControl
    {
        public TemplateEditor()
        {
            InitializeComponent();
            // Assumes parent window's DataContext is MainViewModel; user control reuses it.
            DataContext = System.Windows.Application.Current.MainWindow?.DataContext;
        }
    }
}