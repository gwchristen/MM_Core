using System.Windows;
using System.Windows.Controls;

namespace CmdRunnerPro.Views
{
    public partial class NamePrompt : UserControl
    {
        public NamePrompt() => InitializeComponent();

        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(nameof(Title), typeof(string), typeof(NamePrompt), new PropertyMetadata("Save As"));

        public static readonly DependencyProperty PromptProperty =
            DependencyProperty.Register(nameof(Prompt), typeof(string), typeof(NamePrompt), new PropertyMetadata("New name"));

        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register(nameof(Text), typeof(string), typeof(NamePrompt), new PropertyMetadata(""));

        public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
        public string Prompt { get => (string)GetValue(PromptProperty); set => SetValue(PromptProperty, value); }
        public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    }
}