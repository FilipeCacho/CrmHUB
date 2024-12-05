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