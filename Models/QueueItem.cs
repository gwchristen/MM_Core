namespace MMCore.Models
{
    public class QueueItem
    {
        public CommandTemplate Template { get; set; } = new();
        public string ExpandedCommand { get; set; } = "";
        public string Status { get; set; } = "Pending";
    }
}