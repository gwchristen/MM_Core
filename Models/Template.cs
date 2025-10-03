// Models/Template.cs
using System.ComponentModel;
using System.Runtime.CompilerServices;

// If you use System.Text.Json (default in .NET 8):
using System.Text.Json.Serialization;

// If you use Newtonsoft.Json instead, comment the line above and
// add: using Newtonsoft.Json;

namespace CmdRunnerPro.Models
{
    public class Template : INotifyPropertyChanged
    {
        // Backing fields (initialize to avoid nullable warnings)
        private string _name = string.Empty;
        private string _templateText = string.Empty;

        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged();
                }
            }
        }

        // Keep JSON name as "Template" while avoiding CS0542 by naming the C# property TemplateText
        [JsonPropertyName("Template")]               // System.Text.Json
        // [JsonProperty("Template")]                // Newtonsoft.Json alternative
        public string TemplateText
        {
            get => _templateText;
            set
            {
                if (_templateText != value)
                {
                    _templateText = value;
                    OnPropertyChanged();
                }
            }
        }

        public override string ToString() => Name ?? base.ToString();

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}