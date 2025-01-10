using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;


public sealed class PermissionCopier
{
    private readonly ServiceClient _serviceClient;

    public PermissionCopier(ServiceClient serviceClient)
    {
        _serviceClient = serviceClient ?? throw new ArgumentNullException(nameof(serviceClient));
    }

    public async Task CopyBusinessUnit(Entity sourceUser, Entity targetUser)
    {
        ArgumentNullException.ThrowIfNull(sourceUser);
        ArgumentNullException.ThrowIfNull(targetUser);

        if (!sourceUser.Contains("businessunitid"))
        {
            Console.WriteLine("Source user does not have a Business Unit assigned.");
            return;
        }

        var sourceBuId = ((EntityReference)sourceUser["businessunitid"]).Id;
        var updateEntity = new Entity("systemuser")
        {
            Id = targetUser.Id,
            ["businessunitid"] = new EntityReference("businessunit", sourceBuId)
        };

        await Task.Run(() => _serviceClient.Update(updateEntity));

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("\nBusiness Unit copied successfully.");
        Console.ResetColor();
    }

    public async Task CopyTeams(Entity sourceUser, Entity targetUser)
    {
        ArgumentNullException.ThrowIfNull(sourceUser);
        ArgumentNullException.ThrowIfNull(targetUser);

        var sourceTeams = await GetUserTeamsAsync(sourceUser.Id);
        var targetTeams = await GetUserTeamsAsync(targetUser.Id);

        Console.WriteLine("\n");

        foreach (var team in sourceTeams.Entities)
        {
            if (!targetTeams.Entities.Any(t => t.Id == team.Id))
            {
                var addMembersRequest = new AddMembersTeamRequest
                {
                    TeamId = team.Id,
                    MemberIds = new[] { targetUser.Id }
                };

                try
                {
                    await Task.Run(() => _serviceClient.Execute(addMembersRequest));

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"User added to team '{team.GetAttributeValue<string>("name")}'.");
                    Console.ResetColor();
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Error adding user to team '{team.GetAttributeValue<string>("name")}': {ex.Message}");
                    Console.ResetColor();
                }
            }
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("\nTeam memberships copied successfully.\n");
        Console.ResetColor();
    }

    public async Task CopyRoles(Entity sourceUser, Entity targetUser)
    {
        ArgumentNullException.ThrowIfNull(sourceUser);
        ArgumentNullException.ThrowIfNull(targetUser);

        var sourceRoles = await GetUserRolesAsync(sourceUser.Id);
        var targetRoles = await GetUserRolesAsync(targetUser.Id);
        var targetUserBusinessUnit = ((EntityReference)targetUser["businessunitid"]).Id;

        foreach (var sourceRole in sourceRoles.Entities)
        {
            // Find an equivalent role in the target user's Business Unit
            var equivalentRole = await FindEquivalentRoleInBusinessUnitAsync(sourceRole, targetUserBusinessUnit);

            if (equivalentRole == null)
            {
                Console.WriteLine($"No equivalent role found for '{sourceRole.GetAttributeValue<string>("name")}' in the target user's Business Unit.");
                continue;
            }

            if (!targetRoles.Entities.Any(r => r.Id == equivalentRole.Id))
            {
                try
                {
                    var request = new AssociateRequest
                    {
                        Target = new EntityReference("systemuser", targetUser.Id),
                        RelatedEntities = new EntityReferenceCollection
                        {
                            new EntityReference("role", equivalentRole.Id)
                        },
                        Relationship = new Relationship("systemuserroles_association")
                    };

                    await Task.Run(() => _serviceClient.Execute(request));

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"Role '{equivalentRole.GetAttributeValue<string>("name")}' assigned to user.");
                    Console.ResetColor();
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Error assigning role '{equivalentRole.GetAttributeValue<string>("name")}' to user: {ex.Message}");
                    Console.ResetColor();
                }
            }
        }

        Console.WriteLine("Roles copying process completed.");
    }

    private async Task<Entity?> FindEquivalentRoleInBusinessUnitAsync(Entity sourceRole, Guid targetBusinessUnitId)
    {
        ArgumentNullException.ThrowIfNull(sourceRole);

        var query = new QueryExpression("role")
        {
            ColumnSet = new ColumnSet("name"),
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

    public async Task<EntityCollection> GetUserTeamsAsync(Guid userId)
    {
        // Retrieve the user's Business Unit
        var userQuery = new QueryExpression("systemuser")
        {
            ColumnSet = new ColumnSet("businessunitid"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("systemuserid", ConditionOperator.Equal, userId),
                    new ConditionExpression("isdisabled", ConditionOperator.Equal, false)
                }
            }
        };

        var userResult = await Task.Run(() => _serviceClient.RetrieveMultiple(userQuery));
        if (!userResult.Entities.Any())
        {
            throw new Exception($"User with ID {userId} not found or is disabled.");
        }

        var userBusinessUnitId = ((EntityReference)userResult.Entities[0]["businessunitid"]).Id;

        // Retrieve the Business Unit name
        var buQuery = new QueryExpression("businessunit")
        {
            ColumnSet = new ColumnSet("name"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("businessunitid", ConditionOperator.Equal, userBusinessUnitId)
                }
            }
        };

        var buResult = await Task.Run(() => _serviceClient.RetrieveMultiple(buQuery));
        var businessUnitName = buResult.Entities[0].GetAttributeValue<string>("name");

        // Retrieve the user's teams, including the Business Unit ID for each team
        var teamQuery = new QueryExpression("team")
        {
            ColumnSet = new ColumnSet("name", "businessunitid"),
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

        var result = await Task.Run(() => _serviceClient.RetrieveMultiple(teamQuery));

        // Filter out only the teams that don't match the user's Business Unit name
        var filteredTeams = result.Entities
            .Where(e => e.GetAttributeValue<string>("name") != businessUnitName)
            .ToList();

        return new EntityCollection(filteredTeams);
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
                                    new ConditionExpression("systemuserid", ConditionOperator.Equal, userId),
                                    new ConditionExpression("isdisabled", ConditionOperator.Equal, false)
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