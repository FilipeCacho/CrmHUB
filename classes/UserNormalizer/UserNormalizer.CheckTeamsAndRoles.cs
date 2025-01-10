using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;


public sealed partial class UserNormalizerV2
{
    private async Task EnsureUserHasRoles(Entity user, string[] rolesToEnsure)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(rolesToEnsure);

        if (!user.Contains("businessunitid"))
        {
            throw new InvalidOperationException("User does not have a business unit assigned.");
        }

        var userBusinessUnitId = ((EntityReference)user["businessunitid"]).Id;
        var currentRoles = await RetrieveUserRolesAsync(user.Id, userBusinessUnitId);
        var currentRoleNames = currentRoles
            .Select(r => r.GetAttributeValue<string>("name"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var availableRoles = await RetrieveAvailableRolesAsync(userBusinessUnitId, rolesToEnsure);

        foreach (var role in availableRoles.Entities)
        {
            var roleName = role.GetAttributeValue<string>("name");
            if (string.IsNullOrEmpty(roleName))
            {
                continue;
            }

            if (!currentRoleNames.Contains(roleName))
            {
                await AssignRoleToUserAsync(user.Id, role.Id, roleName);
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"Role already assigned: {roleName}");
                Console.ResetColor();
            }
        }

        var missingRoles = rolesToEnsure.Except(
            availableRoles.Entities.Select(r => r.GetAttributeValue<string>("name")),
            StringComparer.OrdinalIgnoreCase);

        foreach (var roleName in missingRoles)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Role not found in user's business unit: {roleName}");
            Console.ResetColor();
        }
    }

    private async Task EnsureUserHasTeams(Entity user, string[] teamsToEnsure)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(teamsToEnsure);

        var currentTeams = await _permissionCopier.GetUserTeamsAsync(user.Id);
        var currentTeamNames = currentTeams.Entities
            .Select(t => t.GetAttributeValue<string>("name"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var teamsToAdd = teamsToEnsure
            .Except(currentTeamNames, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var teamName in teamsToAdd)
        {
            await AddUserToTeamAsync(user.Id, teamName);
        }
    }

    private async Task<EntityCollection> RetrieveAvailableRolesAsync(Guid businessUnitId, string[] roleNames)
    {
        var query = new QueryExpression("role")
        {
            ColumnSet = new ColumnSet("roleid", "name"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("businessunitid", ConditionOperator.Equal, businessUnitId),
                    new ConditionExpression("name", ConditionOperator.In, roleNames)
                }
            }
        };

        return await Task.Run(() => _serviceClient.RetrieveMultiple(query));
    }

    private async Task AssignRoleToUserAsync(Guid userId, Guid roleId, string roleName)
    {
        try
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

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Added role: {roleName}");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error adding role {roleName}: {ex.Message}");
            Console.ResetColor();
            throw;
        }
    }

    private async Task AddUserToTeamAsync(Guid userId, string teamName)
    {
        try
        {
            var teamQuery = new QueryExpression("team")
            {
                ColumnSet = new ColumnSet("teamid"),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression("name", ConditionOperator.Equal, teamName)
                    }
                }
            };

            var teams = await Task.Run(() => _serviceClient.RetrieveMultiple(teamQuery));

            if (!teams.Entities.Any())
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Team not found: {teamName}");
                Console.ResetColor();
                return;
            }

            var teamId = teams.Entities[0].Id;
            var addMembersRequest = new AddMembersTeamRequest
            {
                TeamId = teamId,
                MemberIds = new[] { userId }
            };

            await Task.Run(() => _serviceClient.Execute(addMembersRequest));

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Added team: {teamName}");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error adding team {teamName}: {ex.Message}");
            Console.ResetColor();
            throw;
        }
    }
}