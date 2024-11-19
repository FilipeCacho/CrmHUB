namespace CrmHub.ParkVisualizer
{
    public static class DemoData
    {
        public static Dictionary<string, List<ParkInfo>> GetDemoParks()
        {
            return new Dictionary<string, List<ParkInfo>>
            {
                ["United States"] = new List<ParkInfo>
                {
                    new ParkInfo("Yellowstone", "Wyoming", 8991, "First national park in the world"),
                    new ParkInfo("Yosemite", "California", 3083, "Known for its granite cliffs and waterfalls"),
                    new ParkInfo("Grand Canyon", "Arizona", 4926, "Carved by the Colorado River")
                },
                ["Canada"] = new List<ParkInfo>
                {
                    new ParkInfo("Banff", "Alberta", 6641, "Canada's first national park"),
                    new ParkInfo("Jasper", "Alberta", 11000, "Largest national park in Canadian Rockies")
                },
                ["Australia"] = new List<ParkInfo>
                {
                    new ParkInfo("Great Barrier Reef", "Queensland", 34400, "World's largest coral reef system"),
                    new ParkInfo("Uluru-Kata Tjuta", "Northern Territory", 1326, "Sacred to indigenous Australians")
                }
            };
        }
    }
}