using Microsoft.Xrm.Sdk;


public sealed partial class UserNormalizerV2
{
    private async Task GiveResco(Entity user, string regionChoice)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrEmpty(regionChoice);

        while (true)
        {
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("\nDo you want to give RESCO access and Role to this user? (y/n)");
            Console.ResetColor();

            var response = await Task.Run(() => Console.ReadLine());
            var answer = response?.Trim().ToLower();

            if (answer == "n")
            {
                break;
            }

            if (answer == "y")
            {
                await ProcessRescoAccess(user, regionChoice);
                break;
            }

            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\nInvalid choice. Please enter 'y' or 'n'");
            Console.ResetColor();
        }
    }

    private async Task ProcessRescoAccess(Entity user, string regionChoice)
    {
        try
        {
            switch (regionChoice)
            {
                case "1": // EU
                    await EnsureUserHasTeams(user, CodesAndRoles.RescoTeamEU);
                    await EnsureUserHasRoles(user, CodesAndRoles.RescoRole);

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("\nRESCO role and team were given to the EU user");
                    Console.WriteLine("(Don't forget to give a RESCO license in Woodford, this code can't do that)");
                    Console.ResetColor();
                    break;

                case "2": // NA
                    await EnsureUserHasTeams(user, CodesAndRoles.RescoTeamNA);
                    await EnsureUserHasRoles(user, CodesAndRoles.RescoRole);

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("\nRESCO role and team were given to the NA user");
                    Console.WriteLine("(Don't forget to give a RESCO license in Woodford, this code can't do that)");
                    Console.ResetColor();
                    break;

                default:
                    throw new ArgumentException($"Invalid region choice: {regionChoice}", nameof(regionChoice));
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\nError assigning RESCO access: {ex.Message}");
            Console.ResetColor();
            throw;
        }
    }
}