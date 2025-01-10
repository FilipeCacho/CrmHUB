using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using ClosedXML.Excel;
using System.Text.Json;

//this code places a empty cell when the priority value to correct column contains the text "US Only" case insensitive

public class WorkOrderAuditProcessor
{
    private readonly ServiceClient _serviceClient;
    private readonly DateTime _startDate = new(2024, 10, 01);
    private readonly DateTime _endDate = new(2024, 12, 06, 23, 59, 59);

    private class AuditData
    {
        public List<ChangedAttribute> changedAttributes { get; set; } = new();
    }

    private class ChangedAttribute
    {
        public string? logicalName { get; set; }
        public string? oldValue { get; set; }
        public string? newValue { get; set; }
    }

    public WorkOrderAuditProcessor()
    {
        _serviceClient = SessionManager.Instance.GetClient();
    }

    private async Task<string> GetPriorityNameAsync(string priorityGUID)
    {
        if (string.IsNullOrEmpty(priorityGUID)) return string.Empty;

        // Extract the GUID part after "msdyn_priority,"
        string priorityId = priorityGUID.Contains(",")
            ? priorityGUID.Split(',')[1]
            : priorityGUID;

        var fetchXml = $@"
        <fetch>
            <entity name='msdyn_priority'>
                <attribute name='msdyn_name' />
                <filter>
                    <condition attribute='msdyn_priorityid' operator='eq' value='{priorityId}' />
                </filter>
            </entity>
        </fetch>";

        try
        {
            var result = await Task.Run(() => _serviceClient.RetrieveMultiple(new FetchExpression(fetchXml)));
            if (result.Entities.Any())
            {
                return result.Entities[0].GetAttributeValue<string>("msdyn_name");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error retrieving priority name for ID {priorityId}: {ex.Message}");
        }

        return priorityId;
    }

    public async Task RunAsync()
    {
        try
        {
            Console.WriteLine("Starting Work Order audit analysis...");

            var workorders = await FetchWorkordersAsync();
            Console.WriteLine($"Found {workorders.Count} workorders");

            if (!workorders.Any())
            {
                Console.WriteLine("No workorders found for the specific query.");
                return;
            }

            var auditMasterList = new List<WorkOrderAuditResults>();
            foreach (var workOrder in workorders)
            {
                var workOrderId = workOrder.Id;
                var workorderName = workOrder.GetAttributeValue<string>("msdyn_name");

                Console.WriteLine($"\nProcessing Workorder: {workorderName}");
                Console.WriteLine($"Workorder Id: {workOrderId}");

                // Try primary query first
                var (auditRecords, primaryCount) = await RetrieveAuditRecordsAsync(workOrderId);
                Console.WriteLine($"Found {primaryCount} audit records in primary query");

                var auditEntry = new WorkOrderAuditResults
                {
                    WorkOrderName = workorderName,
                    PrimaryQueryCount = primaryCount,
                    BackupQueryCount = 0
                };

                // Process primary query results if they exist
                if (primaryCount > 0 && auditRecords.Any())
                {
                    // Find the first record that doesn't have "US Only" in its priority (value to correct)
                    Entity selectedRecord = null;
                    foreach (var record in auditRecords)
                    {
                        var priorityOld = await GetPriorityValueToCorrect(record);
                        if (!priorityOld.ToUpperInvariant().Contains("US ONLY"))
                        {
                            selectedRecord = record;
                            break;
                        }
                    }

                    // If no record found without "US Only" then pick the first most recent record
                    selectedRecord ??= auditRecords[0];
                    await ProcessAuditRecord(selectedRecord, auditEntry);
                }
                else // No primary results, try backup query
                {
                    var (backupRecords, backupCount) = await RetrieveBackupAuditRecordsAsync(workOrderId, workorderName);
                    Console.WriteLine($"Found {backupCount} audit records in backup query");

                    auditEntry.BackupQueryCount = backupCount;
                    auditEntry.UsedBackupQuery = true;

                    if (backupRecords.Any())
                    {
                        Entity selectedRecord = null;
                        foreach (var record in backupRecords)
                        {
                            var priorityOld = await GetPriorityValueToCorrect(record);
                            if (!priorityOld.Contains("US Only"))
                            {
                                selectedRecord = record;
                                break;
                            }
                        }

                        selectedRecord ??= backupRecords[0];
                        await ProcessAuditRecord(selectedRecord, auditEntry);
                    }
                }

                auditMasterList.Add(auditEntry);
            }

            await CreateExcelFileAsync(auditMasterList);

            Console.WriteLine("\nProcess completed!");
            Console.WriteLine("\nPress any key to continue...");
            Console.ReadKey();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            Console.ResetColor();
            Console.WriteLine("\nPress any key to continue...");
            Console.ReadKey();
        }
    }

    private async Task<string> GetPriorityValueToCorrect(Entity auditRecord)
    {
        var jsonData = auditRecord.GetAttributeValue<string>("changedata");

        if (string.IsNullOrEmpty(jsonData)) return string.Empty;

        try
        {
            var auditData = JsonSerializer.Deserialize<AuditData>(jsonData);
            if (auditData?.changedAttributes != null)
            {
                var priorityChange = auditData.changedAttributes.FirstOrDefault(x => x.logicalName == "msdyn_priority");

                if (priorityChange != null)
                {
                    return await GetPriorityNameAsync(priorityChange.oldValue);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error parsing JSON: {ex.Message}");
        }

        return string.Empty;
    }

    private async Task ProcessAuditRecord(Entity auditRecord, WorkOrderAuditResults auditEntry)
    {
        var jsonData = auditRecord.GetAttributeValue<string>("changedata");
        Console.WriteLine("Raw JSON Data:");
        Console.WriteLine(jsonData);

        if (!string.IsNullOrEmpty(jsonData))
        {
            try
            {
                var auditData = JsonSerializer.Deserialize<AuditData>(jsonData);
                if (auditData?.changedAttributes != null)
                {
                    foreach (var changeddataInfo in auditData.changedAttributes)
                    {
                        switch (changeddataInfo.logicalName)
                        {
                            case "msdyn_priority":
                                auditEntry.PriorityOld = await GetPriorityNameAsync(changeddataInfo.oldValue);
                                auditEntry.PriorityNew = await GetPriorityNameAsync(changeddataInfo.newValue);
                                break;
                            case "atos_estdinstal":
                                auditEntry.EstdInstalOld = changeddataInfo.oldValue;
                                auditEntry.EstdInstalNew = changeddataInfo.newValue;
                                break;
                            case "atos_fechafin":
                                auditEntry.FechaFinOld = changeddataInfo.oldValue;
                                auditEntry.FechaFinNew = changeddataInfo.newValue;
                                break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error while parsing JSON for {auditEntry.WorkOrderName}: {ex.Message}");
                Console.WriteLine($"JSON content: {jsonData}");
            }
        }
    }

    private async Task<List<Entity>> FetchWorkordersAsync()
    {
        var fetchXml = $@"
        <fetch>
            <entity name='msdyn_workorder'>
                <attribute name='msdyn_workorderid' />
                <attribute name='msdyn_name' />
                <attribute name='msdyn_serviceaccount' />
                <attribute name='msdyn_priority' />
                <attribute name='atos_estdinstal' />
                <attribute name='atos_fechafin' />
                <attribute name='createdon' />
                <filter>
                    <condition attribute='createdon' operator='ge' value='2024-09-01T00:00:00+00:00' />
                    <condition attribute='createdon' operator='le' value='2024-12-06T23:59:59+00:00' />
                    <condition attribute='msdyn_serviceaccountname' operator='not-like' value='0-US%' />
                    <condition attribute='msdyn_serviceaccountname' operator='not-null' />
                    <condition attribute='msdyn_serviceaccountname' operator='not-like' value='0-MX%' />
                    <condition attribute='msdyn_serviceaccountname' operator='not-null' />
                    <condition attribute='msdyn_serviceaccountname' operator='not-like' value='0-CA%' />
                    <condition attribute='msdyn_serviceaccountname' operator='not-null' />
                    <condition attribute='msdyn_priorityname' operator='like' value='%US Only%' />
                </filter>
            </entity>
        </fetch>";

        var queryResults = await Task.Run(() => _serviceClient.RetrieveMultiple(new FetchExpression(fetchXml)));
        return queryResults.Entities.ToList();
    }

    private async Task<(List<Entity> Records, int TotalCount)> RetrieveAuditRecordsAsync(Guid workOrderId)
    {
        // Get all records to count them, but only retrieve basic attributes
        var countFetchXml = $@"
        <fetch>
            <entity name='audit'>
                <attribute name='createdon' />
                <filter>
                    <condition attribute='objecttypecode' operator='eq' value='10122' />
                    <condition attribute='operation' operator='eq' value='2' />
                    <condition attribute='objectid' operator='eq' value='{workOrderId}' />
                    <condition attribute='createdon' operator='ge' value='2024-10-01T00:00:00+00:00' />
                    <condition attribute='createdon' operator='le' value='2024-12-06T23:59:59+00:00' />
                    <condition attribute='attributemask' operator='like' value='%75%' />
                    <condition attribute='attributemask' operator='like' value='%161%' />
                    <condition attribute='attributemask' operator='like' value='%168%' />
                </filter>
            </entity>
        </fetch>";

        var countResult = await Task.Run(() => _serviceClient.RetrieveMultiple(new FetchExpression(countFetchXml)));
        var totalCount = countResult.Entities.Count;

        List<Entity> records = new();
        if (totalCount > 0)
        {
            var fetchXml = $@"
            <fetch>
                <entity name='audit'>
                    <all-attributes />
                    <filter>
                        <condition attribute='objecttypecode' operator='eq' value='10122' />
                        <condition attribute='operation' operator='eq' value='2' />
                        <condition attribute='objectid' operator='eq' value='{workOrderId}' />
                        <condition attribute='createdon' operator='ge' value='2024-10-01T00:00:00+00:00' />
                        <condition attribute='createdon' operator='le' value='2024-12-06T23:59:59+00:00' />
                        <condition attribute='attributemask' operator='like' value='%75%' />
                        <condition attribute='attributemask' operator='like' value='%161%' />
                        <condition attribute='attributemask' operator='like' value='%168%' />
                    </filter>
                    <order attribute='createdon' descending='true' />
                </entity>
            </fetch>";

            var auditResults = await Task.Run(() => _serviceClient.RetrieveMultiple(new FetchExpression(fetchXml)));
            records = auditResults.Entities.ToList();
        }

        return (records, totalCount);
    }

    private async Task<(List<Entity> Records, int TotalCount)> RetrieveBackupAuditRecordsAsync(Guid workOrderId, string workorderName)
    {
        // Get all records with relaxed conditions to count them
        var countFetchXml = $@"
        <fetch>
            <entity name='audit'>
                <attribute name='createdon' />
                <filter>
                    <condition attribute='objectid' operator='eq' value='{workOrderId}' />
                    <condition attribute='objecttypecode' operator='eq' value='10122' />
                    <condition attribute='attributemask' operator='like' value='%75%' />
                </filter>
            </entity>
        </fetch>";

        var countResult = await Task.Run(() => _serviceClient.RetrieveMultiple(new FetchExpression(countFetchXml)));
        var totalCount = countResult.Entities.Count;

        List<Entity> records = new();
        if (totalCount > 0)
        {
            var fetchXml = $@"
            <fetch>
                <entity name='audit'>
                    <all-attributes />
                    <filter>
                        <condition attribute='objectid' operator='eq' value='{workOrderId}' />
                        <condition attribute='objecttypecode' operator='eq' value='10122' />
                        <condition attribute='attributemask' operator='like' value='%75%' />
                    </filter>
                    <order attribute='createdon' descending='true' />
                </entity>
            </fetch>";

            var auditResults = await Task.Run(() => _serviceClient.RetrieveMultiple(new FetchExpression(fetchXml)));
            records = auditResults.Entities.ToList();
        }

        return (records, totalCount);
    }

    private string MapInstallValues(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";

        return value switch
        {
            "1" => "En funcionamiento",
            "2" => "Limitado",
            "3" => "Fuera de servicio",
            _ => value
        };
    }

    private async Task CreateExcelFileAsync(List<WorkOrderAuditResults> auditResults)
    {
        string downloadsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads",
            "generated excels");
        Directory.CreateDirectory(downloadsFolder);

        string filePathLocation = Path.Combine(downloadsFolder, $"WorkOrderAudit_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");

        await Task.Run(() =>
        {
            using var excelfile = new XLWorkbook();
            var sheet = excelfile.Worksheets.Add("Audit Results");

            // headers
            sheet.Cell(1, 1).Value = "Workorder";
            sheet.Cell(1, 2).Value = "Priority (Value to Correct)";
            sheet.Cell(1, 3).Value = "Priority (Present Value)";
            sheet.Cell(1, 4).Value = "EstdInstal (Value to Correct)";
            sheet.Cell(1, 5).Value = "EstdInstal (Present Value)";
            sheet.Cell(1, 6).Value = "FechaFin (Value to Correct)";
            sheet.Cell(1, 7).Value = "FechaFin (Present Value)";

            // header styles
            var headerRange = sheet.Range(1, 1, 1, 7);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;

            // color codes
            var highlightYellow = XLColor.FromHtml("#FFEB9C");
            var highlightBlue = XLColor.FromHtml("#DCE6F1");

            // add data to excel and applies formatting
            for (int i = 0; i < auditResults.Count; i++)
            {
                var row = i + 2;
                var result = auditResults[i];
                var range = sheet.Range(row, 1, row, 7);

                // force write these values
                sheet.Cell(row, 1).Value = result.WorkOrderName ?? "";

                // Process data for all cases where we have an audit record
                if ((result.PrimaryQueryCount >= 1) || (result.PrimaryQueryCount == 0 && result.BackupQueryCount >= 1))
                {
                    //remove content of any cells in the column PriorityOld contains "US ONLY" case insensitive
                    string priorityOldValue = (result.PriorityOld ?? "").ToUpperInvariant().Contains("US ONLY") ? "" : (result.PriorityOld ?? "");

                    sheet.Cell(row, 2).Value = priorityOldValue;
                    sheet.Cell(row, 3).Value = result.PriorityNew ?? "";
                    sheet.Cell(row, 4).Value = MapInstallValues(result.EstdInstalOld);
                    sheet.Cell(row, 5).Value = MapInstallValues(result.EstdInstalNew);
                    sheet.Cell(row, 6).Value = result.FechaFinOld ?? "";
                    sheet.Cell(row, 7).Value = result.FechaFinNew ?? "";
                }

                // set colors based on conditions
                if (result.PrimaryQueryCount == 0)
                {
                    if (result.BackupQueryCount >= 1)
                    {
                        range.Style.Fill.BackgroundColor = highlightBlue;  // Results from backup query
                    }
                    else
                    {
                        range.Style.Fill.BackgroundColor = highlightYellow;  // No results from either query
                    }
                }

                // set cell borders
                range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                range.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            }

            // fit columns
            sheet.Columns().AdjustToContents();

            // freeze headers
            sheet.SheetView.FreezeRows(1);

            // saves to disk
            excelfile.SaveAs(filePathLocation);

            Console.WriteLine($"\nProcessed {auditResults.Count} records");
            foreach (var result in auditResults)
            {
                Console.WriteLine($"Work Order: {result.WorkOrderName}");
                if ((result.PrimaryQueryCount >= 1) || (result.PrimaryQueryCount == 0 && result.BackupQueryCount == 1))
                {
                    Console.WriteLine($"Priority: {result.PriorityOld} -> {result.PriorityNew}");
                    Console.WriteLine($"EstdInstal: {MapInstallValues(result.EstdInstalOld)} -> {MapInstallValues(result.EstdInstalNew)}");
                    Console.WriteLine($"FechaFin: {result.FechaFinOld} -> {result.FechaFinNew}");
                }
            }
        });

        Console.WriteLine($"\nExcel file created at: {filePathLocation}");
    }
}

public class WorkOrderAuditResults
{
    public string? WorkOrderName { get; set; }
    public int PrimaryQueryCount { get; set; }  // Count from primary query
    public int BackupQueryCount { get; set; }   // Count from backup query
    public string? PriorityOld { get; set; }
    public string? PriorityNew { get; set; }
    public string? EstdInstalOld { get; set; }
    public string? EstdInstalNew { get; set; }
    public string? FechaFinOld { get; set; }
    public string? FechaFinNew { get; set; }
    public bool UsedBackupQuery { get; set; } = false;
}