using System.Windows.Controls;
using System.Windows;
using CmdRunnerPro.ViewModels;
using System.Windows.Input;

namespace CmdRunnerPro.Views
{
    public partial class TemplateEditor : UserControl
    {
        public TemplateEditor()
        {
            InitializeComponent();
            // Hook after load so the element names exist.
            this.Loaded += (_, __) =>
            {
                var combo = this.FindName("TemplatesCombo") as ComboBox;
                if (combo != null)
                {
                    combo.SelectionChanged -= TemplatesCombo_SelectionChanged;
                    combo.SelectionChanged += TemplatesCombo_SelectionChanged;
                }
            };
        }

        private void TemplatesCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is TemplateEditorViewModel vm)
            {
                var selected = (sender as ComboBox)?.SelectedItem;
                if (selected != null)
                {
                    vm.LoadFrom(selected);
                }
            }
        }
    }
}