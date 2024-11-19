public static class EnvironmentManager
{
    public static void SwitchEnvironment()
    {
        Console.Clear();
        Console.WriteLine("Select environment to switch to:");
        Console.WriteLine("1. PRD (Production)");
        Console.WriteLine("2. PRE (Pre-Production)");
        Console.WriteLine("3. DEV (Development)");
        Console.Write("\nChoice (1-3): ");

        string choice = Console.ReadLine() ?? string.Empty;
        string newEnv = choice switch
        {
            "1" => "PRD",
            "2" => "PRE",
            "3" => "DEV",
            _ => string.Empty
        };

        if (string.IsNullOrEmpty(newEnv))
        {
            Console.WriteLine("Invalid choice. Environment not changed.");
            return;
        }

        // Important: Clean up old environment's connection before switching
        SessionManager.Instance.Disconnect();

        // Update environment
        EnvironmentsDetails.CurrentEnvironment = newEnv;

        // Delete existing token cache for the new environment to force fresh authentication
        string tokenCachePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CrmHub",
            newEnv,
            "TokenCache");

        try
        {
            if (File.Exists(tokenCachePath))
            {
                File.Delete(tokenCachePath);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Could not clean up old token cache: {ex.Message}");
        }

        // Try to connect to new environment
        if (SessionManager.Instance.TryConnect())
        {
            Console.WriteLine($"\nSuccessfully switched to {newEnv} environment");
        }
        else
        {
            Console.WriteLine($"\nFailed to connect to {newEnv} environment. You can try again or continue with limited functionality.");
        }
    }
}