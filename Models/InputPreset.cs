
namespace CmdRunnerPro.Models
{
    public class InputPreset
    {
        public string Name { get; set; } = "";
        public string WorkingDirectory { get; set; } = "";
        public string Com1 { get; set; } = "";
        public string Com2 { get; set; } = "";

        public string Username { get; set; } = "";
        public string PasswordEnc { get; set; } = ""; // Encrypted at rest
        public string Opco { get; set; } = "";
        public string Program { get; set; } = "";

        public override string ToString() => Name;
    }
}
