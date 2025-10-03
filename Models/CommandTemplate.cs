namespace CmdRunnerPro.Models
{
    public class CommandTemplate
    {
        public string Name { get; set; } = "";
        public string Template { get; set; } = ""; // supports tokens: {comport1},{comport2},{username},{password},{opco},{program},{wd} and {Q:token}
        public override string ToString() => Name;
    }
}