using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System.Collections.Concurrent;

public sealed class UserRetrieverV2
{
    private readonly ServiceClient _serviceClient;

    // Define a static field to represent the go back to main menu
    public static readonly Entity Exit = new("exit");

    public UserRetrieverV2(ServiceClient serviceClient)
    {
        _serviceClient = serviceClient ?? throw new ArgumentNullException(nameof(serviceClient));
    }

    public async Task<Entity> PromptAndRetrieveUser(string prompt)
    {
        while (true)
        {
            Console.Write(prompt);
            var input = Console.ReadLine();

            // If the user inputs 0 it sends entity type back with an exit to indicate the code must go back to main
            if (input == "0")
            {
                return Exit;
            }

            var users = await FindUsersAsync(input ?? string.Empty);

            if (users.Count == 0)
            {
                Console.WriteLine("No users found. Please try again.");
                Console.WriteLine("If the user is not found it might be disabled");
                continue;
            }

            if (users.Count == 1)
            {
                return users[0];
            }

            while (true)
            {
                Console.WriteLine("\nMultiple users found:");
                for (int i = 0; i < users.Count; i++)
                {
                    string domainName = users[i].GetAttributeValue<string>("domainname") ?? "N/A";
                    string username = domainName != "N/A" ? domainName.Split('@')[0] : "N/A";
                    string fullName = users[i].GetAttributeValue<string>("fullname") ?? "N/A";
                    Console.WriteLine($"({i + 1}) {fullName} (Username: {username})");
                }

                Console.Write($"\nSelect one of the users (1-{users.Count}), or press 0 to go back to the previous search: ");
                if (int.TryParse(Console.ReadLine(), out int selection))
                {
                    if (selection == 0)
                    {
                        break;
                    }

                    if (selection >= 1 && selection <= users.Count)
                    {
                        return users[selection - 1];
                    }
                }

                Console.WriteLine("\nInvalid selection. Please try again. Press any key to retry");
                Console.ReadKey();
                Console.Clear();
            }
        }
    }

    public async Task<List<Entity>> FindUsersAsync(string input)
    {
        var query = new QueryExpression("systemuser")
        {
            ColumnSet = new ColumnSet("fullname", "businessunitid", "domainname", "internalemailaddress", "windowsliveid"),
            Criteria = new FilterExpression(LogicalOperator.And)
            {
                Conditions =
                {
                    new ConditionExpression("isdisabled", ConditionOperator.Equal, false)
                },
                Filters =
                {
                    new FilterExpression(LogicalOperator.Or)
                    {
                        Conditions =
                        {
                            new ConditionExpression("domainname", ConditionOperator.BeginsWith, input),
                            new ConditionExpression("internalemailaddress", ConditionOperator.BeginsWith, input),
                            new ConditionExpression("windowsliveid", ConditionOperator.BeginsWith, input),
                            new ConditionExpression("fullname", ConditionOperator.Like, $"%{input}%"),
                            new ConditionExpression("yomifullname", ConditionOperator.Like, $"%{input}%")
                        }
                    }
                }
            }
        };

        var result = await Task.Run(() => _serviceClient.RetrieveMultiple(query), CancellationToken.None);

        return result.Entities
            .Where(e =>
                (e.Contains("domainname") && e.GetAttributeValue<string>("domainname")?.IndexOf(input, StringComparison.OrdinalIgnoreCase) >= 0) ||
                (e.Contains("internalemailaddress") && e.GetAttributeValue<string>("internalemailaddress")?.IndexOf(input, StringComparison.OrdinalIgnoreCase) >= 0) ||
                (e.Contains("windowsliveid") && e.GetAttributeValue<string>("windowsliveid")?.IndexOf(input, StringComparison.OrdinalIgnoreCase) >= 0) ||
                (e.Contains("fullname") && e.GetAttributeValue<string>("fullname")?.IndexOf(input, StringComparison.OrdinalIgnoreCase) >= 0) ||
                (e.Contains("yomifullname") && e.GetAttributeValue<string>("yomifullname")?.IndexOf(input, StringComparison.OrdinalIgnoreCase) >= 0))
            .ToList();
    }

    public async Task<Entity> RetrieveUserAsync(Guid userId)
    {
        return await Task.Run(() => _serviceClient.Retrieve("systemuser", userId, new ColumnSet(true)));
    }
}