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
            // Step 1: Disconnect from current environment
            Console.WriteLine($"Disconnecting from {EnvironmentsDetails.CurrentEnvironment} environment...");
            SessionManager.Instance.Disconnect();

            // Step 2: Clean up token cache for current environment
            Console.WriteLine("Cleaning up token cache...");
            CleanupTokenCache(EnvironmentsDetails.CurrentEnvironment);

            // Step 3: Update environment setting
            string oldEnv = EnvironmentsDetails.CurrentEnvironment;
            EnvironmentsDetails.CurrentEnvironment = newEnv;

            // Step 4: Force new connection
            Console.WriteLine($"\nConnecting to {newEnv} environment...");
            Console.WriteLine("You may be prompted for MFA verification, but existing credentials will be reused if possible.");

            if (SessionManager.Instance.TryConnect())
            {
                Console.WriteLine($"\nSuccessfully connected to {newEnv} environment!");
                Thread.Sleep(1000); // Give user time to see success message
            }
            else
            {
                // Revert environment setting if connection fails
                EnvironmentsDetails.CurrentEnvironment = oldEnv;
                throw new Exception($"Failed to connect to {newEnv} environment");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nError switching environment: {ex.Message}");

            // Try to recover the previous connection
            AttemptRecovery();

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
                            // Try to securely delete sensitive token files
                            SecureDeleteFile(file);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Warning: Could not delete file {file}: {ex.Message}");
                            // Fallback to regular delete if secure delete fails
                            try
                            {
                                File.Delete(file);
                            }
                            catch
                            {
                                Console.WriteLine($"Warning: Could not delete file {Path.GetFileName(file)}");
                            }
                        }
                    }
                }
            }

            Console.WriteLine($"Token cache for {environment} environment cleared.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Error cleaning up token cache: {ex.Message}");
        }
    }

    private static void SecureDeleteFile(string filePath)
    {
        if (!File.Exists(filePath)) return;

        try
        {
            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length > 0)
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Write);
                // First overwrite with zeros
                byte[] zeros = new byte[fileInfo.Length];
                fs.Position = 0;
                fs.Write(zeros, 0, zeros.Length);
                // Then overwrite with ones (0xFF)
                byte[] ones = Enumerable.Repeat((byte)0xFF, (int)fileInfo.Length).ToArray();
                fs.Position = 0;
                fs.Write(ones, 0, ones.Length);
                fs.Flush(true);
            }
            File.Delete(filePath);
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to securely delete file: {ex.Message}", ex);
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