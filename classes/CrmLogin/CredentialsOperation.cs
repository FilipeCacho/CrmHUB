using System.Security.Cryptography;

public static class CredentialsOperation
{
    // Constants for secure file deletion
    private const int OVERWRITE_PASSES = 3;
    private const string TOKEN_FOLDER_NAME = "CrmHub";

    public static void UpdateCredentials()
    {
        try
        {
            // Clear console and show warning
            Console.Clear();
            DisplayWarningMessage();
            if (!ConfirmOperation()) return;

            // Step 1: Close active connections
            Console.WriteLine("\nStep 1: Closing active connections...");
            SessionManager.Instance.Disconnect();
            Console.WriteLine("Active connections closed successfully.");

            // Step 2: Remove authentication tokens with enhanced security
            Console.WriteLine("\nStep 2: Removing authentication tokens...");
            SecurelyRemoveTokens();

            // Step 3: Remove Windows Credentials with verification
            Console.WriteLine("\nStep 3: Removing stored credentials...");
            RemoveCredentialsWithVerification();

            // Step 4: Initialize new authentication
            Console.WriteLine("\nStep 4: Starting new authentication process...");
            Console.WriteLine("Note: A browser window may open for MFA verification.");

            if (SessionManager.Instance.TryConnect())
            {
                Console.WriteLine("\nCredential update completed successfully!");
                Console.WriteLine("New credentials validated and token cache created.");
            }
            else
            {
                throw new Exception("Failed to validate new credentials");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nERROR: Failed to update credentials: {ex.Message}");
            Console.WriteLine("Please try again or contact support if the issue persists.");
            throw;
        }
    }

    private static void SecurelyRemoveTokens()
    {
        // Get all possible environment token paths
        var environments = new[] { "PRD", "PRE", "DEV" };
        foreach (var env in environments)
        {
            string tokenPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                TOKEN_FOLDER_NAME,
                env);

            if (Directory.Exists(tokenPath))
            {
                // Securely delete all files in the directory
                foreach (string file in Directory.GetFiles(tokenPath, "*.*", SearchOption.AllDirectories))
                {
                    if (IsTokenFile(file))
                    {
                        SecureDeleteFile(file);
                    }
                }

                try
                {
                    Directory.Delete(tokenPath, recursive: true);
                    Console.WriteLine($"Token cache for {env} environment securely cleared.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Could not remove token directory for {env}: {ex.Message}");
                }
            }
        }
    }

    private static bool IsTokenFile(string filePath)
    {
        // Verify file belongs to our application
        var validExtensions = new[] { ".token", ".lifetime", ".cache" };
        var fileInfo = new FileInfo(filePath);
        return validExtensions.Contains(fileInfo.Extension.ToLower());
    }

    private static void RemoveCredentialsWithVerification()
    {
        // Remove credentials
        CredentialManager.RemoveCredentials();

        // Verify removal
        var (username, password) = CredentialManager.LoadCredentials();
        if (username != null || password != null)
        {
            throw new Exception("Failed to completely remove credentials from Windows Credential Manager");
        }

        Console.WriteLine("Credentials successfully removed from Windows Credential Manager.");
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
                for (int pass = 0; pass < OVERWRITE_PASSES; pass++)
                {
                    byte[] overwriteData = GetOverwritePattern(pass, (int)fs.Length);
                    fs.Position = 0;
                    fs.Write(overwriteData, 0, overwriteData.Length);
                    fs.Flush(true);
                }
            }
            File.Delete(filePath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Could not securely delete file {Path.GetFileName(filePath)}: {ex.Message}");
            try
            {
                File.Delete(filePath);
            }
            catch
            {
                Console.WriteLine($"Warning: Could not delete file {Path.GetFileName(filePath)}");
            }
        }
    }

    private static byte[] GetOverwritePattern(int pass, int length)
    {
        return pass switch
        {
            0 => new byte[length], // Zeros
            1 => Enumerable.Repeat((byte)0xFF, length).ToArray(), // Ones
            _ => GenerateSecureRandomBytes(length) // Random data using cryptographic RNG
        };
    }

    private static byte[] GenerateSecureRandomBytes(int length)
    {
        byte[] randomBytes = new byte[length];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(randomBytes);
        }
        return randomBytes;
    }

    private static void DisplayWarningMessage()
    {
        Console.WriteLine("=== Credential Update Process ===\n");
        Console.WriteLine("WARNING: This process will:");
        Console.WriteLine("1. Close all active Dynamics 365 connections");
        Console.WriteLine("2. Permanently delete stored authentication tokens");
        Console.WriteLine("3. Remove credentials from Windows Credential Manager");
        Console.WriteLine("4. Require new credentials and possible MFA authentication\n");
    }

    private static bool ConfirmOperation()
    {
        while (true)
        {
            Console.Write("Do you want to continue? (y/n): ");
            string? response = Console.ReadLine()?.ToLower();

            if (response == "n")
            {
                Console.WriteLine("\nCredential update cancelled. No changes were made.");
                return false;
            }
            if (response == "y") return true;
            Console.WriteLine("Please enter 'y' or 'n'");
        }
    }
}