using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Crm.Sdk.Messages;

public class AssignTeamData
{
    public string Username { get; set; }
    public string TeamName { get; set; }
}

public sealed class AssignTeams
{
    private ServiceClient _serviceClient;
    private UserRetriever _userRetriever;

    public async Task ProcessAssignTeamsAsync()
    {
        //list to store disabled users to make sure they are not repeated when processing teams
        List<string> disabledUser = new List<string>();

        try
        {
            await ConnectToDataverseAsync();
            List<AssignTeamData> assignTeamDataList = ExcelReader.ReadAssignTeamsData();

            if (!ValidateExcelData(assignTeamDataList))
            {
                Console.ForegroundColor = ConsoleColor.Blue;
                Console.WriteLine("Press any key to return to the menu.");
                Console.ReadKey();
                return;
            }

            foreach (var data in assignTeamDataList)
            {
                await ProcessUserAsync(data.Username, data.TeamName.Trim(), disabledUser);
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\nTeam assignment process completed.");
            Console.ResetColor();
            Console.WriteLine("\nPress any key to return to the menu.");
            Console.ReadKey();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"An error occurred: {ex.Message}");

            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("Press any key to return to the menu.");
            Console.ResetColor();
            Console.ReadKey();
        }
    }

    private bool ValidateExcelData(List<AssignTeamData> data)
    {
        bool isValid = true;
        for (int i = 0; i < data.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(data[i].Username) || string.IsNullOrWhiteSpace(data[i].TeamName))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Warning: Row {i + 2} has missing data. Username: '{data[i].Username}', Team Name: '{data[i].TeamName}'");
                Console.ResetColor();
                isValid = false;
            }
        }
        return isValid;
    }

    private async Task ConnectToDataverseAsync()
    {
        try
        {
            // Use the SessionManager to get the service client
            _serviceClient = SessionManager.Instance.GetClient();

            if (_serviceClient == null || !_serviceClient.IsReady)
            {
                throw new Exception($"Failed to connect. Error: {(_serviceClient?.LastError ?? "Unknown error")}");
            }

            _userRetriever = new UserRetriever(_serviceClient);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred while connecting to Dataverse: {ex.Message}");
            throw;
        }
    }

    private async Task ProcessUserAsync(string userIdentifier, string teamName, List<string> disabledUser)
    {
        if (disabledUser.Contains(userIdentifier))
        {
            return;
        }

        var users = await _userRetriever.FindUsersLegacyAsync(userIdentifier);
        if (users.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"User {userIdentifier} not found.");
            Console.ResetColor();
            disabledUser.Add(userIdentifier);
            return;
        }

        var user = users[0];

        if (user.GetAttributeValue<bool>("isdisabled"))
        {
            disabledUser.Add(userIdentifier);
            return;
        }

        string username = user.GetAttributeValue<string>("domainname").Split('@')[0];
        Console.WriteLine($"User {username} (active) - assigning team:");
        await EnsureUserHasTeam(user, teamName);
    }

    private async Task EnsureUserHasTeam(Entity user, string teamName)
    {
        var currentTeams = await GetUserTeamsAsync(user.Id);
        var currentTeamNames = currentTeams.Entities
            .Select(t => t.GetAttributeValue<string>("name")?.Trim())
            .Where(n => n != null)
            .ToList();

        // Case-insensitive comparison
        if (currentTeamNames.Any(t => t.Equals(teamName, StringComparison.OrdinalIgnoreCase)))
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"{teamName} (already assigned, skipped)");
            Console.ResetColor();
            return;
        }

        var team = await GetTeamByNameAsync(teamName);
        if (team != null)
        {
            var addMembersRequest = new AddMembersTeamRequest
            {
                TeamId = team.Id,
                MemberIds = new[] { user.Id }
            };

            await Task.Run(() => _serviceClient.Execute(addMembersRequest));
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"{teamName} (assigned)");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"{teamName} (team not found, skipped)");
            Console.ResetColor();
        }
    }

    private async Task<EntityCollection> GetUserTeamsAsync(Guid userId)
    {
        var query = new QueryExpression("team")
        {
            ColumnSet = new ColumnSet("name"),
            LinkEntities =
            {
                new LinkEntity
                {
                    LinkFromEntityName = "team",
                    LinkToEntityName = "teammembership",
                    LinkFromAttributeName = "teamid",
                    LinkToAttributeName = "teamid",
                    LinkCriteria = new FilterExpression
                    {
                        Conditions =
                        {
                            new ConditionExpression("systemuserid", ConditionOperator.Equal, userId)
                        }
                    }
                }
            }
        };

        return await Task.Run(() => _serviceClient.RetrieveMultiple(query));
    }

    private async Task<Entity> GetTeamByNameAsync(string teamName)
    {
        var query = new QueryExpression("team")
        {
            ColumnSet = new ColumnSet("teamid", "name"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("name", ConditionOperator.Equal, teamName)
                }
            }
        };

        var result = await Task.Run(() => _serviceClient.RetrieveMultiple(query));
        return result.Entities.FirstOrDefault();
    }
}