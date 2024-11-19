namespace CrmHub.ParkVisualizer
{
    public class ParkInfo
    {
        public string Name { get; set; }
        public string Location { get; set; }
        public decimal Size { get; set; }
        public string Description { get; set; }

        public ParkInfo(string name, string location, decimal size, string description)
        {
            Name = name;
            Location = location;
            Size = size;
            Description = description;
        }
    }
}