using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk;
using System.Collections.Concurrent;

// Base abstract class for team types
public abstract class BaseTeam
{
    public string TeamName { get; set; } = string.Empty;
    public string BusinessUnitName { get; set; } = string.Empty;
    public string AdministratorName { get; set; } = string.Empty;
    public string[] TeamRoles { get; set; }

    protected BaseTeam()
    {
        // Initialize arrays to empty to avoid null reference issues
        TeamRoles = Array.Empty<string>();
    }

    public abstract void SetTeamProperties(TransformedTeamData teamData);
}

// Concrete implementation for proprietary teams
public sealed class ProprietaryTeam : BaseTeam
{
    public override void SetTeamProperties(TransformedTeamData teamData)
    {
        ArgumentNullException.ThrowIfNull(teamData);

        TeamName = teamData.EquipaEDPR;
        BusinessUnitName = teamData.Bu;
        AdministratorName = CodesAndRoles.AdministratorNameEU;
        TeamRoles = CodesAndRoles.ProprietaryTeamRoles;
    }
}

// Concrete implementation for standard teams
public sealed class StandardTeam : BaseTeam
{
    public override void SetTeamProperties(TransformedTeamData teamData)
    {
        ArgumentNullException.ThrowIfNull(teamData);

        TeamName = teamData.EquipaContrata;
        BusinessUnitName = teamData.Bu;
        AdministratorName = CodesAndRoles.AdministratorNameEU;
        TeamRoles = CodesAndRoles.TeamRolesEU;
    }
}

// Record type for operation results
public sealed record TeamOperationResult
{
    public required string TeamName { get; init; }
    public required bool Exists { get; init; }
    public required bool WasUpdated { get; init; }
    public required string BuName { get; init; }
    public bool Cancelled { get; init; }
}

public sealed class TeamManager
{
    private readonly ServiceClient _serviceClient;

    public TeamManager()
    {
        _serviceClient = SessionManager.Instance.GetClient();
    }

    public async Task<TeamOperationResult> CreateOrUpdateTeamAsync(
        BaseTeam team,
        bool isProprietaryTeam,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(team);

        string teamType = isProprietaryTeam ? "Proprietary" : "Standard";
        Console.ForegroundColor = ConsoleColor.Cyan;
        await Console.Out.WriteAsync($"\nStarting {teamType} team creation/update process for team: ");
        Console.ResetColor();
        await Console.Out.WriteLineAsync(team.TeamName.Trim());

        cancellationToken.ThrowIfCancellationRequested();

        var existingTeam = await GetExistingTeamAsync(team.TeamName.Trim(), cancellationToken);
        if (existingTeam is not null)
        {
            // Check if team type matches
            bool isCorrectType = (isProprietaryTeam && existingTeam.GetAttributeValue<OptionSetValue>("teamtype").Value == 0) ||
                                (!isProprietaryTeam && existingTeam.GetAttributeValue<OptionSetValue>("teamtype").Value == 0);

            if (!isCorrectType)
            {
                await DeleteTeamAsync(existingTeam.Id, cancellationToken);
                return await CreateNewTeamAsync(team, isProprietaryTeam, cancellationToken);
            }
            return await UpdateTeamIfNeededAsync(existingTeam, team, isProprietaryTeam, cancellationToken);
        }
        return await CreateNewTeamAsync(team, isProprietaryTeam, cancellationToken);
    }

    private async Task<Entity?> GetExistingTeamAsync(string teamName, CancellationToken cancellationToken)
    {
        var query = new QueryExpression("team")
        {
            ColumnSet = new ColumnSet(true),
            Criteria = new FilterExpression
            {
                Conditions = { new ConditionExpression("name", ConditionOperator.Equal, teamName) }
            }
        };

        var result = await Task.Run(() => _serviceClient.RetrieveMultiple(query), cancellationToken);
        return result.Entities.FirstOrDefault();
    }

    private async Task DeleteTeamAsync(Guid teamId, CancellationToken cancellationToken)
    {
        await Task.Run(() => _serviceClient.Delete("team", teamId), cancellationToken);
        await Console.Out.WriteLineAsync($"Deleted existing team with incorrect type. Team ID: {teamId}");
    }

    private async Task<TeamOperationResult> UpdateTeamIfNeededAsync(
        Entity existingTeam,
        BaseTeam team,
        bool isProprietaryTeam,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(existingTeam);
        ArgumentNullException.ThrowIfNull(team);

        bool updated = false;
        var updateEntity = new Entity("team") { Id = existingTeam.Id };

        var businessUnitId = await GetBusinessUnitIdAsync(team.BusinessUnitName, cancellationToken);
        if (existingTeam.GetAttributeValue<EntityReference>("businessunitid").Id != businessUnitId)
        {
            updateEntity["businessunitid"] = new EntityReference("businessunit", businessUnitId);
            updated = true;
            await Console.Out.WriteLineAsync($"Updating businessunitid for team '{team.TeamName.Trim()}' to '{businessUnitId}'");
        }

        var administratorId = await GetUserIdAsync(team.AdministratorName, cancellationToken);
        if (existingTeam.GetAttributeValue<EntityReference>("administratorid").Id != administratorId)
        {
            updateEntity["administratorid"] = new EntityReference("systemuser", administratorId);
            updated = true;
            await Console.Out.WriteLineAsync($"Updating administratorid for team '{team.TeamName.Trim()}' to '{administratorId}'");
        }

        if (updated)
        {
            await Task.Run(() => _serviceClient.Update(updateEntity), cancellationToken);
            await Console.Out.WriteLineAsync($"Team '{team.TeamName.Trim()}' updated successfully.");
        }

        if (isProprietaryTeam)
        {
            updated |= await UpdateTeamRolesIfNeededAsync(existingTeam.Id, team.TeamRoles, businessUnitId, cancellationToken);
            await UpdateBusinessUnitWithProprietaryTeamAsync(businessUnitId, existingTeam.Id, team.TeamName.Trim(), cancellationToken);
        }
        else
        {
            updated |= await UpdateTeamRolesIfNeededAsync(existingTeam.Id, team.TeamRoles, businessUnitId, cancellationToken);
        }

        return new TeamOperationResult
        {
            TeamName = team.TeamName,
            Exists = true,
            WasUpdated = updated,
            BuName = team.BusinessUnitName
        };
    }

    private async Task<TeamOperationResult> CreateNewTeamAsync(
        BaseTeam team,
        bool isProprietaryTeam,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(team);

        var businessUnitId = await GetBusinessUnitIdAsync(team.BusinessUnitName, cancellationToken);
        var administratorId = await GetUserIdAsync(team.AdministratorName, cancellationToken);

        var teamEntity = new Entity("team")
        {
            ["name"] = team.TeamName.Trim(),
            ["businessunitid"] = new EntityReference("businessunit", businessUnitId),
            ["administratorid"] = new EntityReference("systemuser", administratorId),
            ["teamtype"] = new OptionSetValue(0)
        };

        var newTeamId = await Task.Run(() => _serviceClient.Create(teamEntity), cancellationToken);

        await Console.Out.WriteLineAsync($"New team created successfully. Team ID: {newTeamId}");
        await Console.Out.WriteLineAsync($"Administrator '{team.AdministratorName}' assigned to team.");

        if (team.TeamRoles?.Length > 0)
        {
            await AssignRolesToTeamAsync(newTeamId, businessUnitId, team.TeamRoles, cancellationToken);
        }

        if (isProprietaryTeam)
        {
            await UpdateBusinessUnitWithProprietaryTeamAsync(businessUnitId, newTeamId, team.TeamName.Trim(), cancellationToken);
        }

        return new TeamOperationResult
        {
            TeamName = team.TeamName,
            Exists = true,
            WasUpdated = false,
            BuName = team.BusinessUnitName
        };
    }

    private async Task UpdateBusinessUnitWithProprietaryTeamAsync(
        Guid businessUnitId,
        Guid proprietaryTeamId,
        string proprietaryTeamName,
        CancellationToken cancellationToken)
    {
        try
        {
            var businessUnitUpdate = new Entity("businessunit", businessUnitId)
            {
                ["atos_equipopropietarioid"] = new EntityReference("team", proprietaryTeamId),
                ["atos_equipopropietarioidname"] = proprietaryTeamName
            };

            await Task.Run(() => _serviceClient.Update(businessUnitUpdate), cancellationToken);
            await Console.Out.WriteLineAsync("Business Unit updated with Proprietary Team information.");
        }
        catch (Exception ex)
        {
            await Console.Out.WriteLineAsync($"Error updating Business Unit with Proprietary Team: {ex.Message}");
        }
    }

    private async Task<bool> UpdateTeamRolesIfNeededAsync(
        Guid teamId,
        string[] desiredRoles,
        Guid businessUnitId,
        CancellationToken cancellationToken)
    {
        try
        {
            var currentRoles = await GetTeamRolesAsync(teamId, cancellationToken);
            var desiredRoleSet = new HashSet<string>(desiredRoles ?? Array.Empty<string>());
            var currentRoleSet = new HashSet<string>(currentRoles.Select(r => r.GetAttributeValue<string>("name")));

            var rolesToAdd = desiredRoleSet.Except(currentRoleSet);
            var rolesToRemove = currentRoleSet.Except(desiredRoleSet);

            bool updated = false;

            foreach (var roleName in rolesToAdd)
            {
                var roleInfo = await GetRoleInfoAsync(roleName, businessUnitId, cancellationToken);
                if (roleInfo.HasValue)
                {
                    await AssignRoleToTeamAsync(teamId, roleInfo.Value.roleId, roleInfo.Value.roleName, cancellationToken);
                    updated = true;
                }
            }

            foreach (var roleName in rolesToRemove)
            {
                var roleToRemove = currentRoles.FirstOrDefault(r => r.GetAttributeValue<string>("name") == roleName);
                if (roleToRemove is not null)
                {
                    await RemoveRoleFromTeamAsync(teamId, roleToRemove.Id, roleName, cancellationToken);
                    updated = true;
                }
            }

            return updated;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            await Console.Out.WriteLineAsync($"Warning: Error updating team roles: {ex.Message}");
            Console.ResetColor();
            return false;
        }
    }

    private async Task AssignRolesToTeamAsync(
        Guid teamId,
        Guid businessUnitId,
        string[] roleNames,
        CancellationToken cancellationToken)
    {
        foreach (var roleName in roleNames)
        {
            var roleInfo = await GetRoleInfoAsync(roleName, businessUnitId, cancellationToken);
            if (roleInfo.HasValue)
            {
                await AssignRoleToTeamAsync(teamId, roleInfo.Value.roleId, roleInfo.Value.roleName, cancellationToken);
            }
        }
    }

    private async Task<(Guid roleId, string roleName)?> GetRoleInfoAsync(
        string roleName,
        Guid businessUnitId,
        CancellationToken cancellationToken)
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

        var result = await Task.Run(() => _serviceClient.RetrieveMultiple(query), cancellationToken);
        if (result.Entities.Count == 0)
        {
            await Console.Out.WriteLineAsync($"Role not found: {roleName}");
            return null;
        }

        var role = result.Entities[0];
        return (role.Id, role.GetAttributeValue<string>("name"));
    }

    private async Task AssignRoleToTeamAsync(
        Guid teamId,
        Guid roleId,
        string roleName,
        CancellationToken cancellationToken)
    {
        try
        {
            var associateRequest = new AssociateRequest
            {
                Target = new EntityReference("team", teamId),
                RelatedEntities = new EntityReferenceCollection
                {
                    new EntityReference("role", roleId)
                },
                Relationship = new Relationship("teamroles_association")
            };

            await Task.Run(() => _serviceClient.Execute(associateRequest), cancellationToken);
            await Console.Out.WriteLineAsync($"Role '{roleName}' assigned to team successfully.");
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            await Console.Out.WriteLineAsync($"Warning: Error assigning role '{roleName}' to team: {ex.Message}");
            Console.ResetColor();
        }
    }

    private async Task RemoveRoleFromTeamAsync(
        Guid teamId,
        Guid roleId,
        string roleName,
        CancellationToken cancellationToken)
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

            await Task.Run(() => _serviceClient.Execute(disassociateRequest), cancellationToken);
            await Console.Out.WriteLineAsync($"Role '{roleName}' removed from team.");
        }
        catch (Exception ex)
        {
            await Console.Out.WriteLineAsync($"Error removing role '{roleName}' from team: {ex.Message}");
        }
    }

    private async Task<List<Entity>> GetTeamRolesAsync(
        Guid teamId,
        CancellationToken cancellationToken)
    {
        try
        {
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

            var result = await Task.Run(() => _serviceClient.RetrieveMultiple(query), cancellationToken);
            return result.Entities.ToList();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            await Console.Out.WriteLineAsync($"Warning: Error retrieving team roles: {ex.Message}");
            Console.ResetColor();
            return new List<Entity>();
        }
    }

    private async Task<Guid> GetBusinessUnitIdAsync(
        string businessUnitName,
        CancellationToken cancellationToken)
    {
        var query = new QueryExpression("businessunit")
        {
            ColumnSet = new ColumnSet("businessunitid"),
            Criteria = new FilterExpression
            {
                Conditions = { new ConditionExpression("name", ConditionOperator.Equal, businessUnitName) }
            }
        };

        var result = await Task.Run(() => _serviceClient.RetrieveMultiple(query), cancellationToken);
        if (result.Entities.Count == 0)
        {
            throw new Exception($"Business Unit not found: {businessUnitName}");
        }

        return result.Entities[0].Id;
    }

    private async Task<Guid> GetUserIdAsync(
        string fullName,
        CancellationToken cancellationToken)
    {
        var query = new QueryExpression("systemuser")
        {
            ColumnSet = new ColumnSet("systemuserid"),
            Criteria = new FilterExpression
            {
                Conditions = { new ConditionExpression("fullname", ConditionOperator.Equal, fullName) }
            }
        };

        var result = await Task.Run(() => _serviceClient.RetrieveMultiple(query), cancellationToken);
        if (result.Entities.Count == 0)
        {
            throw new Exception($"User not found: {fullName}");
        }

        return result.Entities[0].Id;
    }
}

public class CreateTeam
{
    public static async Task<List<TeamOperationResult>> RunAsync(
    List<TransformedTeamData> transformedTeams,
    TeamType teamType)
    {
        using var cts = new CancellationTokenSource();
        var results = new ConcurrentBag<TeamOperationResult>();
        var monitorTask = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Q)
                    {
                        cts.Cancel();
                        Console.WriteLine("\nCancellation requested. Completing current operation...");
                        break;
                    }
                    await Task.Delay(100, cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation, ignore
            }
        });

        try
        {
            var teamManager = new TeamManager();

            await Parallel.ForEachAsync(transformedTeams,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = 3,
                    CancellationToken = cts.Token
                },
                async (team, token) =>
                {
                    try
                    {
                        BaseTeam baseTeam = teamType switch
                        {
                            TeamType.Proprietary => new ProprietaryTeam(),
                            TeamType.Standard => new StandardTeam(),
                            _ => throw new ArgumentException($"Unsupported team type: {teamType}")
                        };

                        baseTeam.SetTeamProperties(team);

                        var result = await teamManager.CreateOrUpdateTeamAsync(baseTeam, teamType == TeamType.Proprietary, token);
                        results.Add(result with { BuName = team.Bu });

                        await Console.Out.WriteLineAsync(
                            $"{teamType} Team '{result.TeamName}' " +
                            $"{(result.Exists ? (result.WasUpdated ? "updated" : "already exists") : "created")}.");
                    }
                    catch (OperationCanceledException)
                    {
                        results.Add(new TeamOperationResult
                        {
                            TeamName = team.Bu,
                            Exists = false,
                            WasUpdated = false,
                            BuName = team.Bu,
                            Cancelled = true
                        });
                        await Console.Out.WriteLineAsync($"Operation cancelled for Team in BU '{team.Bu}'");
                    }
                    catch (Exception ex)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        await Console.Out.WriteLineAsync($"Error processing {teamType} Team for '{team.Bu}': {ex.Message}");
                        Console.ResetColor();
                        results.Add(new TeamOperationResult
                        {
                            TeamName = team.Bu,
                            Exists = false,
                            WasUpdated = false,
                            BuName = team.Bu,
                            Cancelled = false
                        });
                    }
                });
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("\nOperation was cancelled by user.");
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error in {teamType} Team creation/update process: {ex.Message}");
            Console.ResetColor();
        }
        finally
        {
            // Cancel the monitoring task if it hasn't been cancelled already
            if (!cts.IsCancellationRequested)
            {
                cts.Cancel();
            }

            // Wait for the monitoring task to complete
            await monitorTask;
        }

        return results.ToList();
    }
}

public enum TeamType
{
    Standard,
    Proprietary
}