namespace CmdRunnerPro.Models
{
    public class UserSettings
    {
        public bool IsDarkTheme { get; set; } = false;
        public string PrimaryColor { get; set; } = "DeepPurple";
        public string SecondaryColor { get; set; } = "Lime";
        public bool ShowTimestamps { get; set; } = true;

        // Extend with other persisted fields as needed (paths, last selections, etc.)
    }
}
