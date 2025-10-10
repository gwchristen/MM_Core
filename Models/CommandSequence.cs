using System.Collections.Generic;

namespace MMCore.Models
{
    public class CommandSequence
    {
        public string Name { get; set; } = "";
        public List<string> TemplateNames { get; set; } = new();
    }
}