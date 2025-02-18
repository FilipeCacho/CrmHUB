using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using System.Text.RegularExpressions;

public class AssignNewTeamToUser
{
    private readonly List<BuUserDomains> buUserDomainsList;
    private readonly Dictionary<string, string> parkToEquipoContrata;
    private readonly ServiceClient serviceClient;

    public AssignNewTeamToUser(List<BuUserDomains> buUserDomainsList)
    {
        this.buUserDomainsList = buUserDomainsList;
        this.parkToEquipoContrata = new Dictionary<string, string>();
        this.serviceClient = SessionManager.Instance.GetClient();
    }

    public async Task<List<ProcessedUser>> ProcessUsersAsync()
    {
        try
        {
            if (buUserDomainsList == null || !buUserDomainsList.Any())
            {
                Console.WriteLine("There are no users to process.");
                Console.WriteLine("Press any key to continue...");
                Console.ReadKey();
                return new List<ProcessedUser>();
            }

            CreateEquipoContrataMapping();
            return await AssignUsersToParksAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred in ProcessUsersAsync: {ex.Message}");
            throw;
        }
    }

    private void CreateEquipoContrataMapping()
    {
        foreach (var buUserDomain in buUserDomainsList)
        {
            string equipoContrata = AdjustCreatedTeamName(buUserDomain.NewCreatedPark);
            parkToEquipoContrata[buUserDomain.NewCreatedPark] = equipoContrata;
        }
    }

    private static string AdjustCreatedTeamName(string parkName)
    {
        string[] words = parkName.Trim().Split(' ');
        int lastValidIndex = words.Length - 1;

        for (int i = words.Length - 1; i >= 0; i--)
        {
            if (Regex.IsMatch(words[i], @"^[a-zA-Z]+-[a-zA-Z]+-[a-zA-Z]+$"))
            {
                lastValidIndex = i;
                break;
            }
        }

        return string.Join(" ", words.Take(lastValidIndex + 1)).Trim();
    }

    private async Task<List<ProcessedUser>> AssignUsersToParksAsync()
    {
        List<ProcessedUser> processedUsers = new();
        foreach (var buUserDomain in buUserDomainsList)
        {
            string equipaContrata = parkToEquipoContrata[buUserDomain.NewCreatedPark];
            foreach (var userDomain in buUserDomain.UserDomains)
            {
                bool assigned = await AssignUserToTeamAsync(userDomain, equipaContrata);
                if (assigned)
                {
                    processedUsers.Add(new ProcessedUser
                    {
                        UserDomain = userDomain,
                        AssignedPark = buUserDomain.NewCreatedPark
                    });

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write($"User {userDomain} successfully processed for park\n");
                    Console.ResetColor();
                    Console.Write(buUserDomain.NewCreatedPark);
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"User {userDomain} could not be processed for park {buUserDomain.NewCreatedPark}. Check previous errors for details.");
                }
            }
        }

        Console.ForegroundColor = ConsoleColor.Blue;
        Console.WriteLine("\nPress any key to continue...");
        Console.ResetColor();
        Console.ReadKey();

        return processedUsers;
    }

    public static string FindParkMasterDataTeam(string input)
    {
        string lowercaseInput = input.ToLower();
        int firstContrataIndex = lowercaseInput.IndexOf("contrata");

        // If no "contrata" found, return original input
        if (firstContrataIndex == -1) return input.Trim();

        // Find second "contrata"
        int secondContrataIndex = lowercaseInput.IndexOf("contrata", firstContrataIndex + 1);

        // If no second "contrata", return original input
        if (secondContrataIndex == -1) return input.Trim();

        // Take everything up to and including the second "Contrata"
        int endPosition = secondContrataIndex + "contrata".Length;
        string result = input.Substring(0, endPosition).Trim();

        // Keep the ZP check from the original code
        if (result.Length >= 3)
        {
            string lastThreeChars = result[^3..];
            if (lastThreeChars.StartsWith("ZP", StringComparison.OrdinalIgnoreCase))
            {
                result = result[..^3].Trim();
            }
        }

        return result;
    }

    private async Task<bool> AssignUserToTeamAsync(string userDomain, string fullTeamName)
    {
        try
        {
            // Extract the team name variations we need
            var teamNames = ExtractTeamNames(fullTeamName);
            var baseTeam = teamNames.BaseTeam;
            var intermediateTeam = teamNames.IntermediateTeam;
            var finalTeam = teamNames.FinalTeam;

            // Get user ID
            var userId = await GetUserIdAsync(userDomain);
            if (!userId.HasValue) return false;

            // Check if user is in intermediate team first
            var intermediateTeamId = await GetTeamIdAsync(intermediateTeam);
            if (!intermediateTeamId.HasValue)
            {
                Console.WriteLine($"Intermediate team not found: {intermediateTeam}");
                return false;
            }

            // Check if user is member of intermediate team
            if (!await IsUserTeamMemberAsync(userId.Value, intermediateTeamId.Value))
            {
                Console.WriteLine($"User {userDomain} is not a member of intermediate team {intermediateTeam}");
                return false;
            }

            // Only proceed with assignments if user is in intermediate team

            // Assign to base team
            var baseTeamId = await GetTeamIdAsync(baseTeam);
            if (!baseTeamId.HasValue)
            {
                Console.WriteLine($"Base team not found: {baseTeam}");
                return false;
            }
            await AssignUserToSingleTeamAsync(userId.Value, baseTeamId.Value, userDomain, baseTeam);

            // Assign to final team
            var finalTeamId = await GetTeamIdAsync(finalTeam);
            if (!finalTeamId.HasValue)
            {
                Console.WriteLine($"Final team not found: {finalTeam}");
                return false;
            }
            await AssignUserToSingleTeamAsync(userId.Value, finalTeamId.Value, userDomain, finalTeam);

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in AssignUserToTeamAsync: {ex.Message}");
            return false;
        }
    }

    private class TeamNames
    {
        public string BaseTeam { get; set; }
        public string IntermediateTeam { get; set; }
        public string FinalTeam { get; set; }
    }

    private TeamNames ExtractTeamNames(string fullTeamName)
    {
        // Split by "Contrata" while preserving casing from original string
        var parts = fullTeamName.Split(new[] { " Contrata " }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return new TeamNames();  // Return empty object instead of null

        var basePattern = parts[0];

        // Find the hyphenated pattern (e.g., "0-ES-BDY-01")
        var hyphenMatch = Regex.Match(basePattern, @"\d+-[A-Z]+-[A-Z]+-\d+");
        if (!hyphenMatch.Success) return new TeamNames();  // Return empty object instead of null

        var hyphenPattern = hyphenMatch.Value;
        var beforeHyphen = basePattern.Substring(0, hyphenMatch.Index).Trim();

        return new TeamNames
        {
            BaseTeam = $"{beforeHyphen} {hyphenPattern}".Trim(),
            IntermediateTeam = $"{beforeHyphen} {hyphenPattern} Contrata".Trim(),
            FinalTeam = fullTeamName.Trim()
        };
    }

    private async Task<Guid?> GetUserIdAsync(string userDomain)
    {
        var userQuery = new QueryExpression("systemuser")
        {
            ColumnSet = new ColumnSet("systemuserid"),
            Criteria = new FilterExpression()
        };
        userQuery.Criteria.AddCondition("domainname", ConditionOperator.Equal, userDomain);

        var userResults = await Task.Run(() => serviceClient.RetrieveMultiple(userQuery));
        if (userResults.Entities.Count == 0)
        {
            Console.WriteLine($"User not found: {userDomain}");
            return null;
        }
        return userResults.Entities[0].Id;
    }

    private async Task<Guid?> GetTeamIdAsync(string teamName)
    {
        var teamQuery = new QueryExpression("team")
        {
            ColumnSet = new ColumnSet("teamid"),
            Criteria = new FilterExpression()
        };
        teamQuery.Criteria.AddCondition("name", ConditionOperator.Equal, teamName);

        var teamResults = await Task.Run(() => serviceClient.RetrieveMultiple(teamQuery));
        return teamResults.Entities.Count > 0 ? teamResults.Entities[0].Id : (Guid?)null;
    }

    private async Task<bool> AssignUserToSingleTeamAsync(Guid userId, Guid teamId, string userDomain, string teamName)
    {
        try
        {
            // Check if already a member
            if (await IsUserTeamMemberAsync(userId, teamId)) return true;

            await Task.Run(() => serviceClient.Execute(new AssociateRequest
            {
                Target = new EntityReference("team", teamId),
                RelatedEntities = new EntityReferenceCollection { new EntityReference("systemuser", userId) },
                Relationship = new Relationship("teammembership_association")
            }));

            Console.WriteLine($"User {userDomain} successfully assigned to team {teamName}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to assign user {userDomain} to team {teamName}: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> RemoveUserFromTeamAsync(Guid userId, Guid teamId, string userDomain, string teamName)
    {
        try
        {
            if (!await IsUserTeamMemberAsync(userId, teamId)) return true;

            await Task.Run(() => serviceClient.Execute(new DisassociateRequest
            {
                Target = new EntityReference("team", teamId),
                RelatedEntities = new EntityReferenceCollection { new EntityReference("systemuser", userId) },
                Relationship = new Relationship("teammembership_association")
            }));

            Console.WriteLine($"User {userDomain} successfully removed from team {teamName}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to remove user {userDomain} from team {teamName}: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> IsUserTeamMemberAsync(Guid userId, Guid teamId)
    {
        var membershipQuery = new QueryExpression("teammembership")
        {
            Criteria = new FilterExpression()
        };
        membershipQuery.Criteria.AddCondition("systemuserid", ConditionOperator.Equal, userId);
        membershipQuery.Criteria.AddCondition("teamid", ConditionOperator.Equal, teamId);

        var membershipResults = await Task.Run(() => serviceClient.RetrieveMultiple(membershipQuery));
        return membershipResults.Entities.Count > 0;
    }
}
public class ProcessedUser
{
    public string UserDomain { get; set; }
    public string AssignedPark { get; set; }
}