
using System.Collections.Generic;

namespace CmdRunnerPro.Models
{
    public class UserSettings
    {
        public string WorkingDirectory { get; set; } = "";
        public string SelectedCom1 { get; set; } = "";
        public string SelectedCom2 { get; set; } = "";

        // Inputs
        public string Username { get; set; } = "";
        public string PasswordEnc { get; set; } = "";  // Encrypted at rest
        public string Opco { get; set; } = "";
        public string Program { get; set; } = "";

        public string LastPresetName { get; set; } = "";
        public string LastSequenceName { get; set; } = "";
        public bool StopOnError { get; set; } = true;

        public List<InputPreset> Presets { get; set; } = new();
        public List<CommandTemplate> Templates { get; set; } = new();
        public List<CommandSequence> Sequences { get; set; } = new();
    }
}
