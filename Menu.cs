using CrmHub.ParkVisualizer;
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
        finally
        {
            Cleanup();
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
                HandleParkVisualizer();
                return false;

            case "10":

                if (ConnectionCheck.EnsureConnected())
                {

                    try
                    {
                        var processor = new NasQueryProcessor();
                        var nasLinks = await processor.ProcessNasDownloadsAsync();

                        // Only proceed if we got some links
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

            case "11":

                if (ConnectionCheck.EnsureConnected())
                {

                    //Folder organizer
                    await SharepointOrganizerAsync();
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
            List<TransformedTeamData> transformedTeams = FormatBUandTeams.FormatTeamData();
            if (transformedTeams != null && transformedTeams.Count > 0)
            {
                // Create or verify Business Units
                var buResults = await CreateBu.RunAsync(transformedTeams);

                // Process teams (your existing team creation code)
                var standardTeamResults = await CreateTeam.RunAsync(transformedTeams, TeamType.Standard);
                var proprietaryTeamResults = await CreateTeam.RunAsync(transformedTeams, TeamType.Proprietary);

                // Display results
                ResultsDisplay.DisplayResults(buResults, standardTeamResults, proprietaryTeamResults);
            }
            else
            {
                Console.WriteLine("No teams to create were found, press any key to continue");
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

    private void HandleParkVisualizer()
    {
        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var parkVisualizerForm = new ParkVisualizerForm();
            parkVisualizerForm.ShowDialog();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error launching Park Visualizer: {ex.Message}");
        }
        finally
        {
            ParkVisualizerCleanup();
        }
    }

    private async Task MassivePrepAsync()
    {
        //extract from excel and run query to fetch NAS links
        var processor = new NasQueryProcessor();
        await processor.ProcessNasDownloadsAsync();
    }

    private async Task SharepointOrganizerAsync()
    {
        Console.Clear();
        try
        {
            //folder organizer
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
        Console.WriteLine("6. View info about 1 or 2 users");
        Console.WriteLine("7. Switch Environment");
        Console.WriteLine("8. Update Credentials");
        Console.WriteLine("9. Park visualizer");
        Console.WriteLine("-----------------------------------------------------------------------------------------------------");
        Console.WriteLine("Extract workoders (only) attachments from EDP NAS");
        Console.WriteLine("10. Prepare NAS file extraction");
        Console.WriteLine("11. NAS File local Organizer");
        Console.WriteLine("-----------------------------------------------------------------------------------------------------");
        Console.WriteLine("0. Exit");
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

    private void Cleanup()
    {
       
    }
}