public static class CredentialsOperation
{
    public static void UpdateCredentials()
    {
        try
        {
            Console.WriteLine("\nUpdating credentials...");
            SessionManager.Instance.Disconnect();
            CredentialManager.RemoveCredentials();
            Console.WriteLine("\nPlease enter your Dynamics 365 credentials:");

            string? username;
            do
            {
                Console.Write("Username: ");
                username = Console.ReadLine();
            } while (string.IsNullOrWhiteSpace(username));

            Console.Write("Password: ");
            string password = PasswordHelper.GetSecurePassword();

            // At this point, username is guaranteed to be non-null and non-empty
            CredentialManager.SaveCredentials(username, password);

            // Test the new credentials
            SessionManager.Instance.GetClient();
            Console.WriteLine("Credentials updated successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nFailed to update credentials: {ex.Message}");
        }
    }
}