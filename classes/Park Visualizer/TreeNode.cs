namespace CrmHub.Classes.ParkVisualizer
{
    public class TreeNode
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string? Description { get; set; }
        public List<TreeNode> Children { get; set; } = new();
    }
}