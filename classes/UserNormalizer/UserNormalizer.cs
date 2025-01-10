using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

public sealed partial class UserNormalizerV2 : IAsyncDisposable
{
    private readonly ServiceClient _serviceClient;
    private readonly UserRetrieverV2 _userRetriever;
    private readonly PermissionCopier _permissionCopier;

    public UserNormalizerV2()
    {
        // Use SessionManager to get the client instance
        _serviceClient = SessionManager.Instance.GetClient();
        _userRetriever = new UserRetrieverV2(_serviceClient);
        _permissionCopier = new PermissionCopier(_serviceClient);
    }

    public async ValueTask DisposeAsync()
    {
        if (_serviceClient != null)
        {
            await Task.Run(() => _serviceClient.Dispose());
        }
        GC.SuppressFinalize(this);
    }

    // Rest of the class implementation remains the same, just update any references to UserRetriever to UserRetrieverV2
    public async Task<List<UserNormalizationResult>> Run()
    {
        var results = new List<UserNormalizationResult>();
        try
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("This code will normalize a user's permissions based on their region (EU or NA)");
            Console.ResetColor();

            var user = await _userRetriever.PromptAndRetrieveUser("\nEnter the user's name or username (or 0 to exit): ");

            if (user == UserRetrieverV2.Exit || user == null)
            {
                return results;
            }

            await DisplayUserInfo(user);

            if (await ConfirmUserNormalization(user))
            {
                string regionChoice = await GetRegionChoice();

                if (regionChoice == "0")
                {
                    return results;
                }

                await NormalizeUser(user, regionChoice);
                await GiveResco(user, regionChoice);

                string username = user.GetAttributeValue<string>("domainname")?.Split('@')[0] ?? "";
                bool isInternal = IsInternalUser(username);

                results.Add(new UserNormalizationResult
                {
                    Username = username,
                    IsInternal = isInternal,
                    Name = GetUserName(user, isInternal)
                });
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"An error occurred: {ex.Message}");
            Console.WriteLine($"Stack Trace: {ex.StackTrace}");
            Console.ResetColor();
        }

        return results;
    }

    private async Task DisplayUserInfo(Entity user)
    {
        ArgumentNullException.ThrowIfNull(user);

        string username = user.GetAttributeValue<string>("domainname")?.Split('@')[0] ?? "N/A";
        string fullName = user.GetAttributeValue<string>("fullname") ?? "N/A";
        string businessUnit = user.Contains("businessunitid")
            ? ((EntityReference)user["businessunitid"]).Name
            : "N/A";
        string regionName = await GetUserRegionName(user.Id);

        Console.Clear();
        Console.WriteLine($"User: {fullName}");
        Console.WriteLine($"ID: {username}");
        Console.WriteLine($"Business Unit: {businessUnit}");
        Console.WriteLine($"Region Name: {regionName}");
    }

    private async Task<string> GetUserRegionName(Guid userId)
    {
        try
        {
            var query = new QueryExpression("systemuser")
            {
                ColumnSet = new ColumnSet(true),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression("systemuserid", ConditionOperator.Equal, userId)
                    }
                }
            };

            var result = await Task.Run(() => _serviceClient.RetrieveMultiple(query));

            if (!result.Entities.Any())
            {
                return "User not found";
            }

            var user = result.Entities[0];

            if (user.Contains("atos_regionname"))
            {
                return user.GetAttributeValue<string>("atos_regionname") ?? "Region not set";
            }

            if (user.Contains("atos_region"))
            {
                var optionSetValue = (OptionSetValue)user["atos_region"];
                return await GetOptionSetLabelAsync("systemuser", "atos_region", optionSetValue.Value);
            }

            return "Region not set";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error retrieving region name: {ex.Message}");
            return "Error";
        }
    }

    private async Task<string> GetOptionSetLabelAsync(string entityName, string attributeName, int value)
    {
        var request = new RetrieveAttributeRequest
        {
            EntityLogicalName = entityName,
            LogicalName = attributeName,
            RetrieveAsIfPublished = true
        };

        var response = (RetrieveAttributeResponse)await Task.Run(() => _serviceClient.Execute(request));
        var metadata = (EnumAttributeMetadata)response.AttributeMetadata;

        var option = metadata.OptionSet.Options.FirstOrDefault(o => o.Value == value);
        return option?.Label.UserLocalizedLabel.Label ?? $"Unknown ({value})";
    }

    private static async Task<bool> ConfirmUserNormalization(Entity user)
    {
        if (user.Contains("businessunitid") &&
            ((EntityReference)user["businessunitid"]).Name.Equals("edpr", StringComparison.OrdinalIgnoreCase))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\nWarning: The user's business unit is set to 'edpr'.");
            Console.WriteLine("This means this user with this BU and if it has roles can see all parks");
            Console.ResetColor();

            Console.WriteLine("\nAre you sure you want to continue? (y/n)");

            var response = await Task.Run(() => Console.ReadLine());
            return response?.Trim().ToLower() == "y";
        }
        return true;
    }

    private static async Task<string> GetRegionChoice()
    {
        while (true)
        {
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("\nSelect region for normalization:");
            Console.ResetColor();
            Console.WriteLine("1. EU");
            Console.WriteLine("2. NA");
            Console.WriteLine("0. Go back to the main menu");
            Console.Write("\nEnter your region choice (1, 2, or 0): ");

            var choice = await Task.Run(() => Console.ReadLine());

            if (choice is "1" or "2" or "0")
            {
                return choice;
            }

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\nInvalid choice. Please try again.");
            Console.ResetColor();
        }
    }

    private static bool IsInternalUser(string username)
    {
        return !string.IsNullOrEmpty(username) &&
               username.StartsWith("e", StringComparison.OrdinalIgnoreCase) &&
               username.Length > 1 &&
               char.IsDigit(username[1]) &&
               !username[1..2].Equals("x", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetUserName(Entity user, bool isInternal)
    {
        return isInternal
            ? user.GetAttributeValue<string>("fullname") ?? ""
            : user.GetAttributeValue<string>("firstname") ?? "";
    }

    private async Task<List<Entity>> RetrieveUserRolesAsync(Guid userId, Guid businessUnitId)
    {
        var query = new QueryExpression("role")
        {
            ColumnSet = new ColumnSet("name", "roleid"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("businessunitid", ConditionOperator.Equal, businessUnitId)
                }
            },
            LinkEntities =
            {
                new LinkEntity
                {
                    LinkFromEntityName = "role",
                    LinkToEntityName = "systemuserroles",
                    LinkFromAttributeName = "roleid",
                    LinkToAttributeName = "roleid",
                    JoinOperator = JoinOperator.Inner,
                    LinkCriteria = new FilterExpression
                    {
                        Conditions =
                        {
                            new ConditionExpression("systemuserid", ConditionOperator.Equal, userId)
                        }
                    }
                }
            }
        };

        var results = await Task.Run(() => _serviceClient.RetrieveMultiple(query));
        return results.Entities.ToList();
    }

    private async Task UpdateUserRegion(Entity user, string[] region)
    {
        var userUpdate = new Entity("systemuser")
        {
            Id = user.Id,
            ["atos_regionname"] = region[0],
            ["atos_region"] = new OptionSetValue(int.Parse(region[1]))
        };

        await Task.Run(() => _serviceClient.Update(userUpdate));

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"\nUpdated user region to: {region[0]}");
        Console.ResetColor();
    }
}