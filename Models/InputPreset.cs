namespace CmdRunnerPro.Models
{
    public class InputPreset
    {
        public string Name { get; set; } = "";
        public string? Com1 { get; set; }
        public string? Com2 { get; set; }
        public string? Username { get; set; }
        public string? PasswordPlain { get; set; } // replace with encrypted at rest if you have DPAPI
        public string? Opco { get; set; }
        public string? Program { get; set; }
        public string? WorkingDirectory { get; set; }
        public string? TemplateName { get; set; }

        public override string ToString() => Name;
    }
}