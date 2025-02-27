using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

    public class UserRetriever
    {
        private readonly ServiceClient _serviceClient;
        public static readonly Entity Exit = new("systemuser");

        public UserRetriever(ServiceClient serviceClient)
        {
            _serviceClient = serviceClient;
        }

    public static readonly Entity ExitLegacy = new("exit");

    public async Task<Entity?> PromptAndRetrieveUserLegacy(string prompt)
    {
        while (true)
        {
            Console.Write(prompt);
            var input = Console.ReadLine();

            //if the users inputs 0 it sends entity type back with a exit to indicate the code must go back to main
            if (input == "0")
            {
                return ExitLegacy;
            }

            var users = await FindUsersLegacyAsync(input);

            if (users.Count == 0)
            {
                Console.WriteLine("No users found. Please try again.");
                Console.WriteLine("If the user is not found it might be disabled");
                continue;
            }

            if (users.Count == 1)
            {
                return users[0];
            }

            while (true)
            {
                Console.WriteLine("\nMultiple users found:");
                for (int i = 0; i < users.Count; i++)
                {
                    string domainName = users[i].Contains("domainname") ? users[i]["domainname"].ToString() : "N/A";
                    string username = domainName != "N/A" ? domainName.Split('@')[0] : "N/A";
                    string fullName = users[i].Contains("fullname") ? users[i]["fullname"].ToString() : "N/A";
                    Console.WriteLine($"({i + 1}) {fullName} (Username: {username})");
                }

                Console.Write($"\nSelect one of the users (1-{users.Count}), or press 0 to go back to the previous search: ");
                if (int.TryParse(Console.ReadLine(), out int selection))
                {
                    if (selection == 0)
                    {
                        // Go back to the previous search
                        break;
                    }
                    else if (selection >= 1 && selection <= users.Count)
                    {
                        return users[selection - 1];
                    }
                }

                Console.WriteLine("\nInvalid selection. Please try again. Press any key to retry");

                Console.ReadKey();
                Console.Clear();
            }
        }
    }

    public async Task<List<Entity>> FindUsersLegacyAsync(string input)
    {
        var query = new QueryExpression("systemuser")
        {
            ColumnSet = new ColumnSet("fullname", "businessunitid", "domainname", "internalemailaddress", "windowsliveid"),
            Criteria = new FilterExpression(LogicalOperator.And)
        };

        query.Criteria.AddCondition("isdisabled", ConditionOperator.Equal, false);

        var orFilter = new FilterExpression(LogicalOperator.Or);
        orFilter.AddCondition("domainname", ConditionOperator.BeginsWith, input);
        orFilter.AddCondition("internalemailaddress", ConditionOperator.BeginsWith, input);
        orFilter.AddCondition("windowsliveid", ConditionOperator.BeginsWith, input);
        orFilter.AddCondition("fullname", ConditionOperator.Like, $"%{input}%");
        orFilter.AddCondition("yomifullname", ConditionOperator.Like, $"%{input}%");

        query.Criteria.AddFilter(orFilter);

        var result = await Task.Run(() => _serviceClient.RetrieveMultiple(query));

        // Perform additional case-insensitive filtering in memory
        var filteredResults = result.Entities.Where(e =>
            (e.Contains("domainname") && e["domainname"].ToString().IndexOf(input, StringComparison.OrdinalIgnoreCase) >= 0) ||
            (e.Contains("internalemailaddress") && e["internalemailaddress"].ToString().IndexOf(input, StringComparison.OrdinalIgnoreCase) >= 0) ||
            (e.Contains("windowsliveid") && e["windowsliveid"].ToString().IndexOf(input, StringComparison.OrdinalIgnoreCase) >= 0) ||
            (e.Contains("fullname") && e["fullname"].ToString().IndexOf(input, StringComparison.OrdinalIgnoreCase) >= 0) ||
            (e.Contains("yomifullname") && e["yomifullname"].ToString().IndexOf(input, StringComparison.OrdinalIgnoreCase) >= 0)
        ).ToList();

        return filteredResults;
    }

    public async Task<Entity> RetrieveUserAsync(Guid userId)
    {
        return await Task.Run(() => _serviceClient.Retrieve("systemuser", userId, new ColumnSet(true)));
    }

    public async Task<Entity?> PromptAndRetrieveUserAsync(string prompt)
        {
            while (true)
            {
                Console.Write(prompt);
                string? input = Console.ReadLine();

                if (string.IsNullOrEmpty(input) || input == "0")
                    return null;

                var users = await FindUsersAsync(input);

                if (!users.Any())
                {
                    Console.WriteLine("No users found matching the search criteria.");
                    continue;
                }

                if (users.Count == 1)
                    return users[0];

                Console.WriteLine("\nMultiple users found. Please select one:");
                for (int i = 0; i < users.Count; i++)
                {
                    Console.WriteLine($"{i + 1}. {users[i].GetAttributeValue<string>("fullname")} ({users[i].GetAttributeValue<string>("domainname")})");
                }

                Console.Write("\nEnter number (or 0 to search again): ");
                if (int.TryParse(Console.ReadLine(), out int selection) && selection > 0 && selection <= users.Count)
                {
                    return users[selection - 1];
                }

                if (selection == 0)
                    continue;

                Console.WriteLine("Invalid selection.");
            }
        }

        public async Task<List<Entity>> FindUsersAsync(string searchText)
        {
            var query = new QueryExpression("systemuser")
            {
                ColumnSet = new ColumnSet("fullname", "domainname", "businessunitid"),
                Criteria = new FilterExpression(LogicalOperator.Or)
                {
                    Conditions =
                    {
                        new ConditionExpression("fullname", ConditionOperator.Like, $"%{searchText}%"),
                        new ConditionExpression("domainname", ConditionOperator.Like, $"%{searchText}%")
                    }
                }
            };

            // Add link-entity for business unit name
            query.LinkEntities.Add(new LinkEntity
            {
                LinkFromEntityName = "systemuser",
                LinkToEntityName = "businessunit",
                LinkFromAttributeName = "businessunitid",
                LinkToAttributeName = "businessunitid",
                JoinOperator = JoinOperator.Inner,
                Columns = new ColumnSet("name")
            });

            var result = await Task.Run(() => _serviceClient.RetrieveMultiple(query));
            return result.Entities.ToList();
        }
    }

    public class PermissionRetriever
    {
        private readonly ServiceClient _serviceClient;

        public PermissionRetriever(ServiceClient serviceClient)
        {
            _serviceClient = serviceClient;
        }

        public async Task<EntityCollection> GetUserRolesAsync(Guid userId)
        {
            var query = new QueryExpression("role")
            {
                ColumnSet = new ColumnSet("name"),
                LinkEntities =
                {
                    new LinkEntity
                    {
                        LinkFromEntityName = "role",
                        LinkToEntityName = "systemuserroles",
                        LinkFromAttributeName = "roleid",
                        LinkToAttributeName = "roleid",
                        JoinOperator = JoinOperator.Inner,
                        LinkEntities =
                        {
                            new LinkEntity
                            {
                                LinkFromEntityName = "systemuserroles",
                                LinkToEntityName = "systemuser",
                                LinkFromAttributeName = "systemuserid",
                                LinkToAttributeName = "systemuserid",
                                LinkCriteria = new FilterExpression
                                {
                                    Conditions =
                                    {
                                        new ConditionExpression("systemuserid", ConditionOperator.Equal, userId)
                                    }
                                }
                            }
                        }
                    }
                }
            };

            return await Task.Run(() => _serviceClient.RetrieveMultiple(query));
        }

        public async Task<EntityCollection> GetUserTeamsAsync(Guid userId)
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
                        JoinOperator = JoinOperator.Inner,
                        LinkCriteria = new FilterExpression(),
                        LinkEntities =
                        {
                            new LinkEntity
                            {
                                LinkFromEntityName = "teammembership",
                                LinkToEntityName = "systemuser",
                                LinkFromAttributeName = "systemuserid",
                                LinkToAttributeName = "systemuserid",
                                LinkCriteria = new FilterExpression
                                {
                                    Conditions =
                                    {
                                        new ConditionExpression("systemuserid", ConditionOperator.Equal, userId)
                                    }
                                }
                            }
                        }
                    }
                }
            };

            return await Task.Run(() => _serviceClient.RetrieveMultiple(query));
        }
    }