using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk;
using System.Collections.Concurrent;

public sealed class CreateBu
{
    private static bool _warningDisplayed;

    // Record to hold the creation result with modern C# record type
    public sealed record BuCreationResult
    {
        public required string BuName { get; init; }
        public required bool Exists { get; init; }
        public required bool WasUpdated { get; init; }
        public bool Cancelled { get; init; }
    }

    public static async Task<List<BuCreationResult>> RunAsync(List<TransformedTeamData> transformedTeams)
    {
        if (!_warningDisplayed)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;

            Console.WriteLine("\nNote: Cancellation will complete the current BU operation before stopping.");
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("Press any key to start the BU creation process. Press 'q' at any time to cancel.");
            Console.ResetColor();
            Console.ReadKey(true);
            _warningDisplayed = true;
            Console.Clear();
        }

        var results = new ConcurrentBag<BuCreationResult>();
        using var cts = new CancellationTokenSource();

        // Start key monitoring task and store its reference
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
                    await Task.Delay(100, cts.Token); // Use Task.Delay instead of Thread.Sleep
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation, ignore
            }
        });

        try
        {
            var serviceClient = SessionManager.Instance.GetClient();
            var buManager = new DataverseBusinessUnitManager(serviceClient);

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
                        var (exists, wasUpdated) = await buManager.CreateOrUpdateBusinessUnitAsync(team, token);
                        results.Add(new BuCreationResult
                        {
                            BuName = team.Bu,
                            Exists = exists,
                            WasUpdated = wasUpdated,
                            Cancelled = false
                        });

                        await Console.Out.WriteLineAsync(
                            $"Business Unit '{team.Bu}' {(exists ? (wasUpdated ? "updated" : "already exists") : "created")}.\n");
                    }
                    catch (OperationCanceledException)
                    {
                        results.Add(new BuCreationResult
                        {
                            BuName = team.Bu,
                            Exists = false,
                            WasUpdated = false,
                            Cancelled = true
                        });
                        await Console.Out.WriteLineAsync($"Operation cancelled for Business Unit '{team.Bu}'");
                    }
                    catch (Exception ex)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        await Console.Out.WriteLineAsync($"Error processing Business Unit '{team.Bu}': {ex.Message}");
                        Console.ResetColor();
                        results.Add(new BuCreationResult
                        {
                            BuName = team.Bu,
                            Exists = false,
                            WasUpdated = false,
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
            Console.WriteLine($"Error in Business Unit creation process: {ex.Message}");
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

public sealed class DataverseBusinessUnitManager
{
    private readonly ServiceClient _serviceClient;

    public DataverseBusinessUnitManager(ServiceClient serviceClient)
    {
        _serviceClient = serviceClient ?? throw new ArgumentNullException(nameof(serviceClient));
    }

    public async Task<(bool exists, bool updated)> CreateOrUpdateBusinessUnitAsync(
        TransformedTeamData team,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(team);

        Console.ForegroundColor = ConsoleColor.Cyan;
        await Console.Out.WriteAsync("Starting business unit creation/update process for: ");
        Console.ResetColor();
        await Console.Out.WriteLineAsync(team.Bu.Trim());

        cancellationToken.ThrowIfCancellationRequested();

        var existingBu = await GetExistingBusinessUnit(team.Bu.Trim(), cancellationToken);

        return existingBu is not null
            ? await UpdateBusinessUnitIfNeeded(existingBu, team, cancellationToken)
            : (await CreateNewBusinessUnitAsync(team, cancellationToken), false);
    }

    private async Task<Entity?> GetExistingBusinessUnit(string businessUnitName, CancellationToken cancellationToken)
    {
        var query = new QueryExpression("businessunit")
        {
            ColumnSet = new ColumnSet(true),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("name", ConditionOperator.Equal, businessUnitName)
                }
            }
        };

        var result = await Task.Run(() => _serviceClient.RetrieveMultiple(query), cancellationToken);
        return result.Entities.FirstOrDefault();
    }

    private async Task<(bool exists, bool updated)> UpdateBusinessUnitIfNeeded(
        Entity existingBu,
        TransformedTeamData team,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(existingBu);
        ArgumentNullException.ThrowIfNull(team);

        var updateEntity = new Entity("businessunit") { Id = existingBu.Id };
        var updated = false;

        // Fix the null handling for existingCode
        var existingCode = existingBu.GetAttributeValue<string>("atos_codigout");
        if (existingCode != team.Bu)
        {
            updateEntity["atos_codigout"] = team.Bu;
            updated = true;
            await Console.Out.WriteLineAsync(
                $"Updating atos_codigout for BU '{team.Bu.Trim()}' from '{existingCode ?? "null"}' to '{team.Bu}'");
        }

        var parentBuId = await GetBusinessUnitIdAsync(team.PrimaryCompany, cancellationToken);
        if (existingBu.GetAttributeValue<EntityReference>("parentbusinessunitid")?.Id != parentBuId)
        {
            updateEntity["parentbusinessunitid"] = new EntityReference("businessunit", parentBuId);
            updated = true;
        }

        var plannerGroupId = await GetPlannerGroupIdAsync(team.PlannerGroup, team.PlannerCenterName, cancellationToken);
        if (plannerGroupId.HasValue &&
            existingBu.GetAttributeValue<EntityReference>("atos_grupoplanificadorid")?.Id != plannerGroupId.Value)
        {
            updateEntity["atos_grupoplanificadorid"] = new EntityReference("atos_grupoplanificador", plannerGroupId.Value);
            updated = true;
        }

        var workCenterId = await GetWorkCenterIdAsync(team.ContractorCode, team.PlannerCenterName, cancellationToken);
        if (workCenterId.HasValue &&
            existingBu.GetAttributeValue<EntityReference>("atos_puestodetrabajoid")?.Id != workCenterId.Value)
        {
            updateEntity["atos_puestodetrabajoid"] = new EntityReference("atos_puestodetrabajo", workCenterId.Value);
            updated = true;
        }

        if (updated)
        {
            await Task.Run(() => _serviceClient.Update(updateEntity), cancellationToken);
            Console.ForegroundColor = ConsoleColor.Green;
            await Console.Out.WriteLineAsync($"Business Unit '{team.Bu.Trim()}' updated successfully.");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Green;
            await Console.Out.WriteLineAsync($"Business Unit '{team.Bu.Trim()}' is up to date. No changes needed.");
            Console.ResetColor();
        }

        return (true, updated);
    }

    private async Task<bool> CreateNewBusinessUnitAsync(TransformedTeamData team, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(team);

        Console.ForegroundColor = ConsoleColor.Blue;
        await Console.Out.WriteLineAsync($"Creating new business unit: {team.Bu.Trim()}");
        Console.ResetColor();

        var parentBuId = await GetBusinessUnitIdAsync(team.PrimaryCompany, cancellationToken);
        var plannerGroupId = await GetPlannerGroupIdAsync(team.PlannerGroup, team.PlannerCenterName, cancellationToken);
        var workCenterId = await GetWorkCenterIdAsync(team.ContractorCode, team.PlannerCenterName, cancellationToken);

        if (!plannerGroupId.HasValue || !workCenterId.HasValue)
        {
            throw new Exception("Creation halted due to missing Planner Group or Work Center data");
        }

        var buEntity = new Entity("businessunit")
        {
            ["name"] = team.Bu.Trim(),
            ["atos_codigout"] = team.Bu,
            ["parentbusinessunitid"] = new EntityReference("businessunit", parentBuId),
            ["atos_grupoplanificadorid"] = new EntityReference("atos_grupoplanificador", plannerGroupId.Value),
            ["atos_puestodetrabajoid"] = new EntityReference("atos_puestodetrabajo", workCenterId.Value)
        };

        var newBuId = await Task.Run(() => _serviceClient.Create(buEntity), cancellationToken);

        Console.ForegroundColor = ConsoleColor.Green;
        await Console.Out.WriteLineAsync($"New business unit created successfully. BU ID: {newBuId}");
        Console.ResetColor();

        return true;
    }

    private async Task<Guid> GetBusinessUnitIdAsync(string businessUnitName, CancellationToken cancellationToken)
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

    private async Task<Guid?> GetPlannerGroupIdAsync(string plannerGroupCode, string planningCenterName, CancellationToken cancellationToken)
    {
        var query = new QueryExpression("atos_grupoplanificador")
        {
            ColumnSet = new ColumnSet("atos_grupoplanificadorid", "atos_codigo", "atos_name"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("atos_codigo", ConditionOperator.Equal, plannerGroupCode)
                }
            },
            LinkEntities =
            {
                new LinkEntity
                {
                    LinkFromEntityName = "atos_grupoplanificador",
                    LinkToEntityName = "atos_centrodeplanificacion",
                    LinkFromAttributeName = "atos_centroplanificacionid",
                    LinkToAttributeName = "atos_centrodeplanificacionid",
                    JoinOperator = JoinOperator.Inner,
                    Columns = new ColumnSet("atos_name"),
                    EntityAlias = "pc",
                    LinkCriteria = new FilterExpression
                    {
                        Conditions =
                        {
                            new ConditionExpression("atos_name", ConditionOperator.Like, $"%{planningCenterName}%")
                        }
                    }
                }
            }
        };

        var result = await Task.Run(() => _serviceClient.RetrieveMultiple(query), cancellationToken);

        if (result.Entities.Count == 0)
        {
            Console.WriteLine($"No Planner Group found for Code: {plannerGroupCode}, Planning Center: {planningCenterName}");
            return null;
        }
        if (result.Entities.Count > 1)
        {
            Console.WriteLine($"Multiple Planner Groups found for Code: {plannerGroupCode}, Planning Center: {planningCenterName}");
            return null;
        }

        return result.Entities[0].Id;
    }

    private async Task<Guid?> GetWorkCenterIdAsync(string contractorCode, string planningCenterName, CancellationToken cancellationToken)
    {
        var query = new QueryExpression("atos_puestodetrabajo")
        {
            ColumnSet = new ColumnSet("atos_puestodetrabajoid", "atos_codigopuestodetrabajo", "atos_name"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("atos_codigopuestodetrabajo", ConditionOperator.Equal, contractorCode)
                }
            },
            LinkEntities =
            {
                new LinkEntity
                {
                    LinkFromEntityName = "atos_puestodetrabajo",
                    LinkToEntityName = "atos_centrodeemplazamiento",
                    LinkFromAttributeName = "atos_centroemplazamientoid",
                    LinkToAttributeName = "atos_centrodeemplazamientoid",
                    JoinOperator = JoinOperator.Inner,
                    Columns = new ColumnSet("atos_name"),
                    EntityAlias = "ce",
                    LinkCriteria = new FilterExpression
                    {
                        Conditions =
                        {
                            new ConditionExpression("atos_name", ConditionOperator.Like, $"%{planningCenterName}%")
                        }
                    }
                }
            }
        };

        var result = await Task.Run(() => _serviceClient.RetrieveMultiple(query), cancellationToken);

        if (result.Entities.Count == 0)
        {
            Console.WriteLine($"No Work Center found for Contractor Code: {contractorCode}, Planning Center: {planningCenterName}");
            return null;
        }
        if (result.Entities.Count > 1)
        {
            Console.WriteLine($"Multiple Work Centers found for Contractor Code: {contractorCode}, Planning Center: {planningCenterName}");
            return null;
        }

        return result.Entities[0].Id;
    }
}