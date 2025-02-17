using System;
using System.Collections.Generic;


    public class FormatBUandTeams
    {
        public static List<TransformedTeamData> FormatTeamData()
        {
            List<TransformedTeamData> dynamicTeams = new List<TransformedTeamData>();
            List<TeamRow> validTeams = ExcelReader.ValidateTeamsToCreate();
            if (validTeams.Count > 0)
            {
                foreach (var team in validTeams)
                {
                    string bu = team.ColumnA;
                    if (team.ColumnC != "ZP1")
                    {
                        bu += " " + team.ColumnC;
                    }
                    bu += " Contrata " + team.ColumnB;
                    TransformedTeamData transformedTeam = new TransformedTeamData
                    {
                        Bu = bu,
                        EquipaContrata = "Equipo contrata " + bu,
                        EquipaEDPR = "EDPR: " + bu,
                        ContractorCode = team.ColumnB,
                        PlannerGroup = team.ColumnC,
                        PlannerCenterName = team.ColumnD,
                        PrimaryCompany = team.ColumnA,
                        Contractor = team.ColumnE
                        

                    };
                    dynamicTeams.Add(transformedTeam);
                }
                foreach (var team in dynamicTeams)
                {
                    Console.ForegroundColor = ConsoleColor.Blue;
                    Console.WriteLine("\nTransformed Team Data:");
                    Console.ResetColor();
                    Console.WriteLine($"BU: {team.Bu}");
                    Console.WriteLine($"Equipa Contrata: {team.EquipaContrata}");
                    Console.WriteLine($"Equipa EDPR: {team.EquipaEDPR}");
                    Console.WriteLine($"PlannerGroup: {team.PlannerGroup}");
                    Console.WriteLine($"Planner Group Code: {team.PlannerCenterName}");
                    Console.WriteLine($"Contractor : {team.Contractor}");
                    Console.WriteLine($"Contractor Code : {team.ContractorCode}");
                    Console.ResetColor();
                }
                string input;
                do
                {
                    Console.ForegroundColor = ConsoleColor.Blue;
                    Console.WriteLine($"\nFound {validTeams.Count} valid team(s):\n");
                    Console.ResetColor();
                    Console.WriteLine("Do you want to use these valid teams?");
                    Console.Write("\nEnter your choice (y/n): ");
                    input = Console.ReadLine().ToLower();
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
    }
