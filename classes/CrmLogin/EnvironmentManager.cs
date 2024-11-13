public static class EnvironmentManager
{
    public static void SwitchEnvironment()
    {
        Console.Clear();
        Console.WriteLine("\nSelect environment to switch to:");
        Console.WriteLine("1. PRD (Production)");
        Console.WriteLine("2. PRE (Pre-Production)");
        Console.WriteLine("3. DEV (Development)");
        Console.Write("\nChoice (1-3): ");

        // Handle null case for Console.ReadLine()
        string choice = Console.ReadLine() ?? string.Empty;

        // Use string.Empty instead of null
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

        // Disconnect current session
        SessionManager.Instance.Disconnect();

        // Update environment
        EnvironmentsDetails.CurrentEnvironment = newEnv;

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