public class Block
{
    public string Name { get; set; }
    public string Type { get; set; }
    public string Region { get; set; }
    public int Size { get; set; }
    public RectangleF Rectangle { get; set; }
    public List<Block> Children { get; set; } = new();
    public string Details { get; set; }
    public Color Color { get; set; }
}