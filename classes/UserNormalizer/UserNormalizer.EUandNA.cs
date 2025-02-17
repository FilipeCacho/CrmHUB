using Microsoft.Xrm.Sdk;


public sealed partial class UserNormalizerV2
{
    private async Task NormalizeUser(Entity user, string regionChoice)
    {
        ArgumentNullException.ThrowIfNull(user);

        switch (regionChoice)
        {
            case "1":
                await NormalizeEUUserAsync(user);
                break;
            case "2":
                await NormalizeNAUserAsync(user);
                break;
            default:
                throw new ArgumentException($"Invalid region choice: {regionChoice}", nameof(regionChoice));
        }
    }

    private async Task NormalizeEUUserAsync(Entity user)
    {
        var username = user.GetAttributeValue<string>("domainname")?.Split('@')[0] ?? string.Empty;
        var isInternal = IsInternalUser(username);

        var (rolesToAdd, teamsToAdd) = isInternal
            ? (CodesAndRoles.EUDefaultRolesForInternalUsers, CodesAndRoles.EUDefaultTeamsForInternalUsers)
            : (CodesAndRoles.EUDefaultRolesForExternalUsers, CodesAndRoles.EUDefaultTeamsForExternalUsers);

        try
        {
            await EnsureUserHasRoles(user, rolesToAdd);
            await EnsureUserHasTeams(user, teamsToAdd);
            await UpdateUserRegion(user, CodesAndRoles.EURegion);
            await AddPortugalSpainTeams(user);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\nSuccessfully normalized EU user: {username}");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\nError normalizing EU user {username}: {ex.Message}");
            Console.ResetColor();
            throw;
        }
    }

    private async Task NormalizeNAUserAsync(Entity user)
    {
        var username = user.GetAttributeValue<string>("domainname")?.Split('@')[0] ?? string.Empty;
        var isInternal = IsInternalUser(username);

        try
        {
            if (isInternal)
            {
                await EnsureUserHasRoles(user, CodesAndRoles.NADefaultRolesForInternalUser);
                await UpdateUserRegion(user, CodesAndRoles.NARegion);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"\nSuccessfully normalized NA user: {username}");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("User does not match NA internal pattern.");
                Console.ResetColor();
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\nError normalizing NA user {username}: {ex.Message}");
            Console.ResetColor();
            throw;
        }
    }
}