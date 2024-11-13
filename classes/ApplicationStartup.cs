public static class ApplicationStartup
{
    private const int MaxSetupAttempts = 2;
    private const int AutoContinueDelay = 2000; // 2 seconds in milliseconds

    public static void Initialize()
    {
        ShowWelcomeMessage();
        EnvironmentsDetails.CurrentEnvironment = "PRD";

        int setupAttempts = 0;
        bool setupSuccess = false;

        // Check if this is first time run before the setup loop
        bool isFirstTimeRun = IsFirstTimeRun();

        while (!setupSuccess && setupAttempts < MaxSetupAttempts)
        {
            try
            {
                if (isFirstTimeRun)
                {
                    PerformFirstTimeSetup();
                }

                // Initial connection attempt with MFA handling
                Console.WriteLine("\nInitializing connection...");
                Console.WriteLine("Note: You may be prompted for MFA authentication in your browser.");

                if (SessionManager.Instance.TryConnect())
                {
                    setupSuccess = true;
                    Console.WriteLine($"\nSuccessfully connected to {EnvironmentsDetails.CurrentEnvironment} environment!");
                    ShowPostSetupInstructions(isFirstTimeRun);
                }
            }
            catch (Exception ex)
            {
                setupAttempts++;
                HandleSetupError(ex, setupAttempts);

                if (setupAttempts < MaxSetupAttempts)
                {
                    Console.WriteLine("\nPress any key to retry setup...");
                    Console.ReadKey();
                }
            }
        }

        if (!setupSuccess)
        {
            Console.WriteLine("\nUnable to complete setup. Please verify your credentials and try again.");
            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
            Environment.Exit(1);
        }
    }

    private static void ShowPostSetupInstructions(bool isFirstTimeRun)
    {
        Console.WriteLine("\nSetup completed successfully!\n");

        if (isFirstTimeRun)
        {
            // First time setup - wait for user input
            Console.WriteLine("Press any key to continue to the main menu...");
            Console.ReadKey();
        }
        else
        {
            // Subsequent runs - auto-continue after delay
            Console.WriteLine($"Continuing to main menu in {AutoContinueDelay / 1000} seconds...");
            Thread.Sleep(AutoContinueDelay);
        }
    }

   
    private static bool IsFirstTimeRun()
    {
        var (username, password) = CredentialManager.LoadCredentials();
        return string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password);
    }

   
    private static void PerformFirstTimeSetup()
    {
        ShowFirstTimeInstructions();

        bool credentialsValid = false;
        while (!credentialsValid)
        {
            Console.Write("Username (email format): ");
            string? username = Console.ReadLine();

            if (!ValidateCredentialFormat(username))
            {
                continue;
            }

            Console.Write("Password: ");
            string password = PasswordHelper.GetSecurePassword();

            if (string.IsNullOrEmpty(password))
            {
                Console.WriteLine("Password cannot be empty. Please try again.");
                continue;
            }

            try
            {
                CredentialManager.SaveCredentials(username!, password);
                credentialsValid = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nError saving credentials: {ex.Message}");
                Console.WriteLine("Please try again.");
            }
        }
    }

    private static bool ValidateCredentialFormat(string? username)
    {
        if (string.IsNullOrEmpty(username))
        {
            Console.WriteLine("Username cannot be empty.");
            return false;
        }

        if (!username.Contains('@'))
        {
            Console.WriteLine("Username must be in email format (e.g., user@domain.com)");
            return false;
        }

        // Add any additional validation rules here
        return true;
    }

    private static void ShowWelcomeMessage()
    {
        Console.Clear();
        Console.WriteLine("=================================================");
        Console.WriteLine("     Welcome to Dynamics 365 Console Client");
        Console.WriteLine("=================================================");
        Console.WriteLine("\nInitializing application...");
    }

    private static void ShowFirstTimeInstructions()
    {
        Console.Clear();
        Console.WriteLine("First-Time Setup Instructions");
        Console.WriteLine("============================");
        Console.WriteLine("\nThis appears to be your first time running the application.");
        Console.WriteLine("\nYou will need:");
        Console.WriteLine("- Your Dynamics 365 email address");
        Console.WriteLine("- Your password");
        Console.WriteLine("- Appropriate permissions for the environment");
        Console.WriteLine("\nNotes:");
        Console.WriteLine("- You will initially connect to the PRODUCTION (PRD) environment");
        Console.WriteLine("- You can switch environments later using the environment menu");
        Console.WriteLine("- Your credentials will be securely stored in Windows Credential Manager");
        Console.WriteLine("\nPress any key to begin setup...");
        Console.ReadKey();
        Console.Clear();
    }

    private static void ShowPostSetupInstructions()
    {
        Console.WriteLine("\nSetup completed successfully!\n");
        Console.WriteLine("Press any key to continue");
        Console.ReadKey();
    }

    private static void HandleSetupError(Exception ex, int attemptNumber)
    {
        Console.Clear();
        Console.WriteLine($"\nSetup Attempt {attemptNumber} Failed");
        Console.WriteLine("============================");
        Console.WriteLine($"\nError: {ex.Message}");

        if (ex.InnerException != null)
        {
            Console.WriteLine($"Details: {ex.InnerException.Message}");
        }

        Console.WriteLine("\nCommon solutions:");
        Console.WriteLine("1. Verify your username and password are correct");
        Console.WriteLine("2. Ensure you have a stable network connection");
        Console.WriteLine("3. Check your VPN connection if required");
        Console.WriteLine("4. Verify you have the necessary permissions");

        if (attemptNumber >= MaxSetupAttempts)
        {
            Console.WriteLine("\nMaximum setup attempts reached.");
        }
    }
}