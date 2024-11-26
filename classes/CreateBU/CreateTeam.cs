using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk;

public abstract class BaseTeam
{
    public string TeamName { get; protected set; }
    public string BusinessUnitName { get; protected set; }
    public string AdministratorName { get; protected set; }
    public string[] TeamRoles { get; protected set; }

    public abstract void SetTeamProperties(TransformedTeamData teamData);
}

public class ProprietaryTeam : BaseTeam
{
    public override void SetTeamProperties(TransformedTeamData teamData)
    {
        TeamName = teamData.EquipaEDPR;
        BusinessUnitName = teamData.Bu;
        AdministratorName = CodesAndRoles.AdministratorNameEU;
        TeamRoles = CodesAndRoles.ProprietaryTeamRoles;
    }
}

public class StandardTeam : BaseTeam
{
    public override void SetTeamProperties(TransformedTeamData teamData)
    {
        TeamName = teamData.EquipaContrata;
        BusinessUnitName = teamData.Bu;
        AdministratorName = CodesAndRoles.AdministratorNameEU;
        TeamRoles = CodesAndRoles.TeamRolesEU;
    }
}

public class TeamManager
{
    private readonly ServiceClient _serviceClient;

    public TeamManager()
    {
        _serviceClient = SessionManager.Instance.GetClient();
    }

    public async Task<TeamOperationResult> CreateOrUpdateTeamAsync(BaseTeam team, bool isProprietaryTeam)
    {
        string teamType = isProprietaryTeam ? "Proprietary" : "Standard";
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write($"\nStarting {teamType} team creation/update process for team: ");
        Console.ResetColor();
        Console.Write(team.TeamName.Trim() + "\n");

        var existingTeam = await GetExistingTeamAsync(team.TeamName.Trim());
        if (existingTeam != null)
        {
            bool isCorrectType = (isProprietaryTeam && existingTeam.GetAttributeValue<OptionSetValue>("teamtype").Value == 0) ||
                                 (!isProprietaryTeam && existingTeam.GetAttributeValue<OptionSetValue>("teamtype").Value == 0);

            if (!isCorrectType)
            {
                await DeleteTeamAsync(existingTeam.Id);
                return await CreateNewTeamAsync(team, isProprietaryTeam);
            }
            return await UpdateTeamIfNeededAsync(existingTeam, team, isProprietaryTeam);
        }
        return await CreateNewTeamAsync(team, isProprietaryTeam);
    }

    private async Task<Entity> GetExistingTeamAsync(string teamName)
    {
        var query = new QueryExpression("team")
        {
            ColumnSet = new ColumnSet(true),
            Criteria = new FilterExpression
            {
                Conditions = { new ConditionExpression("name", ConditionOperator.Equal, teamName) }
            }
        };

        var result = await Task.Run(() => _serviceClient.RetrieveMultiple(query));
        return result.Entities.Count > 0 ? result.Entities[0] : null;
    }

    private async Task DeleteTeamAsync(Guid teamId)
    {
        await Task.Run(() => _serviceClient.Delete("team", teamId));
        Console.WriteLine($"Deleted existing team with incorrect type. Team ID: {teamId}");
    }

    private async Task<TeamOperationResult> UpdateTeamIfNeededAsync(Entity existingTeam, BaseTeam team, bool isProprietaryTeam)
    {
        bool updated = false;
        var updateEntity = new Entity("team") { Id = existingTeam.Id };

        var businessUnitId = await GetBusinessUnitIdAsync(team.BusinessUnitName);
        if (existingTeam.GetAttributeValue<EntityReference>("businessunitid").Id != businessUnitId)
        {
            updateEntity["businessunitid"] = new EntityReference("businessunit", businessUnitId);
            updated = true;
            Console.WriteLine($"Updating businessunitid for team '{team.TeamName.Trim()}' to '{businessUnitId}'");
        }

        var administratorId = await GetUserIdAsync(team.AdministratorName);
        if (existingTeam.GetAttributeValue<EntityReference>("administratorid").Id != administratorId)
        {
            updateEntity["administratorid"] = new EntityReference("systemuser", administratorId);
            updated = true;
            Console.WriteLine($"Updating administratorid for team '{team.TeamName.Trim()}' to '{administratorId}'");
        }

        if (updated)
        {
            await Task.Run(() => _serviceClient.Update(updateEntity));
            Console.WriteLine($"Team '{team.TeamName.Trim()}' updated successfully.");
        }

        if (isProprietaryTeam)
        {
            updated |= await UpdateTeamRolesIfNeededAsync(existingTeam.Id, team.TeamRoles, businessUnitId);
            await UpdateBusinessUnitWithProprietaryTeamAsync(businessUnitId, existingTeam.Id, team.TeamName.Trim());
        }
        else
        {
            updated |= await UpdateTeamRolesIfNeededAsync(existingTeam.Id, team.TeamRoles, businessUnitId);
        }

        return new TeamOperationResult { TeamName = team.TeamName, Exists = true, WasUpdated = updated };
    }

    private async Task<TeamOperationResult> CreateNewTeamAsync(BaseTeam team, bool isProprietaryTeam)
    {
        var businessUnitId = await GetBusinessUnitIdAsync(team.BusinessUnitName);
        var administratorId = await GetUserIdAsync(team.AdministratorName);

        var teamEntity = new Entity("team")
        {
            ["name"] = team.TeamName.Trim(),
            ["businessunitid"] = new EntityReference("businessunit", businessUnitId),
            ["administratorid"] = new EntityReference("systemuser", administratorId),
            ["teamtype"] = new OptionSetValue(0)
        };

        var newTeamId = await Task.Run(() => _serviceClient.Create(teamEntity));

        Console.WriteLine($"New team created successfully. Team ID: {newTeamId}");
        Console.WriteLine($"Administrator '{team.AdministratorName}' assigned to team.");

        if (team.TeamRoles?.Length > 0)
        {
            await AssignRolesToTeamAsync(newTeamId, businessUnitId, team.TeamRoles);
        }

        if (isProprietaryTeam)
        {
            await UpdateBusinessUnitWithProprietaryTeamAsync(businessUnitId, newTeamId, team.TeamName.Trim());
        }

        return new TeamOperationResult { TeamName = team.TeamName, Exists = true, WasUpdated = false };
    }

    private async Task UpdateBusinessUnitWithProprietaryTeamAsync(Guid businessUnitId, Guid proprietaryTeamId, string proprietaryTeamName)
    {
        try
        {
            var businessUnitUpdate = new Entity("businessunit", businessUnitId)
            {
                ["atos_equipopropietarioid"] = new EntityReference("team", proprietaryTeamId),
                ["atos_equipopropietarioidname"] = proprietaryTeamName
            };

            await Task.Run(() => _serviceClient.Update(businessUnitUpdate));
            Console.WriteLine($"Business Unit updated with Proprietary Team information.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error updating Business Unit with Proprietary Team: {ex.Message}");
        }
    }

    private async Task<bool> UpdateTeamRolesIfNeededAsync(Guid teamId, string[] desiredRoles, Guid businessUnitId)
    {
        try
        {
            var currentRoles = await GetTeamRolesAsync(teamId);
            var desiredRoleSet = new HashSet<string>(desiredRoles ?? Array.Empty<string>());
            var currentRoleSet = new HashSet<string>(currentRoles.Select(r => r.GetAttributeValue<string>("name")));

            var rolesToAdd = desiredRoleSet.Except(currentRoleSet);
            var rolesToRemove = currentRoleSet.Except(desiredRoleSet);

            bool updated = false;

            // Process roles to add
            foreach (var roleName in rolesToAdd)
            {
                var roleInfo = await GetRoleInfoAsync(roleName, businessUnitId);
                if (roleInfo.HasValue)
                {
                    await AssignRoleToTeamAsync(teamId, roleInfo.Value.roleId, roleInfo.Value.roleName);
                    updated = true;
                }
            }

            // Process roles to remove
            foreach (var roleName in rolesToRemove)
            {
                var roleToRemove = currentRoles.FirstOrDefault(r => r.GetAttributeValue<string>("name") == roleName);
                if (roleToRemove != null)
                {
                    await RemoveRoleFromTeamAsync(teamId, roleToRemove.Id, roleName);
                    updated = true;
                }
            }

            return updated;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Warning: Error updating team roles: {ex.Message}");
            Console.ResetColor();
            return false;
        }
    }

    private async Task AssignRolesToTeamAsync(Guid teamId, Guid businessUnitId, string[] roleNames)
    {
        foreach (var roleName in roleNames)
        {
            var roleInfo = await GetRoleInfoAsync(roleName, businessUnitId);
            if (roleInfo.HasValue)
            {
                await AssignRoleToTeamAsync(teamId, roleInfo.Value.roleId, roleInfo.Value.roleName);
            }
        }
    }

    private async Task<(Guid roleId, string roleName)?> GetRoleInfoAsync(string roleName, Guid businessUnitId)
    {
        var query = new QueryExpression("role")
        {
            ColumnSet = new ColumnSet("roleid", "name"),
            Criteria = new FilterExpression
            {
                Conditions = {
                    new ConditionExpression("name", ConditionOperator.Equal, roleName),
                    new ConditionExpression("businessunitid", ConditionOperator.Equal, businessUnitId)
                }
            }
        };

        var result = await Task.Run(() => _serviceClient.RetrieveMultiple(query));
        if (result.Entities.Count == 0)
        {
            Console.WriteLine($"Role not found: {roleName}");
            return null;
        }

        var role = result.Entities[0];
        return (role.Id, role.GetAttributeValue<string>("name"));
    }

    private async Task AssignRoleToTeamAsync(Guid teamId, Guid roleId, string roleName)
    {
        try
        {
            // Create an associate request to link the team with the role
            var associateRequest = new AssociateRequest
            {
                Target = new EntityReference("team", teamId),
                RelatedEntities = new EntityReferenceCollection
                {
                    new EntityReference("role", roleId)
                },
                // Specify the N:N relationship name between team and role
                Relationship = new Relationship("teamroles_association")
            };

            await Task.Run(() => _serviceClient.Execute(associateRequest));
            Console.WriteLine($"Role '{roleName}' assigned to team successfully.");
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Warning: Error assigning role '{roleName}' to team: {ex.Message}");
            Console.ResetColor();
        }
    }

    private async Task RemoveRoleFromTeamAsync(Guid teamId, Guid roleId, string roleName)
    {
        try
        {
            var disassociateRequest = new DisassociateRequest
            {
                Target = new EntityReference("team", teamId),
                RelatedEntities = new EntityReferenceCollection
                {
                    new EntityReference("role", roleId)
                },
                Relationship = new Relationship("teamroles_association")
            };

            await Task.Run(() => _serviceClient.Execute(disassociateRequest));
            Console.WriteLine($"Role '{roleName}' removed from team.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error removing role '{roleName}' from team: {ex.Message}");
        }
    }

    private async Task<Guid> GetUserIdAsync(string fullName)
    {
        var query = new QueryExpression("systemuser")
        {
            ColumnSet = new ColumnSet("systemuserid"),
            Criteria = new FilterExpression
            {
                Conditions = { new ConditionExpression("fullname", ConditionOperator.Equal, fullName) }
            }
        };

        var result = await Task.Run(() => _serviceClient.RetrieveMultiple(query));
        if (result.Entities.Count == 0)
        {
            throw new Exception($"User not found: {fullName}");
        }

        return result.Entities[0].Id;
    }

    private async Task<List<Entity>> GetTeamRolesAsync(Guid teamId)
    {
        try
        {
            // Query to get all roles associated with the team
            var query = new QueryExpression("role")
            {
                ColumnSet = new ColumnSet("roleid", "name"),
                LinkEntities =
                {
                    new LinkEntity
                    {
                        LinkFromEntityName = "role",
                        LinkToEntityName = "teamroles",
                        LinkFromAttributeName = "roleid",
                        LinkToAttributeName = "roleid",
                        JoinOperator = JoinOperator.Inner,
                        LinkCriteria = new FilterExpression
                        {
                            Conditions =
                            {
                                new ConditionExpression("teamid", ConditionOperator.Equal, teamId)
                            }
                        }
                    }
                }
            };

            var result = await Task.Run(() => _serviceClient.RetrieveMultiple(query));
            return result.Entities.ToList();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Warning: Error retrieving team roles: {ex.Message}");
            Console.ResetColor();
            return new List<Entity>();
        }
    }

    private async Task<Guid> GetBusinessUnitIdAsync(string businessUnitName)
    {
        var query = new QueryExpression("businessunit")
        {
            ColumnSet = new ColumnSet("businessunitid"),
            Criteria = new FilterExpression
            {
                Conditions = { new ConditionExpression("name", ConditionOperator.Equal, businessUnitName) }
            }
        };

        var result = await Task.Run(() => _serviceClient.RetrieveMultiple(query));
        if (result.Entities.Count == 0)
        {
            throw new Exception($"Business Unit not found: {businessUnitName}");
        }

        return result.Entities[0].Id;
    }
}

public class CreateTeam
{
    public static async Task<List<TeamOperationResult>> RunAsync(List<TransformedTeamData> transformedTeams, TeamType teamType)
    {
        var results = new List<TeamOperationResult>();
        try
        {
            var teamManager = new TeamManager();

            foreach (var team in transformedTeams)
            {
                try
                {
                    BaseTeam baseTeam = teamType == TeamType.Proprietary
                        ? new ProprietaryTeam()
                        : new StandardTeam();

                    baseTeam.SetTeamProperties(team);

                    var result = await teamManager.CreateOrUpdateTeamAsync(baseTeam, teamType == TeamType.Proprietary);
                    result.BuName = team.Bu;
                    results.Add(result);

                    Console.WriteLine($"{teamType} Team '{result.TeamName}' {(result.Exists ? (result.WasUpdated ? "updated" : "already exists") : "created")}.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing {teamType} Team for '{team.Bu}': {ex.Message}");
                }

                await Task.Delay(500);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in {teamType} Team creation/update process: {ex.Message}");
        }
        return results;
    }
}

public enum TeamType
{
    Standard,
    Proprietary
}

public class TeamOperationResult
{
    public string TeamName { get; set; }
    public bool Exists { get; set; }
    public bool WasUpdated { get; set; }
    public string BuName { get; set; }
}
