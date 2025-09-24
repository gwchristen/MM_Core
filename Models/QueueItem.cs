
namespace CmdRunnerPro.Models
{
    public class QueueItem
    {
        public string Command { get; set; } = ""; // real command (contains secrets)
        public string Display { get; set; } = ""; // redacted for UI/log
        public string? TemplateName { get; set; } // to support saving sequences by template
        public override string ToString() => Display;
    }
}
