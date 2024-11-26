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

        if (newEnv == EnvironmentsDetails.CurrentEnvironment)
        {
            Console.WriteLine($"Already connected to {newEnv} environment.");
            return;
        }

        try
        {
            // Step 1: Disconnect and cleanup
            SessionManager.Instance.Disconnect();

            // Step 2: Update environment setting
            EnvironmentsDetails.CurrentEnvironment = newEnv;

            // Step 3: Force new connection
            Console.WriteLine($"\nConnecting to {newEnv} environment...");
            if (SessionManager.Instance.TryConnect())
            {
                Console.WriteLine($"\nSuccessfully connected to {newEnv} environment!");
                Thread.Sleep(1000); // Give user time to see success message
            }
            else
            {
                throw new Exception($"Failed to connect to {newEnv} environment");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nError switching environment: {ex.Message}");
            Console.WriteLine("Press any key to continue...");
            Console.ReadKey();
        }
    }



private static void CleanupTokenCache(string environment)
    {
        try
        {
            // Get the token cache directory for the specified environment
            string tokenCacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CrmHub",
                environment);

            // Delete all token-related files if directory exists
            if (Directory.Exists(tokenCacheDir))
            {
                // Delete all files with specific extensions
                foreach (string file in Directory.GetFiles(tokenCacheDir, "*.*"))
                {
                    string extension = Path.GetExtension(file).ToLower();
                    if (extension == ".token" || extension == ".lifetime" || extension == ".cache")
                    {
                        try
                        {
                            File.Delete(file);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Warning: Could not delete file {file}: {ex.Message}");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Error cleaning up token cache: {ex.Message}");
        }
    }

    private static void AttemptRecovery()
    {
        Console.WriteLine("\nAttempting to recover previous connection...");

        // Try to reconnect a few times before giving up
        for (int i = 0; i < 3; i++)
        {
            if (SessionManager.Instance.TryConnect())
            {
                Console.WriteLine("Successfully recovered connection.");
                return;
            }
            Thread.Sleep(1000); // Wait a second between attempts
        }

        Console.WriteLine("WARNING: Could not recover connection. Please restart the application.");
    }
}