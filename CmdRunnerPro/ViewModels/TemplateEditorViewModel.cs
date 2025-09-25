// CmdRunnerPro/ViewModels/TemplateEditorViewModel.cs
using System.ComponentModel;
using System.Runtime.CompilerServices; // <-- add this

namespace CmdRunnerPro.ViewModels
{
    public sealed class TemplateEditorViewModel : INotifyPropertyChanged
    {
        private string _password = string.Empty;
        public string Password
        {
            get => _password;
            set { if (_password != value) { _password = value; OnPropertyChanged(); } }
        }

        // Initialize non-nullables to avoid CS8618 warnings:
        private string _selectedCom1 = string.Empty;
        public string SelectedCom1
        {
            get => _selectedCom1;
            set { if (_selectedCom1 != value) { _selectedCom1 = value; OnPropertyChanged(); } }
        }

        private string _selectedCom2 = string.Empty;
        public string SelectedCom2
        {
            get => _selectedCom2;
            set { if (_selectedCom2 != value) { _selectedCom2 = value; OnPropertyChanged(); } }
        }

        private string _username = string.Empty;
        public string Username
        {
            get => _username;
            set { if (_username != value) { _username = value; OnPropertyChanged(); } }
        }

        private string _opco = string.Empty;
        public string Opco
        {
            get => _opco;
            set { if (_opco != value) { _opco = value; OnPropertyChanged(); } }
        }

        private string _program = string.Empty;
        public string Program
        {
            get => _program;
            set { if (_program != value) { _program = value; OnPropertyChanged(); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        // Allow parameterless OnPropertyChanged() calls:
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
