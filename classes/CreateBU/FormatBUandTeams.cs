using System;
using System.Collections.Generic;

public class FormatBUandTeams
{
    public static List<TransformedTeamData> FormatTeamData()
    {
        List<TransformedTeamData> dynamicTeams = new();
        List<TeamRow> validTeams = ExcelReader.ValidateTeamsToCreate();
        if (validTeams?.Count > 0)
        {
            foreach (var team in validTeams)
            {
                // Get the base BU name and add Contrata
                string baseBuName = team.ColumnA;  
                string buWithContrata = $"{baseBuName} Contrata"; 

                // Get the full BU name for reference 
                string fullBuName = FormatBusinessUnitName(team); 

                dynamicTeams.Add(new TransformedTeamData
                {
                    Bu = buWithContrata,  
                    EquipaContrata = $"Equipo contrata {baseBuName}",
                    EquipaEDPR = $"EDPR: {baseBuName}",
                    ContractorCode = team.ColumnB,
                    PlannerGroup = team.ColumnC ?? "ZP1",
                    PlannerCenterName = team.ColumnD,
                    PrimaryCompany = team.ColumnA,
                    Contractor = team.ColumnE,
                    // Add Contrata here
                    EquipaContrataContrata = $"Equipo contrata {baseBuName} Contrata".Trim(), 
                    FileName = team.ColumnA,
                    FullBuName = fullBuName
                });
            }

            foreach (var team in dynamicTeams)
            {
                Console.ForegroundColor = ConsoleColor.Blue;
                Console.WriteLine("\nTransformed Team Data:");
                Console.ResetColor();
                Console.WriteLine($"BU to search: {team.Bu}"); 
                Console.WriteLine($"Team to search: {team.EquipaContrataContrata}");  
                Console.WriteLine($"FileName: {team.FileName}");
            }

            string input;
            do
            {
                Console.ForegroundColor = ConsoleColor.Blue;
                Console.WriteLine($"\nFound {validTeams.Count} valid team(s):\n");
                Console.ResetColor();
                Console.WriteLine("Do you want to use these valid teams?");
                Console.Write("\nEnter your choice (y/n): ");
                input = Console.ReadLine()?.ToLower() ?? "n";
                if (input == "y")
                {
                    return dynamicTeams;
                }
                else if (input == "n")
                {
                    Console.WriteLine("You chose No. Returning to the previous menu.");
                    return null;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("Invalid input. Please try again.");
                    Console.ResetColor();
                }
            } while (input != "y" && input != "n");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("\nNo valid teams found to create.");
            Console.ResetColor();
        }
        return null;
    }

    private static string FormatBusinessUnitName(TeamRow team)
    {
        if (team == null) return string.Empty;

        string bu = team.ColumnA;
        if (!string.IsNullOrEmpty(team.ColumnC) && team.ColumnC != "ZP1")
        {
            bu += $" {team.ColumnC}";
        }
        return $"{bu} Contrata {team.ColumnB}";
    }
}