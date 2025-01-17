using Microsoft.AspNetCore.Mvc;

namespace CrmHub.Classes.ParkVisualizer
{
    [ApiController]
[Route("api/[controller]")]
public class TreeController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        var parkData = new List<TreeNode>
        {
            new TreeNode
            {
                Id = "1",
                Name = "Wind Park Alpha",
                Type = "folder",
                Description = "Main wind park facility",
                Children = new List<TreeNode>
                {
                    new TreeNode
                    {
                        Id = "2",
                        Name = "Turbine Section A",
                        Type = "folder",
                        Description = "Northern turbine cluster",
                        Children = new List<TreeNode>
                        {
                            new TreeNode
                            {
                                Id = "3",
                                Name = "Turbine A1",
                                Type = "turbine",
                                Description = "15MW Capacity - Operational"
                            },
                            new TreeNode
                            {
                                Id = "4",
                                Name = "Turbine A2",
                                Type = "turbine",
                                Description = "15MW Capacity - Maintenance"
                            }
                        }
                    },
                    new TreeNode
                    {
                        Id = "5",
                        Name = "Turbine Section B",
                        Type = "folder",
                        Description = "Southern turbine cluster",
                        Children = new List<TreeNode>
                        {
                            new TreeNode
                            {
                                Id = "6",
                                Name = "Turbine B1",
                                Type = "turbine",
                                Description = "12MW Capacity - Operational"
                            }
                        }
                    }
                }
            }
        };

        return Ok(parkData);
    }
}
}