using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using System;
using System.Threading.Tasks;

public class MassiveDownloadProcessor
{
    private readonly string _originalEnvironment;
    private readonly List<string> _nasLinks;
    private const string TARGET_ENVIRONMENT = "DEV";
    private const string ENTITY_NAME = "edprdyn_massivedownloadfromnas";
    private const string STATUS_SCHEDULED = "Scheduled";
    private const int STATUS_SCHEDULED_CODE = 870280001;

    public MassiveDownloadProcessor(List<string> nasLinks)
    {
        _originalEnvironment = EnvironmentsDetails.CurrentEnvironment;
        _nasLinks = nasLinks;
    }

    public async Task ProcessDownloadRegistrationAsync()
    {
        try
        {
            if (_originalEnvironment != TARGET_ENVIRONMENT)
            {
                Console.WriteLine($"\nSwitching to {TARGET_ENVIRONMENT} environment...");
                await SwitchToDevEnvironmentAsync();
            }

            await CreateMassiveDownloadRecordAsync();
        }
        finally
        {
            if (_originalEnvironment != TARGET_ENVIRONMENT)
            {
                Console.WriteLine($"\nSwitching back to {_originalEnvironment} environment...");
                await RestoreOriginalEnvironmentAsync();
            }
        }
    }

    private async Task SwitchToDevEnvironmentAsync()
    {
        EnvironmentsDetails.CurrentEnvironment = TARGET_ENVIRONMENT;
        SessionManager.Instance.Disconnect();

        if (!SessionManager.Instance.TryConnect())
        {
            throw new Exception($"Failed to connect to {TARGET_ENVIRONMENT} environment");
        }

        await Task.Delay(1000);
    }

    private async Task CreateMassiveDownloadRecordAsync()
    {
        Console.Clear();

        Console.Write("Enter a name for this extraction (this will save the results in the NAS table in DEV) : ");
        string? extractionName = Console.ReadLine()?.Trim();

        if (string.IsNullOrEmpty(extractionName))
        {
            throw new ArgumentException("Extraction name cannot be empty");
        }

        var serviceClient = SessionManager.Instance.GetClient();

        // Calculate yesterday's date at 8 AM
        DateTime executionDate = DateTime.Now.Date.AddDays(-1).AddHours(8);
        Console.WriteLine("\nCreating record in massive download table...");

        try
        {
            // Combine all NAS links with line breaks
            string combinedPaths = string.Join("\n", _nasLinks);

            // Create single record with all paths
            var massiveDownload = new Entity(ENTITY_NAME)
            {
                ["edprdyn_name"] = extractionName,
                ["edprdyn_paths"] = combinedPaths,
                ["edprdyn_executionstatusname"] = STATUS_SCHEDULED,
                ["edprdyn_executionstatus"] = new OptionSetValue(STATUS_SCHEDULED_CODE),
                ["edprdyn_executiondate"] = executionDate
            };

            var recordId = await Task.Run(() => serviceClient.Create(massiveDownload));

            // Verify record was created
            var verificationQuery = new Microsoft.Xrm.Sdk.Query.QueryExpression(ENTITY_NAME)
            {
                ColumnSet = new Microsoft.Xrm.Sdk.Query.ColumnSet("edprdyn_name", "edprdyn_paths"),
                Criteria = new Microsoft.Xrm.Sdk.Query.FilterExpression
                {
                    Conditions =
                    {
                        new Microsoft.Xrm.Sdk.Query.ConditionExpression(
                            "edprdyn_massivedownloadfromnasid",
                            Microsoft.Xrm.Sdk.Query.ConditionOperator.Equal,
                            recordId)
                    }
                }
            };

            var result = await Task.Run(() => serviceClient.RetrieveMultiple(verificationQuery));

            if (result.Entities.Count == 0)
            {
                throw new Exception("Record creation verification failed - unable to retrieve created record");
            }

            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\nMassive download record created successfully for extraction: {extractionName}");
            Console.WriteLine("Press any key to return to the main menu");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\nError creating massive download record: {ex.Message}");
            Console.ResetColor();
            throw;
        }
    }

    private async Task RestoreOriginalEnvironmentAsync()
    {
        EnvironmentsDetails.CurrentEnvironment = _originalEnvironment;
        SessionManager.Instance.Disconnect();

        if (!SessionManager.Instance.TryConnect())
        {
            throw new Exception($"Failed to reconnect to {_originalEnvironment} environment");
        }

        await Task.Delay(1000);
    }
}