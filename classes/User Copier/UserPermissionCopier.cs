using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public sealed class UserPermissionCopier
{
    private ServiceClient _serviceClient;
    private UserRetriever _userRetriever;
    private PermissionCopier _permissionCopier;

    public async Task Run()
    {
        try
        {
            await ConnectToDataverseAsync();
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("This flow can copy from one user (the source user) its BU's, Roles or Teams to other user (the target user)");
            Console.WriteLine("Roles or Teams that already exist in the target user will be skipped");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\n(Or press 0 to return to main menu)");
            Console.ResetColor();

            var sourceUser = await _userRetriever.PromptAndRetrieveUserLegacy("\nEnter the source user's name or EX: ");
            if (sourceUser == UserRetriever.ExitLegacy) return;
            if (sourceUser == null || !await IsUserInitializedAsync(sourceUser))
            {
                Console.WriteLine("Invalid source user. Press any key to exit.");
                Console.ReadKey();
                return;
            }

            var targetUser = await _userRetriever.PromptAndRetrieveUserLegacy("\nEnter the target user's name or ID: ");
            if (targetUser == UserRetriever.ExitLegacy) return;
            if (targetUser == null)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Target user not found. Press any key to exit.");
                Console.ResetColor();
                Console.ReadKey();
                return;
            }

            Console.Clear();
            DisplayUserInfo(sourceUser, targetUser);

            bool copyBU = await PromptForPermissionCopy("Business Unit", sourceUser, targetUser);
            bool copyTeams = await PromptForPermissionCopy("Teams", sourceUser, targetUser);
            bool copyRoles = await PromptForPermissionCopy("Roles", sourceUser, targetUser);

            if (copyBU)
            {
                await _permissionCopier.CopyBusinessUnitLegacy(sourceUser, targetUser);
                targetUser = await _userRetriever.RetrieveUserAsync(targetUser.Id);
            }

            if (copyTeams)
            {
                await _permissionCopier.CopyTeamsLegacy(sourceUser, targetUser);
            }

            if (copyRoles)
            {
                await _permissionCopier.CopyRolesLegacy(sourceUser, targetUser);
            }

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\nAll operations completed");
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("\nPress any key to continue");
            Console.ResetColor();
            Console.ReadKey();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex.Message}");
            Console.WriteLine($"Stack Trace: {ex.StackTrace}");
        }
    }

    private async Task ConnectToDataverseAsync()
    {
        try
        {
            // Use the SessionManager to get the service client
            _serviceClient = SessionManager.Instance.GetClient();

            if (_serviceClient == null || !_serviceClient.IsReady)
            {
                throw new Exception($"Failed to connect. Error: {(_serviceClient?.LastError ?? "Unknown error")}");
            }

            this._userRetriever = new UserRetriever(_serviceClient);
            this._permissionCopier = new PermissionCopier(_serviceClient);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred while connecting to Dataverse: {ex.Message}");
            throw;
        }
    }

    private void DisplayUserInfo(Entity sourceUser, Entity targetUser)
    {
        string GetUserDisplayName(Entity user)
        {
            string fullName = user.Contains("fullname") ? user["fullname"].ToString() : "N/A";
            string domainName = user.Contains("domainname") ? user["domainname"].ToString() : "N/A";
            string username = domainName != "N/A" ? domainName.Split('@')[0] : "N/A";
            return $"{fullName} (Username: {username})";
        }
        Console.WriteLine("Source User: " + GetUserDisplayName(sourceUser));
        Console.WriteLine("Target User: " + GetUserDisplayName(targetUser));
        Console.WriteLine();
    }

    private async Task<bool> IsUserInitializedAsync(Entity user)
    {
        int conditionsMet = 0;
        // Check Business Unit
        if (user.Contains("businessunitid") && ((EntityReference)user["businessunitid"]).Name == "edpr")
        {
            conditionsMet++;
        }
        // Check Roles
        var roles = await _permissionCopier.GetUserRolesLegacyAsync(user.Id);
        if (roles.Entities.Count == 0)
        {
            conditionsMet++;
        }
        // Check Teams
        var teams = await _permissionCopier.GetUserTeamsLegacyAsync(user.Id);
        if (teams.Entities.Count == 0)
        {
            conditionsMet++;
        }
        if (conditionsMet >= 2)
        {
            Console.WriteLine("This is not a valid user to take permissions from.");
            return false;
        }
        return true;
    }

    private async Task<bool> PromptForPermissionCopy(string permissionType, Entity sourceUser, Entity targetUser)
    {
        Console.ForegroundColor = ConsoleColor.Blue;
        Console.WriteLine($"\n{permissionType}:");
        Console.ResetColor();
        var sourceInfo = await GetPermissionInfo(permissionType, sourceUser);
        var targetInfo = await GetPermissionInfo(permissionType, targetUser);
        if (permissionType == "Business Unit")
        {
            Console.WriteLine($"\nSource user: {sourceInfo[0]}");
            Console.WriteLine($"Target user: {targetInfo[0]}");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\n(Replacing the current user BU deletes its current roles)");
            Console.ResetColor();
        }
        else
        {
            DisplayComparison(permissionType, sourceInfo, targetInfo);
        }
        Console.ForegroundColor = ConsoleColor.Blue;
        Console.Write($"\nDo you want to copy the {permissionType.ToLower()} from the Source User to the Target User? (Y/N): \n");
        Console.ResetColor();
        return Console.ReadLine().Trim().ToUpper() == "Y";
    }

    private async Task<List<string>> GetPermissionInfo(string permissionType, Entity user)
    {
        switch (permissionType)
        {
            case "Business Unit":
                return new List<string> { user.Contains("businessunitid") ? ((EntityReference)user["businessunitid"]).Name : "N/A" };
            case "Teams":
                var teams = await _permissionCopier.GetUserTeamsLegacyAsync(user.Id);
                return teams.Entities.Select(t => t.GetAttributeValue<string>("name")).ToList();
            case "Roles":
                var roles = await _permissionCopier.GetUserRolesLegacyAsync(user.Id);
                return roles.Entities.Select(r => r.GetAttributeValue<string>("name")).ToList();
            default:
                return new List<string> { "Unknown" };
        }
    }

    private void DisplayComparison(string title, List<string> sourceItems, List<string> targetItems)
    {
        Console.WriteLine($"\n{title}:");
        Console.WriteLine("Source User".PadRight(50) + "Target User");
        Console.WriteLine(new string('-', 100));
        var commonItems = sourceItems.Intersect(targetItems).ToHashSet();
        int maxCount = Math.Max(sourceItems.Count, targetItems.Count);
        for (int i = 0; i < maxCount; i++)
        {
            string sourceItem = i < sourceItems.Count ? sourceItems[i] : "";
            string targetItem = i < targetItems.Count ? targetItems[i] : "";
            if (commonItems.Contains(sourceItem))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write(sourceItem.PadRight(50));
                Console.ResetColor();
            }
            else
            {
                Console.Write(sourceItem.PadRight(50));
            }
            if (commonItems.Contains(targetItem))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(targetItem);
                Console.ResetColor();
            }
            else
            {
                Console.WriteLine(targetItem);
            }
        }
    }
}