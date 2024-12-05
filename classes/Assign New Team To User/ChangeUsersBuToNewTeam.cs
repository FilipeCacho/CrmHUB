using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using System.Text.RegularExpressions;

public sealed class ChangeUsersBuToNewTeam
{
    // Modern C# record for preprocessed data
    private sealed record PreprocessedBuData
    {
        public required string OriginalNewCreatedPark { get; init; }
        public required string ProcessedNewBU { get; init; }
        public required List<string> UserDomains { get; init; }
    }

    private readonly ServiceClient _serviceClient;
    private readonly UserRetriever _userRetriever;

    // Use dependency injection pattern for ServiceClient
    public ChangeUsersBuToNewTeam()
    {
        _serviceClient = SessionManager.Instance.GetClient();
        _userRetriever = new UserRetriever(_serviceClient);
    }

    public async Task RunAsync(List<BuUserDomains> buUserDomainsList, List<TransformedTeamData> transformedTeams)
    {
        ArgumentNullException.ThrowIfNull(buUserDomainsList);
        ArgumentNullException.ThrowIfNull(transformedTeams);

        try
        {
            // Preprocess the data using modern LINQ
            var preprocessedData = buUserDomainsList.Select(item => new PreprocessedBuData
            {
                OriginalNewCreatedPark = item.NewCreatedPark,
                ProcessedNewBU = ProcessNewCreatedPark(item.NewCreatedPark),
                UserDomains = item.UserDomains
            }).ToList();

            // Process each user domain with modern foreach
            foreach (var item in preprocessedData)
            {
                foreach (var userDomain in item.UserDomains)
                {
                    await ProcessUserAsync(userDomain, item.OriginalNewCreatedPark, item.ProcessedNewBU, transformedTeams);
                }
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Process completed successfully.");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            LogError($"An error occurred in the main process: {ex.Message}", ex);
            throw;
        }
    }

    private static string ProcessNewCreatedPark(string newCreatedPark)
    {
        ArgumentNullException.ThrowIfNull(newCreatedPark);
        return Regex.Replace(newCreatedPark, @"^Equipo contrata\s*", "", RegexOptions.IgnoreCase).Trim();
    }

    private async Task ProcessUserAsync(string userDomain, string originalNewCreatedPark, string processedNewBU, List<TransformedTeamData> transformedTeams)
    {
        try
        {
            var users = await _userRetriever.FindUsersAsync(userDomain);
            if (users.Count == 0)
            {
                LogWarning($"User with domain {userDomain} not found.");
                return;
            }

            var user = users[0]; // Take the first user if multiple are found
            string fullName = user.GetAttributeValue<string>("fullname") ?? string.Empty;
            var currentBu = user.GetAttributeValue<EntityReference>("businessunitid");

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("\nProcessing User ");
            Console.ResetColor();
            Console.WriteLine($"{fullName}, Current BU: {currentBu.Name}");

            // Find matching team with null check
            var matchingTeam = FindMatchingTeam(currentBu.Name, transformedTeams);
            if (matchingTeam == null)
            {
                LogWarning($"No matching team found for BU {currentBu.Name}. Skipping user {fullName}.");
                Console.WriteLine("Available teams:");
                foreach (var team in transformedTeams)
                {
                    Console.WriteLine($"- {team.EquipaContrataContrata}");
                }
                return;
            }

            // Now that we know matchingTeam is not null, we can safely use its properties
            Console.WriteLine($"Found matching team with contractor: {matchingTeam.Value.Contractor}");
            if (!UserNameContainsContractor(fullName, matchingTeam.Value.Contractor))
            {
                Console.WriteLine($"User {fullName} name does not contain contractor {matchingTeam.Value.Contractor}. Skipping.");
                return;
            }

            string newBuName = DeriveNewBuName(originalNewCreatedPark);
            await ChangeBuAndReapplyRolesAsync(user, newBuName, originalNewCreatedPark);
        }
        catch (Exception ex)
        {
            LogError($"Error processing user {userDomain}", ex);
            throw;
        }
    }

    private TransformedTeamData? FindMatchingTeam(string currentBuName, List<TransformedTeamData> transformedTeams)
    {
        Console.WriteLine($"Looking for BU match for: {currentBuName}");

        // Get unique contractor codes using modern LINQ
        var contractorCodes = transformedTeams
            .Select(t => t.ContractorCode)
            .Distinct()
            .ToList();

        // Extract base BU name
        string baseBuName = contractorCodes.Aggregate(
            currentBuName,
            (current, code) => current.Replace($" {code}", ""))
            .Replace(" Contrata", "")
            .Trim();

        // Debug output
        foreach (var team in transformedTeams)
        {
            string teamBaseBu = contractorCodes.Aggregate(
                team.Bu,
                (current, code) => current.Replace($" {code}", ""))
                .Replace(" Contrata", "")
                .Trim();

            Console.WriteLine($"Comparing with: {team.Bu}");
            Console.WriteLine($"Base BU comparison: '{baseBuName}' vs '{teamBaseBu}'");
            Console.WriteLine($"This team's contractor is: {team.Contractor}");
        }

        // Find matching team using modern LINQ and handle nullable properly
        var matchingTeam = transformedTeams.FirstOrDefault(t =>
        {
            string teamBaseBu = contractorCodes.Aggregate(
                t.Bu,
                (current, code) => current.Replace($" {code}", ""))
                .Replace(" Contrata", "")
                .Trim();

            return string.Equals(teamBaseBu, baseBuName, StringComparison.OrdinalIgnoreCase);
        });

        // Return null if no match is found
        return matchingTeam.Equals(default(TransformedTeamData)) ? null : matchingTeam;
    }

    private static bool UserNameContainsContractor(string userName, string contractor)
    {
        if (string.IsNullOrEmpty(userName) || string.IsNullOrEmpty(contractor))
        {
            Console.WriteLine($"Debug: userName: '{userName}', contractor: '{contractor}'");
            return false;
        }

        var contractorWords = contractor.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Console.WriteLine($"Debug: Checking if '{userName}' contains any of these words: {string.Join(", ", contractorWords)}");

        var result = contractorWords.Any(word =>
            userName.Contains(word, StringComparison.OrdinalIgnoreCase));

        Console.WriteLine($"Debug: Result of contractor check: {result}");
        return result;
    }

    private static string DeriveNewBuName(string originalNewCreatedPark)
    {
        return Regex.Replace(originalNewCreatedPark, @"^Equipo contrata\s*", "", RegexOptions.IgnoreCase).Trim();
    }

    private async Task ChangeBuAndReapplyRolesAsync(Entity user, string newBuName, string originalNewCreatedPark)
    {
        var currentRoles = await GetUserRolesAsync(user.Id);
        var newBu = await GetBusinessUnitByNameAsync(newBuName);

        if (newBu is null)
        {
            LogWarning($"Business Unit {newBuName} not found. Skipping BU change for user {user.GetAttributeValue<string>("yomifullname")}.");
            return;
        }

        // Change user's BU using modern entity reference
        user["businessunitid"] = newBu.ToEntityReference();
        await Task.Run(() => _serviceClient.Update(user));

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Changed BU for user {user.GetAttributeValue<string>("yomifullname")} to {newBuName}.");
        Console.ResetColor();

        // Reapply roles and remove team
        await ReapplyRolesToUserAsync(user.Id, newBu.Id, currentRoles);
        await RemoveContrataContrataTeamAsync(user, originalNewCreatedPark);
    }

    private async Task<List<Entity>> GetUserRolesAsync(Guid userId)
    {
        var query = new QueryExpression("role")
        {
            ColumnSet = new ColumnSet("roleid", "name"),
            LinkEntities =
            {
                new LinkEntity
                {
                    LinkFromEntityName = "role",
                    LinkToEntityName = "systemuserroles",
                    LinkFromAttributeName = "roleid",
                    LinkToAttributeName = "roleid",
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

        var result = await Task.Run(() => _serviceClient.RetrieveMultiple(query));
        return result.Entities.ToList();
    }

    private async Task ReapplyRolesToUserAsync(Guid userId, Guid newBuId, List<Entity> roles)
    {
        foreach (var role in roles)
        {
            try
            {
                var newRole = await FindEquivalentRoleInBusinessUnitAsync(role, newBuId);
                if (newRole is not null)
                {
                    await AssignRoleToUserAsync(userId, newRole.Id);
                    Console.WriteLine($"Reapplied role {newRole.GetAttributeValue<string>("name")} to user.");
                }
                else
                {
                    LogWarning($"Equivalent role for {role.GetAttributeValue<string>("name")} not found in new BU.");
                }
            }
            catch (Exception ex)
            {
                LogError($"Error reapplying role {role.GetAttributeValue<string>("name")}", ex);
            }
        }
    }

    private async Task RemoveContrataContrataTeamAsync(Entity user, string newCreatedPark)
    {
        string contrataContrataTeam = DeriveContrataContrataTeam(newCreatedPark);
        var userTeams = await GetUserTeamsAsync(user.Id);
        var teamToRemove = userTeams.FirstOrDefault(t =>
            string.Equals(t.GetAttributeValue<string>("name"), contrataContrataTeam, StringComparison.OrdinalIgnoreCase));

        if (teamToRemove is not null)
        {
            await RemoveTeamFromUserAsync(user.Id, teamToRemove.Id);
            Console.WriteLine($"Removed team '{contrataContrataTeam}' from user {user.GetAttributeValue<string>("yomifullname")}.");
        }
    }

    private static string DeriveContrataContrataTeam(string newCreatedPark)
    {
        var match = Regex.Match(newCreatedPark, @"(Equipo contrata.*?Contrata)");
        return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
    }

    private async Task<List<Entity>> GetUserTeamsAsync(Guid userId)
    {
        var query = new QueryExpression("team")
        {
            ColumnSet = new ColumnSet("teamid", "name"),
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

        var result = await Task.Run(() => _serviceClient.RetrieveMultiple(query));
        return result.Entities.ToList();
    }

    private async Task RemoveTeamFromUserAsync(Guid userId, Guid teamId)
    {
        var request = new DisassociateRequest
        {
            Target = new EntityReference("systemuser", userId),
            RelatedEntities = new EntityReferenceCollection
            {
                new EntityReference("team", teamId)
            },
            Relationship = new Relationship("teammembership_association")
        };

        await Task.Run(() => _serviceClient.Execute(request));
    }

    private async Task<Entity?> FindEquivalentRoleInBusinessUnitAsync(Entity sourceRole, Guid targetBusinessUnitId)
    {
        var query = new QueryExpression("role")
        {
            ColumnSet = new ColumnSet("roleid", "name"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("name", ConditionOperator.Equal, sourceRole.GetAttributeValue<string>("name")),
                    new ConditionExpression("businessunitid", ConditionOperator.Equal, targetBusinessUnitId)
                }
            }
        };

        var result = await Task.Run(() => _serviceClient.RetrieveMultiple(query));
        return result.Entities.FirstOrDefault();
    }

    private async Task AssignRoleToUserAsync(Guid userId, Guid roleId)
    {
        var request = new AssociateRequest
        {
            Target = new EntityReference("systemuser", userId),
            RelatedEntities = new EntityReferenceCollection
            {
                new EntityReference("role", roleId)
            },
            Relationship = new Relationship("systemuserroles_association")
        };

        await Task.Run(() => _serviceClient.Execute(request));
    }

    private async Task<Entity?> GetBusinessUnitByNameAsync(string buName)
    {
        var query = new QueryExpression("businessunit")
        {
            ColumnSet = new ColumnSet("businessunitid", "name"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("name", ConditionOperator.Equal, buName)
                }
            }
        };

        var result = await Task.Run(() => _serviceClient.RetrieveMultiple(query));
        return result.Entities.FirstOrDefault();
    }

    private static void LogError(string message, Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"ERROR: {message}");
        Console.WriteLine($"Exception: {ex.Message}");
        Console.WriteLine($"Stack Trace: {ex.StackTrace}");
        Console.ResetColor();
    }

    private static void LogWarning(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"WARNING: {message}");
        Console.ResetColor();
    }
}

// Extension method for MainMenuHandler to handle the change users BU operation
public static class ChangeUsersBuMenuExtension
{
    public static async Task ChangeUsersBuToNewTeamAsync(this MainMenuHandler menuHandler, List<BuUserDomains>? buUserDomainsList)
    {
        ArgumentNullException.ThrowIfNull(menuHandler);

        if (buUserDomainsList is null)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("You must run Option 2 first to extract users from teams, if you already run it then it means there is no users to process");
            Console.ResetColor();
            Console.WriteLine("\nPress any key to return to the main menu");
            Console.ReadKey();
            return;
        }

        try
        {
            var transformedTeams = FormatBUandTeams.FormatTeamData();
            if (transformedTeams is { Count: > 0 })
            {
                var changeUsersBuManager = new ChangeUsersBuToNewTeam();
                await changeUsersBuManager.RunAsync(buUserDomainsList, transformedTeams);
            }
            else
            {
                Console.WriteLine("No transformed teams data available. Please run option 1 first.");
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error during BU change process: {ex.Message}");
            Console.ResetColor();
        }

        Console.WriteLine("\nPress any key to continue...");
        Console.ReadKey();
    }
}