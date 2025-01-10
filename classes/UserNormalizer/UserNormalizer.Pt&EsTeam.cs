using Microsoft.Xrm.Sdk;


public sealed partial class UserNormalizerV2
{
    private async Task AddPortugalSpainTeams(Entity user)
    {
        ArgumentNullException.ThrowIfNull(user);

        try
        {
            if (!user.Contains("businessunitid"))
            {
                Console.WriteLine("User does not have a business unit assigned, skipping PT/ES team assignment.");
                return;
            }

            var businessUnit = (EntityReference)user["businessunitid"];
            var buName = businessUnit.Name;

            if (ShouldAddPtEsTeams(buName))
            {
                await EnsureUserHasTeams(user, CodesAndRoles.EUDefaultTeamForPortugueseAndSpanishUsers);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"\nSuccessfully added Portugal/Spain teams for business unit: {buName}");
                Console.ResetColor();
            }
            else
            {
                Console.WriteLine($"\nBusiness unit {buName} does not qualify for Portugal/Spain teams");
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\nError adding Portugal/Spain teams: {ex.Message}");
            Console.ResetColor();
            throw;
        }
    }

    private static bool ShouldAddPtEsTeams(string buName)
    {
        if (string.IsNullOrEmpty(buName))
        {
            return false;
        }

        // Check for direct country names
        if (buName.Contains("Portugal", StringComparison.OrdinalIgnoreCase) ||
            buName.Contains("Spain", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Check for country codes in hyphenated format
        if (buName.Contains('-'))
        {
            var countryCode = buName[(buName.IndexOf('-') + 1)..];
            return countryCode.StartsWith("PT", StringComparison.OrdinalIgnoreCase) ||
                   countryCode.StartsWith("ES", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}