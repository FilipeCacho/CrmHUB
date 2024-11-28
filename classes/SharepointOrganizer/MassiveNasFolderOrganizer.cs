using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

public class MassiveNasFolderOrganizer
{
    private readonly ServiceClient _serviceClient;
    private List<WorkOrderData> _workOrders;

    public MassiveNasFolderOrganizer()
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
    }

    public async Task RunAsync()
    {
        try
        {
            await RetrieveDataFromDynamics();
            ProcessData();
            DisplayResults();

            while (true)
            {
                Console.Write("\nDo you want to continue with file organization? (y/n): ");
                var response = Console.ReadLine()?.ToLower();
                Console.WriteLine($"You entered: {response}");

                if (response == "n")
                {
                    Console.WriteLine("Operation cancelled by user.");
                    Console.WriteLine("Press any key to continue...");
                    Console.ReadKey();
                    return;
                }

                if (response == "y")
                {
                    Console.WriteLine("\nOpening folder selection dialog...");
                    var selectedFolder = SelectFolder();

                    if (string.IsNullOrEmpty(selectedFolder))
                    {
                        Console.WriteLine("No folder was selected. Operation cancelled.");
                        Console.WriteLine("Press any key to continue...");
                        Console.ReadKey();
                        return;
                    }

                    Console.WriteLine($"\nSelected folder: {selectedFolder}");
                    Console.WriteLine("Starting file organization...");
                    OrganizeFiles(selectedFolder);

                    Console.WriteLine("\nOperation completed. Press any key to continue...");
                    Console.ReadKey();
                    return;
                }

                Console.WriteLine("Invalid input. Please enter 'y' or 'n'.");
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error during operation: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            Console.ResetColor();
            Console.WriteLine("\nPress any key to continue...");
            Console.ReadKey();
        }
    }

    public async Task RetrieveDataFromDynamics()
    {
        var fetchXml = @"
    <fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
      <entity name='annotation'>
        <attribute name='notetext' />
        <attribute name='objectid' />
        <link-entity name='msdyn_workorder' from='msdyn_workorderid' to='objectid' alias='wo' link-type='outer'>
          <attribute name='msdyn_name' />
          <attribute name='msdyn_serviceaccount' />
        </link-entity>
        <filter type='and'>
          <condition attribute='isdocument' operator='eq' value='1' />
          <condition attribute='objectidtypecode' operator='eq' value='10122' />
          <condition attribute='notetext' operator='not-null' />
          <filter type='or'>
            <condition attribute='owningbusinessunitname' operator='like' value='%0-FR-BQH%' />
            <condition attribute='owningbusinessunitname' operator='like' value='%0-FR-BBC%' />
            <condition attribute='owningbusinessunitname' operator='like' value='%0-FR-BRX%' />
            <condition attribute='owningbusinessunitname' operator='like' value='%0-FR-FVN%' />
            <condition attribute='owningbusinessunitname' operator='like' value='%0-FR-LLC%' />
            <condition attribute='owningbusinessunitname' operator='like' value='%0-FR-MCV%' />
            <condition attribute='owningbusinessunitname' operator='like' value='%0-FR-MRV%' />
            <condition attribute='owningbusinessunitname' operator='like' value='%0-FR-PDY%' />
            <condition attribute='owningbusinessunitname' operator='like' value='%0-FR-TSS%' />
            <condition attribute='owningbusinessunitname' operator='like' value='%0-FR-PV3%' />
            <condition attribute='owningbusinessunitname' operator='like' value='%0-BE-SVY%' />
            <condition attribute='owningbusinessunitname' operator='like' value='%0-FR-VDN%' />
          </filter>
        </filter>
        <order attribute='subject' />
      </entity>
    </fetch>";

        Console.WriteLine("Retrieving data from Dynamics...");
        var result = await Task.Run(() => _serviceClient.RetrieveMultiple(new FetchExpression(fetchXml)));

        foreach (var entity in result.Entities)
        {
            try
            {
                var serviceAccountName = "";
                var workOrderName = "";

                if (entity.Contains("wo.msdyn_serviceaccount"))
                {
                    var aliasedValue = entity.GetAttributeValue<AliasedValue>("wo.msdyn_serviceaccount");
                    if (aliasedValue?.Value is EntityReference serviceAccountRef)
                    {
                        serviceAccountName = serviceAccountRef.Name ?? "";
                    }
                }

                if (entity.Contains("wo.msdyn_name"))
                {
                    var aliasedValue = entity.GetAttributeValue<AliasedValue>("wo.msdyn_name");
                    workOrderName = aliasedValue?.Value?.ToString() ?? "";
                }

                var noteText = entity.GetAttributeValue<string>("notetext") ?? "";

                // Add debug logging
                Console.WriteLine("\nDebug - Raw Entity Values:");
                foreach (var attr in entity.Attributes)
                {
                    if (attr.Value is AliasedValue aliased)
                    {
                        Console.WriteLine($"Key: {attr.Key}");
                        Console.WriteLine($"  Value Type: {aliased.Value.GetType().Name}");
                        if (aliased.Value is EntityReference entRef)
                        {
                            Console.WriteLine($"  Entity Reference Name: {entRef.Name}");
                            Console.WriteLine($"  Entity Reference Id: {entRef.Id}");
                            Console.WriteLine($"  Entity Reference LogicalName: {entRef.LogicalName}");
                        }
                        else
                        {
                            Console.WriteLine($"  Value: {aliased.Value}");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Key: {attr.Key}, Value: {attr.Value}");
                    }
                }

                if (!string.IsNullOrEmpty(noteText))
                {
                    var workOrder = new WorkOrderData
                    {
                        WorkOrderName = workOrderName,
                        ServiceAccountName = serviceAccountName,
                        NoteText = noteText
                    };

                    Console.WriteLine($"\nFound Work Order:");
                    Console.WriteLine($"Work Order Name: {workOrder.WorkOrderName}");
                    Console.WriteLine($"Service Account: {workOrder.ServiceAccountName}");
                    Console.WriteLine($"Note Text: {workOrder.NoteText}");

                    _workOrders.Add(workOrder);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing entity: {ex.Message}");
            }
        }

        Console.WriteLine($"\nRetrieved {_workOrders.Count} work orders");
    }

    private void ProcessData()
    {
        foreach (var workOrder in _workOrders)
        {
            // Process ServiceAccountName
            if (!string.IsNullOrEmpty(workOrder.ServiceAccountName))
            {
                var parts = workOrder.ServiceAccountName.Split('-');
                if (parts.Length >= 4)
                {
                    // Take only the first 4 parts to create the service account folder name
                    workOrder.ProcessedServiceAccountName = string.Join("-", parts.Take(4));
                    Console.WriteLine($"Processed service account name: {workOrder.ServiceAccountName} -> {workOrder.ProcessedServiceAccountName}");
                }
                else
                {
                    workOrder.ProcessedServiceAccountName = workOrder.ServiceAccountName;
                    Console.WriteLine($"Using original service account name: {workOrder.ServiceAccountName}");
                }
            }

            // Process NoteText
            if (!string.IsNullOrEmpty(workOrder.NoteText))
            {
                var lastBackslashIndex = workOrder.NoteText.LastIndexOf('\\');
                workOrder.ProcessedNoteText = lastBackslashIndex >= 0
                    ? workOrder.NoteText.Substring(lastBackslashIndex + 1)
                    : workOrder.NoteText;
                Console.WriteLine($"Processed note text: {workOrder.NoteText} -> {workOrder.ProcessedNoteText}");
            }
        }
    }

    private void DisplayResults()
    {
        Console.WriteLine("\nProcessed Results:");
        Console.WriteLine("----------------------------------------");
        foreach (var workOrder in _workOrders)
        {
            Console.WriteLine($"Work Order: {workOrder.WorkOrderName}");
            Console.WriteLine($"Original Service Account: {workOrder.ServiceAccountName}");
            Console.WriteLine($"Processed Service Account: {workOrder.ProcessedServiceAccountName}");
            Console.WriteLine($"Original Note Text: {workOrder.NoteText}");
            Console.WriteLine($"Processed Note Text: {workOrder.ProcessedNoteText}");
            Console.WriteLine("----------------------------------------");
        }
    }

    private string SelectFolder()
    {
        string selectedPath = "";
        var thread = new Thread(() =>
        {
            try
            {
                using (var form = new Form())
                {
                    form.Visible = false;
                    using (var dialog = new FolderBrowserDialog())
                    {
                        dialog.Description = "Select folder containing the files to organize";
                        dialog.UseDescriptionForTitle = true;
                        dialog.ShowNewFolderButton = true;

                        if (dialog.ShowDialog(form) == DialogResult.OK)
                        {
                            selectedPath = dialog.SelectedPath;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in folder dialog: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        return selectedPath;
    }
    private void OrganizeFiles(string sourceFolder)
    {
        try
        {
            Console.WriteLine($"\nStarting file organization in folder: {sourceFolder}");
            Console.WriteLine("Current work orders in memory:");
            foreach (var wo in _workOrders)
            {
                Console.WriteLine($"SA: {wo.ProcessedServiceAccountName} | WO: {wo.WorkOrderName} | File: {wo.ProcessedNoteText}");
            }

            // First, create service account folders
            var serviceAccounts = _workOrders
                .Where(wo => !string.IsNullOrEmpty(wo.ProcessedServiceAccountName))
                .Select(wo => wo.ProcessedServiceAccountName)
                .Distinct()
                .ToList();

            foreach (var sa in serviceAccounts)
            {
                var saPath = Path.Combine(sourceFolder, sa);
                if (!Directory.Exists(saPath))
                {
                    Console.WriteLine($"Creating service account folder: {sa}");
                    Directory.CreateDirectory(saPath);
                }

                // Get work orders for this service account
                var relatedWorkOrders = _workOrders
                    .Where(wo => wo.ProcessedServiceAccountName == sa)
                    .Where(wo => !string.IsNullOrEmpty(wo.WorkOrderName));

                foreach (var wo in relatedWorkOrders)
                {
                    var woPath = Path.Combine(saPath, wo.WorkOrderName);
                    if (!Directory.Exists(woPath))
                    {
                        Console.WriteLine($"Creating work order folder: {wo.WorkOrderName} in {sa}");
                        Directory.CreateDirectory(woPath);
                    }

                    // Move file if it exists
                    if (!string.IsNullOrEmpty(wo.ProcessedNoteText))
                    {
                        var sourceFile = Path.Combine(sourceFolder, wo.ProcessedNoteText);
                        var destFile = Path.Combine(woPath, wo.ProcessedNoteText);

                        if (File.Exists(sourceFile))
                        {
                            try
                            {
                                Console.WriteLine($"Moving file {wo.ProcessedNoteText} to {woPath}");
                                if (!File.Exists(destFile))
                                {
                                    File.Move(sourceFile, destFile);
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error moving file: {ex.Message}");
                            }
                        }
                        else
                        {
                            Console.WriteLine($"Source file not found: {sourceFile}");
                        }
                    }
                }
            }

            Console.WriteLine("\nFile organization completed!");
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error organizing files: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            Console.ResetColor();
        }
    }
}