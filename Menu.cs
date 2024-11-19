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
                await HandleUserInfoRetrieval();
                return false;
            case "2":
                HandleEnvironmentSwitch();
                return false;
            case "3":
                HandleCredentialsUpdate();
                return false;
            case "4":
                HandleParkVisualizer();
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
        Console.WriteLine("1. View info about 1 or 2 users");
        Console.WriteLine("2. Switch Environment");
        Console.WriteLine("3. Update Credentials");
        Console.WriteLine("4. Park visualizer");
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