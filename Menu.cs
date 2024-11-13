public class Program
{
    public static async Task Main()
    {
        ApplicationStartup.Initialize();

        Console.WriteLine($"Initializing connection to {EnvironmentsDetails.CurrentEnvironment}...");
        // Initial connection attempt
        SessionManager.Instance.TryConnect();

        while (true)
        {
            ShowMainMenu();
            // Handle null and empty input cases
            string? input = Console.ReadLine();

            // Check for null or empty input
            if (string.IsNullOrWhiteSpace(input))
            {
                Console.WriteLine("Invalid choice");
                continue;
            }

            switch (input)
            {
                case "1":
                    if (ConnectionCheck.EnsureConnected())
                    {
                        // see info about 1 or 2 users
                        var userInfoRetriever = new UserBasicInfoRetriever();
                        await userInfoRetriever.RetrieveAndCompareUserInfoAsync();
                    }
                    break;
                case "2":
                    EnvironmentManager.SwitchEnvironment();
                    break;
                case "3":
                    CredentialsOperation.UpdateCredentials();
                    break;
                case "0":
                    return;
                default:
                    Console.WriteLine("Invalid choice");
                    break;
            }
        }
    }

    private static void ShowMainMenu()
    {
        Console.Clear();
        Console.WriteLine("=== Main Menu ===");
        Console.WriteLine($"Current Environment: {EnvironmentsDetails.CurrentEnvironment}");
        Console.WriteLine("1. View info about 1 or 2 users");
        Console.WriteLine("2. Switch Environment");
        Console.WriteLine("3. Update Credentials");
        Console.WriteLine("0. Exit");
        Console.Write("\nChoice: ");
    }
}