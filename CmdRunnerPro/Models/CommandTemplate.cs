
namespace CmdRunnerPro.Models
{
    public class CommandTemplate
    {
        public string Name { get; set; } = "";
        public string Template { get; set; } = "";
        public override string ToString() => Name;
    }
}
