using System;

public class Program
{
    [STAThread]
    public static async Task Main()
    {
        ApplicationConfiguration.Initialize();
        var menuHandler = new MainMenuHandler();
        await menuHandler.RunAsync();
    }
}

public class MainMenuHandler
{
    private readonly ITeamOperationsHandler? _teamOperations;
    private List<BuUserDomains>? buUserDomainsList;

    public MainMenuHandler()
    {
        var consoleUI = new ConsoleUI();
        var teamUserService = new TeamUserService();
        _teamOperations = new TeamOperationsHandler(teamUserService, consoleUI);
    }

    public async Task RunAsync()
    {
        try
        {
            InitializeApplication();
            await RunMainMenuLoop();
        }
        catch (Exception ex)
        {
            HandleFatalError(ex);
        }
    }

    private void InitializeApplication()
    {
        ApplicationStartup.Initialize();
        Console.WriteLine($"Initializing connection to {EnvironmentsDetails.CurrentEnvironment}...");
        SessionManager.Instance.TryConnect();
    }

    private async Task RunMainMenuLoop()
    {
        while (true)
        {
            ShowMainMenu();
            string? input = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(input))
            {
                Console.WriteLine("Invalid choice");
                continue;
            }

            if (await HandleMenuChoice(input))
                break; // Exit the loop if user chose to exit
        }
    }

    private async Task<bool> HandleMenuChoice(string input)
    {
        switch (input)
        {
            case "1":
                if (ConnectionCheck.EnsureConnected())
                {
                    await CreateBuAndTeams();
                }
                return false;

            case "2":
                if (ConnectionCheck.EnsureConnected() && _teamOperations != null)
                {
                    Console.Clear();
                    await _teamOperations.ExtractUsersFromTeams();
                }
                return false;

            case "3":
                if (ConnectionCheck.EnsureConnected() && _teamOperations != null)
                {
                    Console.Clear();
                    await _teamOperations.AssignTeamsToUsers();
                }
                return false;

            case "4":
                if (ConnectionCheck.EnsureConnected())
                {
                    Console.Clear();
                    var transformedTeams = FormatBUandTeams.FormatTeamData();
                    if (transformedTeams is { Count: > 0 })
                    {
                        // Create Work Order view
                        var workOrderViewCreator = new WorkOrderViewCreator(transformedTeams);
                        var workOrderResult = await workOrderViewCreator.RunAsync();

                        // Only proceed with notifications view if work order view wasn't cancelled
                        if (!workOrderResult.Cancelled)
                        {
                            var notificationsViewCreator = new NotificationsViewCreator(transformedTeams);
                            await notificationsViewCreator.RunAsync();
                        }
                    }
                    else
                    {
                        Console.WriteLine("No team data available for view creation.");
                        Console.WriteLine("\nPress any key to continue...");
                        Console.ReadKey(true);
                    }
                }
                return false;

            case "5":
                if (ConnectionCheck.EnsureConnected())
                {
                    Console.Clear();
                    await this.ChangeUsersBuToNewTeamAsync(buUserDomainsList);
                }
                return false;

            case "6":
                await HandleUserInfoRetrieval();
                return false;

            case "7":
                HandleEnvironmentSwitch();
                return false;

            case "8":
                HandleCredentialsUpdate();
                return false;

            case "9":
                if (ConnectionCheck.EnsureConnected())
                {
                    try
                    {
                        var processor = new NasQueryProcessor();
                        var nasLinks = await processor.ProcessNasDownloadsAsync();

                        if (nasLinks != null && nasLinks.Any())
                        {
                            var massiveProcessor = new MassiveDownloadProcessor(nasLinks);
                            await massiveProcessor.ProcessDownloadRegistrationAsync();
                        }
                        else
                        {
                            Console.WriteLine("\nNo NAS links were found to process.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"\nError in massive download process: {ex.Message}");
                        Console.ResetColor();
                    }
                }
                return false;

            case "10":
                if (ConnectionCheck.EnsureConnected())
                {
                    await SharepointOrganizerAsync();
                }
                return false;

            case "11":
                if (ConnectionCheck.EnsureConnected())
                {
                    Console.Clear();
                    var processor = new WorkOrderAuditProcessor();
                    await processor.RunAsync();
                }
                return false;

            case "12":
                if (ConnectionCheck.EnsureConnected())
                {
                    Console.Clear();
                    using var bulkAssignment = new BulkRoleAssignment();
                    await bulkAssignment.RunAsync();
                }
                    return false;

            case "13":
                Console.Clear();
                var userNormalizer = new UserNormalizerV2();
                List<UserNormalizationResult> results = await userNormalizer.Run();
                if (results != null && results.Count > 0)
                {
                    // Process SAP normalization only if users were normalized
                    UserSAPNormalizer.ProcessUsersAsync(results);
                    // Run workflow for normalized users
                    await RunNewUserWorkFlow.ExecuteWorkflowForUsersAsync(results);
                }
                return false;

            case "0":
                return true; // Signal to exit
            default:
                Console.WriteLine("Invalid choice");
                return false;
        }
    }

    private async Task HandleUserInfoRetrieval()
    {
        if (ConnectionCheck.EnsureConnected())
        {
            var userInfoRetriever = new UserBasicInfoRetriever();
            await userInfoRetriever.RetrieveAndCompareUserInfoAsync();
        }
    }

    private async Task CreateBuAndTeams()
    {
        try
        {
            var transformedTeams = FormatBUandTeams.FormatTeamData();
            if (transformedTeams is { Count: > 0 })
            {
                // Create or verify Business Units with cancellation support
                var buResults = await CreateBu.RunAsync(transformedTeams);

                // Only proceed with team creation if not cancelled
                if (!buResults.Any(r => r.Cancelled))
                {
                    var standardTeamResults = await CreateTeam.RunAsync(transformedTeams, TeamType.Standard);
                    var proprietaryTeamResults = await CreateTeam.RunAsync(transformedTeams, TeamType.Proprietary);
                    ResultsDisplay.DisplayResults(buResults, standardTeamResults, proprietaryTeamResults);
                }
                else
                {
                    Console.WriteLine("BU creation was cancelled. Team creation skipped.");
                }
            }
            else
            {
                Console.WriteLine("No teams to create were found. Press any key to continue");
                Console.ReadKey(true);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex.Message}");
        }
    }

    private void HandleEnvironmentSwitch()
    {
        EnvironmentManager.SwitchEnvironment();
    }

    private void HandleCredentialsUpdate()
    {
        CredentialsOperation.UpdateCredentials();
    }

    private async Task MassivePrepAsync()
    {
        var processor = new NasQueryProcessor();
        await processor.ProcessNasDownloadsAsync();
    }

    private async Task SharepointOrganizerAsync()
    {
        Console.Clear();
        try
        {
            var organizer = new NasLocalFileOrganizer();
            await organizer.RunAsync();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error in Sharepoint Organizer: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            Console.ResetColor();
            Console.WriteLine("Press any key to continue...");
            Console.ReadKey();
        }
    }

    private void ShowMainMenu()
    {
        try
        {
            Console.Clear();
            DisplayMenu();
        }
        catch (InvalidOperationException)
        {
            // If Console.Clear() fails, display without clearing
            Console.WriteLine();
            DisplayMenu();
        }
    }

    private void DisplayMenu()
    {
        Console.WriteLine("=== Main Menu ===");
        Console.WriteLine($"Current Environment: {EnvironmentsDetails.CurrentEnvironment}\n");

        Console.WriteLine("Create Teams flow");
        Console.WriteLine("-----------------------------------------------------------------------------------------------------");
        Console.WriteLine("1.  Create/Update Team Process (BU, Contrata team, EDPR Team) (1-5 is only for EU teams only)");
        Console.WriteLine("2.  Extract users from BU and it's contrata Contrata team");
        Console.WriteLine("3.  Give newly created team to extracted users");
        Console.WriteLine("4.  Create views for workorders and notifications (ordens de trabajo e avisos)");
        Console.WriteLine("--- Run the worflows in XRM Toolbox ---");
        Console.WriteLine("5.  Change BU of users that have in their name the Contractor (Puesto de trabajo)");
        Console.WriteLine("--- Activate the created BU(s) in the form ---");
        Console.WriteLine("-----------------------------------------------------------------------------------------------------");
        Console.WriteLine("6.  View info about 1 or 2 users");
        Console.WriteLine("7.  Switch Environment");
        Console.WriteLine("8.  Update Credentials");
        Console.WriteLine("-----------------------------------------------------------------------------------------------------");
        Console.WriteLine("Extract workoders (only) attachments from EDP NAS");
        Console.WriteLine("9.  Prepare NAS file extraction");
        Console.WriteLine("10. NAS File local Organizer");
        Console.WriteLine("-----------------------------------------------------------------------------------------------------");
        Console.WriteLine("11. Apanhar ordens mal 700");
        Console.WriteLine("12. Assign same role  to multiple users");
        Console.WriteLine("13. Normalize Users");
        Console.WriteLine("0.  Exit");
        Console.Write("\nChoice: ");
    }

    private void HandleFatalError(Exception ex)
    {
        Console.WriteLine($"Fatal error: {ex.Message}");
        Console.WriteLine("Press Enter to exit...");
        Console.ReadLine();
    }

    private void ParkVisualizerCleanup()
    {
        foreach (Form form in Application.OpenForms)
        {
            form.Close();
        }
    }
}