using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk;

public class CreateBu
{
    public static async Task<List<BuCreationResult>> RunAsync(List<TransformedTeamData> transformedTeams)
    {
        var results = new List<BuCreationResult>();
        try
        {
            var buManager = new DataverseBusinessUnitManager(SessionManager.Instance.GetClient());

            foreach (var team in transformedTeams)
            {
                try
                {
                    var (exists, wasUpdated) = await buManager.CreateOrUpdateBusinessUnitAsync(team);
                    results.Add(new BuCreationResult { BuName = team.Bu, Exists = exists, WasUpdated = wasUpdated });
                    Console.WriteLine($"Business Unit '{team.Bu}' {(exists ? (wasUpdated ? "updated" : "already exists") : "created")}.\n");
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Error processing Business Unit '{team.Bu}': {ex.Message}");
                    Console.ResetColor();
                    results.Add(new BuCreationResult { BuName = team.Bu, Exists = false, WasUpdated = false });
                }
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error in Business Unit creation process: {ex.Message}");
            Console.ResetColor();
        }

        return results;
    }
}

public class DataverseBusinessUnitManager
{
    private readonly ServiceClient _serviceClient;

    public DataverseBusinessUnitManager(ServiceClient serviceClient)
    {
        _serviceClient = serviceClient;
    }

    public async Task<(bool exists, bool updated)> CreateOrUpdateBusinessUnitAsync(TransformedTeamData team)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("Starting business unit creation/update process for: ");
        Console.ResetColor();
        Console.Write(team.Bu.Trim() + "\n");

        var existingBu = await GetExistingBusinessUnit(team.Bu.Trim());
        if (existingBu != null)
        {
            return await UpdateBusinessUnitIfNeeded(existingBu, team);
        }
        else
        {
            var created = await CreateNewBusinessUnitAsync(team);
            return (created, false);
        }
    }

    private async Task<Entity?> GetExistingBusinessUnit(string businessUnitName)
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

        var result = await Task.Run(() => _serviceClient.RetrieveMultiple(query));
        return result.Entities.Count > 0 ? result.Entities[0] : null;
    }

    private async Task<(bool, bool)> UpdateBusinessUnitIfNeeded(Entity existingBu, TransformedTeamData team)
    {
        bool updated = false;
        var updateEntity = new Entity("businessunit") { Id = existingBu.Id };

        if (existingBu.GetAttributeValue<string>("atos_codigout") != team.Bu)
        {
            updateEntity["atos_codigout"] = team.Bu;
            updated = true;
            Console.WriteLine($"Updating atos_codigout for BU '{team.Bu.Trim()}' from '{existingBu.GetAttributeValue<string>("atos_codigout")}' to '{team.Bu}'");
        }

        var parentBuId = await GetBusinessUnitIdAsync(team.PrimaryCompany);
        if (existingBu.GetAttributeValue<EntityReference>("parentbusinessunitid").Id != parentBuId)
        {
            updateEntity["parentbusinessunitid"] = new EntityReference("businessunit", parentBuId);
            updated = true;
            Console.WriteLine($"Updating parentbusinessunitid for BU '{team.Bu.Trim()}' to '{parentBuId}'");
        }

        var plannerGroupId = await GetPlannerGroupIdAsync(team.PlannerGroup, team.PlannerCenterName);
        if (plannerGroupId.HasValue && existingBu.GetAttributeValue<EntityReference>("atos_grupoplanificadorid")?.Id != plannerGroupId.Value)
        {
            updateEntity["atos_grupoplanificadorid"] = new EntityReference("atos_grupoplanificador", plannerGroupId.Value);
            updated = true;
            Console.WriteLine($"Updating atos_grupoplanificadorid for BU '{team.Bu.Trim()}' to '{plannerGroupId.Value}'");
        }

        var workCenterId = await GetWorkCenterIdAsync(team.ContractorCode, team.PlannerCenterName);
        if (workCenterId.HasValue && existingBu.GetAttributeValue<EntityReference>("atos_puestodetrabajoid")?.Id != workCenterId.Value)
        {
            updateEntity["atos_puestodetrabajoid"] = new EntityReference("atos_puestodetrabajo", workCenterId.Value);
            updated = true;
            Console.WriteLine($"Updating atos_puestodetrabajoid for BU '{team.Bu.Trim()}' to '{workCenterId.Value}'");
        }

        if (updated)
        {
            await Task.Run(() => _serviceClient.Update(updateEntity));
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Business Unit '{team.Bu.Trim()}' updated successfully.");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Business Unit '{team.Bu.Trim()}' is up to date. No changes needed.");
            Console.ResetColor();
        }

        return (true, updated);
    }

    private async Task<bool> CreateNewBusinessUnitAsync(TransformedTeamData team)
    {
        Console.ForegroundColor = ConsoleColor.Blue;
        Console.WriteLine($"Creating new business unit: ");
        Console.ResetColor();
        Console.Write(team.Bu.Trim());

        var parentBuId = await GetBusinessUnitIdAsync(team.PrimaryCompany);
        var plannerGroupId = await GetPlannerGroupIdAsync(team.PlannerGroup, team.PlannerCenterName);
        var workCenterId = await GetWorkCenterIdAsync(team.ContractorCode, team.PlannerCenterName);

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

        var newBuId = await Task.Run(() => _serviceClient.Create(buEntity));

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"New business unit created successfully. BU ID:");
        Console.ResetColor();
        Console.Write(newBuId);

        return true;
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

    private async Task<Guid?> GetPlannerGroupIdAsync(string plannerGroupCode, string planningCenterName)
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

        var result = await Task.Run(() => _serviceClient.RetrieveMultiple(query));

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

    private async Task<Guid?> GetWorkCenterIdAsync(string contractorCode, string planningCenterName)
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

        var result = await Task.Run(() => _serviceClient.RetrieveMultiple(query));

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

public class BuCreationResult
{
    public string BuName { get; set; }
    public bool Exists { get; set; }
    public bool WasUpdated { get; set; }
}