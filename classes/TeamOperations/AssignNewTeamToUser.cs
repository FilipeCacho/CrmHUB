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

        string result = firstContrataIndex == -1 ? input.Trim() :
            lowercaseInput.IndexOf("contrata", firstContrataIndex + 1) == -1 ? input.Trim() :
            input[..lowercaseInput.IndexOf("contrata", firstContrataIndex + 1)].Trim();

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

    private async Task<bool> AssignUserToTeamAsync(string userDomain, string equipoContrata)
    {
        string contrataContrataTeam = FindParkMasterDataTeam(equipoContrata);

        try
        {
            // Retrieve the user
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
                return false;
            }

            var userId = userResults.Entities[0].Id;

            // Check if the Contrata Contrata team exists
            var contrataTeamQuery = new QueryExpression("team")
            {
                ColumnSet = new ColumnSet("teamid"),
                Criteria = new FilterExpression()
            };
            contrataTeamQuery.Criteria.AddCondition("name", ConditionOperator.Equal, contrataContrataTeam);

            var contrataTeamResults = await Task.Run(() => serviceClient.RetrieveMultiple(contrataTeamQuery));
            if (contrataTeamResults.Entities.Count == 0)
            {
                Console.WriteLine($"Contrata Contrata team not found: {contrataContrataTeam}");
                return false;
            }

            var contrataTeamId = contrataTeamResults.Entities[0].Id;

            // Check if the user is already a member of the Contrata Contrata team
            var membershipQuery = new QueryExpression("teammembership")
            {
                Criteria = new FilterExpression()
            };
            membershipQuery.Criteria.AddCondition("systemuserid", ConditionOperator.Equal, userId);
            membershipQuery.Criteria.AddCondition("teamid", ConditionOperator.Equal, contrataTeamId);

            var membershipResults = await Task.Run(() => serviceClient.RetrieveMultiple(membershipQuery));

            bool contrataTeamAssigned = false;

            if (membershipResults.Entities.Count == 0)
            {
                try
                {
                    await Task.Run(() => serviceClient.Execute(new AssociateRequest
                    {
                        Target = new EntityReference("team", contrataTeamId),
                        RelatedEntities = new EntityReferenceCollection { new EntityReference("systemuser", userId) },
                        Relationship = new Relationship("teammembership_association")
                    }));

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write($"User {userDomain} successfully assigned to Contrata Contrata team");
                    Console.Write(contrataContrataTeam);
                    Console.ResetColor();

                    contrataTeamAssigned = true;
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Failed to assign user {userDomain} to Contrata Contrata team {contrataContrataTeam}: {ex.Message}");
                    Console.ResetColor();
                    return false;
                }
            }
            else
            {
                contrataTeamAssigned = true;
            }

            // Only proceed with new team assignment if Contrata Contrata team assignment was successful
            if (contrataTeamAssigned)
            {
                var newTeamQuery = new QueryExpression("team")
                {
                    ColumnSet = new ColumnSet("teamid"),
                    Criteria = new FilterExpression()
                };
                newTeamQuery.Criteria.AddCondition("name", ConditionOperator.Equal, equipoContrata);

                var newTeamResults = await Task.Run(() => serviceClient.RetrieveMultiple(newTeamQuery));
                if (newTeamResults.Entities.Count == 0)
                {
                    Console.WriteLine($"New team not found: {equipoContrata}");
                    return false;
                }

                var newTeamId = newTeamResults.Entities[0].Id;

                // Check if user is already a member of the new team
                var newMembershipQuery = new QueryExpression("teammembership")
                {
                    Criteria = new FilterExpression()
                };
                newMembershipQuery.Criteria.AddCondition("systemuserid", ConditionOperator.Equal, userId);
                newMembershipQuery.Criteria.AddCondition("teamid", ConditionOperator.Equal, newTeamId);

                var newMembershipResults = await Task.Run(() => serviceClient.RetrieveMultiple(newMembershipQuery));

                if (newMembershipResults.Entities.Count == 0)
                {
                    try
                    {
                        await Task.Run(() => serviceClient.Execute(new AssociateRequest
                        {
                            Target = new EntityReference("team", newTeamId),
                            RelatedEntities = new EntityReferenceCollection { new EntityReference("systemuser", userId) },
                            Relationship = new Relationship("teammembership_association")
                        }));
                        Console.WriteLine($"User {userDomain} successfully assigned to new team {equipoContrata}");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to assign user {userDomain} to new team {equipoContrata}: {ex.Message}");
                        return false;
                    }
                }
                else
                {
                    Console.WriteLine($"User {userDomain} is already a member of the new team {equipoContrata}");
                    return true;
                }
            }
            else
            {
                Console.WriteLine($"User {userDomain} could not be assigned to new team {equipoContrata} because Contrata Contrata team assignment failed");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred while assigning user to team: {ex.Message}");
            return false;
        }
    }
}

public class ProcessedUser
{
    public string UserDomain { get; set; }
    public string AssignedPark { get; set; }
}