﻿using System;
using CrmHub.Classes.ParkVisualizer;
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
    private List<BuUserDomains>? buUserDomainsList;
    private List<TransformedTeamData>? transformedTeamData; 


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
                if (ConnectionCheck.EnsureConnected())
                {
                    Console.Clear();
                    transformedTeamData = await ExtractUsersFromTeam.FormatTeamData();
                    if (transformedTeamData != null && transformedTeamData.Any())
                    {
                        buUserDomainsList = await ExtractUsersFromTeam.CreateExcel(transformedTeamData);
                    }
                }
                return false;

            case "3":
                if (ConnectionCheck.EnsureConnected())
                {
                    Console.Clear();
                    if (transformedTeamData == null || buUserDomainsList == null)
                    {
                        Console.WriteLine("Please run option 2 first to extract the required data.");
                        Console.WriteLine("Press any key to continue...");
                        Console.ReadKey();
                        return false;
                    }

                    var assignTeamToUser = new AssignNewTeamToUser(buUserDomainsList);
                    await assignTeamToUser.ProcessUsersAsync();
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
                    await UserCopier();
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
                    await UserSAPNormalizer.ProcessUsersAsync(results);
                    // Run workflow for normalized users
                    await RunNewUserWorkFlow.ExecuteWorkflowForUsersAsync(results);
                }
                return false;

            case "14":
                Console.Clear();
                var parkExplorer = new ParkExplorerHandler();
                await parkExplorer.LaunchParkExplorer();
                return false;

          
            case "15":
                if (ConnectionCheck.EnsureConnected())
                {
                    Console.Clear();

                    await HoldUserRoles();
                }
                return false;

            case "16":
                if (ConnectionCheck.EnsureConnected())
                {
                    Console.Clear();
                    await RescoLoginAsync();
                }
                return false;

            case "17":
                await JiraRitmLogger();

                return false;

            case "18":
           
                Console.Clear();
                await AssignTeamsFromExcel();
                
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

    private async Task UserCopier()
    {
        try
        {
            var copier = new UserPermissionCopier();
            await copier.Run();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error in User Copier: {ex.Message}");
            Console.ResetColor();
            Console.WriteLine("Press any key to continue...");
            Console.ReadKey();
        }
    }

    private async Task RescoLoginAsync()
    {
        try
        {
            await new RescoLicenseProcessor().ProcessRescoLicensesAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }

    private async Task HoldUserRoles()
    {
        try
        {
            var userRoleManager = new UserRoleManager();
            await userRoleManager.Run();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error in User Role Manager: {ex.Message}");
            Console.ResetColor();
            Console.WriteLine("Press any key to continue...");
            Console.ReadKey();
        }
    }

    private async Task CreateBuAndTeams()
    {
        Console.Clear();

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

    private async Task JiraRitmLogger()
    {
        using (var jiraLogger = new JiraRitmLogger())
        {
            await jiraLogger.LogHoursMenu();
        }
    }

    private async Task AssignTeamsFromExcel()
    {
        try
        {
            var assignTeams = new AssignTeams();
            await assignTeams.ProcessAssignTeamsAsync();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"An error occurred: {ex.Message}");
            Console.ResetColor();
            Console.WriteLine("Press any key to return to the menu.");
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
        Console.WriteLine("3.  Give newly created team to extracted users (if any were extracted in step 2)");
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
        Console.WriteLine("11. Copy BU, Teams and Roles from one user to the other");
        Console.WriteLine("12. Assign same role to multiple users");
        Console.WriteLine("13. Normalize Users");
        Console.WriteLine("14. Open Park Explorer (this is a proof-of-concept for adding React UI to this console)");
        Console.WriteLine("15. Hold user current roles while the BU is replaced and then reapply them");
        Console.WriteLine("16. Get Users Resco last login (for massive extraction when asked by the bussiness)");
        Console.WriteLine("17. Register RITM hours in JIRA");
        Console.WriteLine("18. Assign Teams to Users in the 'Assign Teams' worksheet");
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