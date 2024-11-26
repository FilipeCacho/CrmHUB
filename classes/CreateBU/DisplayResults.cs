public static class ResultsDisplay
{
    public static void DisplayResults(List<BuCreationResult> buResults,
        List<TeamOperationResult> standardTeamResults,
        List<TeamOperationResult> proprietaryTeamResults)
    {
        if (buResults == null || !buResults.Any())
        {
            Console.WriteLine("\nNo Business Unit results to display.");
            return;
        }

        Console.WriteLine("\nProcess Results:");
        Console.WriteLine("------------------");

        foreach (var buResult in buResults)
        {
            // Display BU result
            Console.ForegroundColor = buResult.Exists ? ConsoleColor.Green : ConsoleColor.Red;
            Console.WriteLine($"BU: {buResult.BuName} - {(buResult.Exists ? "Exists/Created" : "Failed to create")}");
            Console.ResetColor();

            // Find corresponding team results, with null checking
            var standardTeam = standardTeamResults?.FirstOrDefault(tr => tr?.BuName == buResult.BuName);
            var proprietaryTeam = proprietaryTeamResults?.FirstOrDefault(tr => tr?.BuName == buResult.BuName);

            // Display Standard Team result
            Console.Write("  Standard Team: ");
            if (standardTeam != null)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(GetTeamStatusMessage(standardTeam));
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Failed to process");
            }
            Console.ResetColor();

            // Display Proprietary Team result
            Console.Write("  Proprietary Team: ");
            if (proprietaryTeam != null)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(GetTeamStatusMessage(proprietaryTeam));
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Failed to process");
            }
            Console.ResetColor();

            Console.WriteLine(); // Add blank line between BU entries
        }

        Console.WriteLine("Press any key to continue...");
        Console.ReadKey();
    }

    // Generate consistent status messages for teams
    private static string GetTeamStatusMessage(TeamOperationResult team)
    {
        if (!team.Exists) return "Failed to create";
        return team.WasUpdated ? "Updated" : "Already Exists";
    }
}