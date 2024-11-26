public static class ResultsDisplay
{
    public static void DisplayResults(List<BuCreationResult> buResults,
        List<TeamOperationResult> standardTeamResults,
        List<TeamOperationResult> proprietaryTeamResults)
    {
        Console.WriteLine("\nProcess Results:");
        Console.WriteLine("------------------");
        foreach (var buResult in buResults)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"BU: {buResult.BuName} - {(buResult.Exists ? "Exists/Created" : "Failed to create")}");
            Console.ResetColor();

            var standardTeam = standardTeamResults.FirstOrDefault(tr => tr.BuName == buResult.BuName);
            var proprietaryTeam = proprietaryTeamResults.FirstOrDefault(tr => tr.BuName == buResult.BuName);

            if (standardTeam != null)
                Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  Standard Team: {(standardTeam.Exists ? (standardTeam.WasUpdated ? "Updated" : "Already Exists") : "Created")}");
            Console.ResetColor();

            if (proprietaryTeam != null)
                Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  Proprietary Team: {(proprietaryTeam.Exists ? (proprietaryTeam.WasUpdated ? "Updated" : "Already Exists") : "Created")} \n");
            Console.ResetColor();
        }
        Console.WriteLine("Press any key 2 times (except 'q' to continue)...");
        Console.ReadKey();
    }
}