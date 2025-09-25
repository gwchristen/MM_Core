
namespace CmdRunnerPro.Models
{
    public class PortInfo
    {
        public string Name { get; set; } = "";
        public bool InUse { get; set; }
        public override string ToString() => InUse ? $"{Name} (in use)" : Name;
    }
}
