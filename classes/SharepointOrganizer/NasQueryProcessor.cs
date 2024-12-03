using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public class NasQueryProcessor
{
    private readonly ServiceClient _serviceClient;
    private List<WorkOrderData> _workOrders;
    private const string NasDownloadTable = "https://edprdesarrollo1.crm4.dynamics.com/main.aspx?appid=f4e561c0-b105-4505-9092-3dc5846562d0&pagetype=entitylist&etn=edprdyn_massivedownloadfromnas&viewid=28310f9b-76e0-4336-ac10-7553c5ab7a84&viewType=1039";
    private const string NasFilesDownloader = "https://make.powerautomate.com/environments/e17eb108-0681-48cc-b70f-570efcb3e18b/flows/791ed936-2f7c-470a-b9df-89284aa94a68/details\r\n";
    private const string NasFileChecker = "https://make.powerautomate.com/environments/e17eb108-0681-48cc-b70f-570efcb3e18b/solutions/fd140aaf-4df4-11dd-bd17-0019b9312238/flows/eb15d61c-982a-4e38-852a-2bd47ff05ac8/details?v3=false\r\n";
    private const string SharePointUrl = "https://edponcloud.sharepoint.com/teams/O365_MobilityNoesisCGI/Shared%20Documents/Forms/AllItems.aspx?newTargetListUrl=%2Fteams%2FO365%5FMobilityNoesisCGI%2FShared%20Documents&viewpath=%2Fteams%2FO365%5FMobilityNoesisCGI%2FShared%20Documents%2FForms%2FAllItems%2Easpx&id=%2Fteams%2FO365%5FMobilityNoesisCGI%2FShared%20Documents%2FVentient%20Shared%20Folder%20%5BAttachments%5D&viewid=0aa21647%2D1724%2D4fa7%2Db69b%2D2334919b1b09&OR=Teams%2DHL&CT=1732802956597&clickparams=eyJBcHBOYW1lIjoiVGVhbXMtRGVza3RvcCIsIkFwcFZlcnNpb24iOiI0OS8yNDEwMjAwMTMxOCIsIkhhc0ZlZGVyYXRlZFVzZXIiOmZhbHNlfQ%3D%3D\r\n";

    public NasQueryProcessor()
    {
        _serviceClient = SessionManager.Instance.GetClient();
        _workOrders = new List<WorkOrderData>();
    }

    public class WorkOrderData
    {
        public string WorkOrderName { get; set; }
        public string ServiceAccountName { get; set; }
        public string NoteText { get; set; }
        public string ProcessedServiceAccountName { get; set; }
        public string ProcessedNoteText { get; set; }
        public string ObjectIdType { get; set; }
    }

    private void DisplayInitialInstructions()
    {
        Console.Clear();
        Console.WriteLine("=== NAS Download Preparation Tool ===\n");
        Console.WriteLine("This tool will help you prepare for extracting files from the NAS.");
        Console.WriteLine("\nRequired Steps:");
        Console.WriteLine("1. Ensure you have updated the dataCenter Excel file's 'NAS Download' worksheet with level 3 functional locations");
        Console.WriteLine("2. This tool will extract and prepare the NAS links");
        Console.WriteLine("3. After completion, run the Power Automate flows to transfer files\n");

        Console.WriteLine("Useful Resources (you can CTRL + Left click to open them):");

        Console.WriteLine("\nThis is the table here the NAS links need to be registred (this tool does this step for you)");
        Console.WriteLine("we only need to run the 2 following power automates after running this tool");

        // Documentation Link
        Console.ForegroundColor = ConsoleColor.Blue;
        Console.Write("\x1b]8;;" + NasDownloadTable + "\x1b\\");
        Console.Write("NAS Download Table");
        Console.Write("\x1b]8;;\x1b\\\n");


        // First Power Automate Flow
        Console.ResetColor();
        Console.WriteLine("\nAfter extracting the links with this tool, use the following Power Automate to get the files to the Sharepoint:");
        Console.ForegroundColor = ConsoleColor.Blue;
        Console.Write("\x1b]8;;" + NasFilesDownloader + "\x1b\\");
        Console.Write("NAS files downloader flow");
        Console.Write("\x1b]8;;\x1b\\\n");

        Console.ResetColor();
        Console.WriteLine("\nAfter the first power automate extracts the files to the Sharepoint run this 2nd Power Automate ");
        Console.WriteLine("to make sure all files are retrieved:");
        Console.ForegroundColor = ConsoleColor.Blue;
        // Second Power Automate Flow
        Console.Write("\x1b]8;;" + NasFileChecker + "\x1b\\");
        Console.Write("Nas links files verification Flow");
        Console.Write("\x1b]8;;\x1b\\\n");


        Console.ResetColor();
        Console.WriteLine("\nFinally make sure all files are in the Sharepoint:");
        // Sharepoint link
        Console.ForegroundColor = ConsoleColor.Blue;
        Console.Write("\x1b]8;;" + SharePointUrl + "\x1b\\");
        Console.Write("Sharepoint URL");
        Console.Write("\x1b]8;;\x1b\\\n");

        Console.ResetColor();
        Console.WriteLine("\nNote: You can run this tool again at any time to check the links.");
        Console.WriteLine("\nDo you want to proceed with the extraction?");
    }

    private bool GetUserConfirmation()
    {
        while (true)
        {
            Console.Write("\nEnter (y/n): ");
            string? input = Console.ReadLine()?.Trim().ToLower();

            if (input == "y")
                return true;
            if (input == "n")
                return false;

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Invalid input. Please enter 'y' for yes or 'n' for no.");
            Console.ResetColor();
        }
    }

    public async Task<List<string>> ProcessNasDownloadsAsync()
    {
        try
        {
            DisplayInitialInstructions();

            if (!GetUserConfirmation())
            {
                Console.WriteLine("Operation cancelled. Returning to main menu...");
                return new List<string>();
            }

            Console.Clear();
            var locations = ExcelReader.ValidateNasDownloads();
            if (locations == null || !locations.Any())
            {
                Console.WriteLine("No valid locations found in Excel.");
                Console.WriteLine("\nPress any key to continue...");
                Console.ReadKey();
                return new List<string>();
            }

            Console.WriteLine($"Found {locations.Count} locations in Excel.");
            Console.WriteLine("\nPress any key to start processing...");
            Console.ReadKey();
            Console.Clear();

            await RetrieveDataFromDynamics(locations);
            ProcessData();
            await DisplayResultsAsync();

            return _workOrders.Select(wo => wo.NoteText).ToList();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error during NAS download processing: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            Console.ResetColor();
            Console.WriteLine("\nPress any key to continue...");
            Console.ReadKey();
            return new List<string>();
        }
    }

    private async Task RetrieveDataFromDynamics(List<string> locations)
    {
        Console.ResetColor();

        var conditions = string.Join("", locations.Select(loc =>
            $"<condition attribute='msdyn_serviceaccountname' entityname='wo' operator='like' value='%{loc}%' />"));

        var fetchXml = $@"<fetch xmlns:generator='MarkMpn.SQL4CDS'>
          <entity name='annotation'>
            <attribute name='notetext' />
            <attribute name='objectid' />
            <link-entity name='msdyn_workorder' to='objectid' from='msdyn_workorderid' alias='wo' link-type='outer'>
              <attribute name='msdyn_name' />
              <attribute name='msdyn_serviceaccount' />
            </link-entity>
            <filter>
              <condition attribute='isdocument' operator='eq' value='1' />
              <condition attribute='objectidtypecode' operator='eq' value='10122' />
              <condition attribute='notetext' operator='not-null' />
              <filter type='or'>
                {conditions}
              </filter>
              <condition attribute='objectidtypecode' operator='eq' value='10122' />
              <condition attribute='notetext' operator='not-null' />
            </filter>
            <order attribute='notetext' />
          </entity>
        </fetch>";

        // Retrieve and analyze data
        Console.WriteLine("\nRetrieving and analyzing data...");
        Console.WriteLine("(It's not stuck, wait for it to complete)\n");
        var result = await _serviceClient.RetrieveMultipleAsync(new FetchExpression(fetchXml));

        // Diagnostic information
        var rawCount = result.Entities.Count;
        var rawNoteTexts = result.Entities.Select(e => e.GetAttributeValue<string>("notetext")).ToList();
        var trimmedNoteTexts = rawNoteTexts.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).ToList();
        var caseInsensitiveDistinct = trimmedNoteTexts.Distinct(StringComparer.OrdinalIgnoreCase).Count();

        // Find duplicates for diagnostic purposes
        var duplicates = trimmedNoteTexts
            .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Take(5)
            .ToDictionary(x => x.Key, x => x.Count());

        // Display diagnostic information
        Console.WriteLine($"Raw records from query: {rawCount}");
        Console.WriteLine($"Total raw notetext values: {rawNoteTexts.Count}");
        Console.WriteLine($"After removing nulls/whitespace: {trimmedNoteTexts.Count}");
        Console.WriteLine($"Case-insensitive distinct values: {caseInsensitiveDistinct}");

        if (duplicates.Any())
        {
            Console.WriteLine("\nFound the following duplicates (first 5):");
            foreach (var kvp in duplicates)
            {
                Console.WriteLine($"NoteText: {kvp.Key} appears {kvp.Value} times");
            }
            var totalDuplicates = trimmedNoteTexts.GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
                                                 .Count(g => g.Count() > 1);
            if (totalDuplicates > 5)
            {
                Console.WriteLine($"... and {totalDuplicates - 5} more duplicates");
            }
        }

        // Process final results with case-insensitive grouping
        var groupedResults = result.Entities
            .Where(e => !string.IsNullOrWhiteSpace(e.GetAttributeValue<string>("notetext")))
            .GroupBy(
                e => e.GetAttributeValue<string>("notetext").Trim(),
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var minWorkOrderName = group
                    .Where(e => e.Contains("wo.msdyn_name"))
                    .Select(e => e.GetAttributeValue<AliasedValue>("wo.msdyn_name")?.Value?.ToString())
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .OrderBy(n => n)
                    .FirstOrDefault();

                var minServiceAccount = group
                    .Where(e => e.Contains("wo.msdyn_serviceaccount"))
                    .Select(e => e.GetAttributeValue<AliasedValue>("wo.msdyn_serviceaccount"))
                    .Where(val => val?.Value is EntityReference)
                    .Select(val => ((EntityReference)val.Value).Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .OrderBy(n => n)
                    .FirstOrDefault();

                return new WorkOrderData
                {
                    WorkOrderName = minWorkOrderName ?? "",
                    ServiceAccountName = minServiceAccount ?? "",
                    NoteText = group.Key,
                    ObjectIdType = "msdyn_workorder"
                };
            })
            .OrderBy(wo => wo.NoteText)
            .ToList();

        Console.WriteLine($"\nFinal grouped results: {groupedResults.Count}");
        Console.WriteLine("\nPress any key to continue with results display...");
        Console.ReadKey();
        Console.Clear();

        _workOrders.Clear();
        _workOrders.AddRange(groupedResults);
    }

    private void ProcessData()
    {
        foreach (var workOrder in _workOrders)
        {
            if (!string.IsNullOrEmpty(workOrder.ServiceAccountName))
            {
                var parts = workOrder.ServiceAccountName.Split('-');
                workOrder.ProcessedServiceAccountName = parts.Length >= 4
                    ? string.Join("-", parts.Take(4))
                    : workOrder.ServiceAccountName;
            }

            if (!string.IsNullOrEmpty(workOrder.NoteText))
            {
                var lastBackslashIndex = workOrder.NoteText.LastIndexOf('\\');
                workOrder.ProcessedNoteText = lastBackslashIndex >= 0
                    ? workOrder.NoteText.Substring(lastBackslashIndex + 1).Trim()
                    : workOrder.NoteText.Trim();
            }
        }
    }

    private async Task DisplayResultsAsync()
    {
        Console.Clear();
        Console.WriteLine("\nProcessed Results:");
        Console.WriteLine("----------------------------------------");

        int count = 0;
        foreach (var workOrder in _workOrders)
        {
            count++;
            Console.WriteLine($"Record {count} of {_workOrders.Count}:");
            Console.WriteLine($"Work Order: {workOrder.WorkOrderName}");
            Console.WriteLine($"Original Service Account: {workOrder.ServiceAccountName}");
            Console.WriteLine($"Processed Service Account: {workOrder.ProcessedServiceAccountName}");
            Console.WriteLine($"Original Note Text: {workOrder.NoteText}");
            Console.WriteLine($"Processed Note Text: {workOrder.ProcessedNoteText}");
            Console.WriteLine("----------------------------------------");
        }

        Console.WriteLine("\nQuery processing completed. Press any key to save this extraction...");
        await Task.Run(() => Console.ReadKey());
        Console.Clear();
    }
}