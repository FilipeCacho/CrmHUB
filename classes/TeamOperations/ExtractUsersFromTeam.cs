using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using ClosedXML.Excel;

public static class ExtractUsersFromTeam
{
    public static async Task<List<TransformedTeamData>> FormatTeamData()
    {
        List<TransformedTeamData> dynamicTeams = new();
        List<TeamRow> validTeams = ExcelReader.ValidateTeamsToCreate();

        if (validTeams is null || validTeams.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\nNo valid teams found to process.");
            Console.ResetColor();
            return null;
        }

        foreach (var team in validTeams)
        {
            string baseBuName = team.ColumnA;

            //add planner group if different than ZP1, append this right away to BU level 4 so all the other team names that are generated from it have the planner group
            if (team.ColumnC != "ZP1")
                baseBuName = $"{baseBuName} {team.ColumnC}";

     
            string buWithContrata = $"{baseBuName} Contrata";
            string teamName = $"Equipo contrata {baseBuName} Contrata";
            string fullBuName = $"{baseBuName} Contrata {team.ColumnB}";

            


            dynamicTeams.Add(new TransformedTeamData
            {
                Bu = buWithContrata,
                EquipaContrataContrata = teamName,
                FileName = baseBuName,
                FullBuName = fullBuName,
                               
            });

        }

        DisplayTransformedTeamData(dynamicTeams);

        if (!GetUserConfirmation())
        {
            return null;
        }

        var validatedTeams = await ValidateBusAndTeamsAsync(dynamicTeams);

        if (validatedTeams.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("\nNo valid Business Units or Teams found. Please check the datacenter XLS file and correct the information.");
            Console.ResetColor();
            return null;
        }

        return validatedTeams;
    }

    private static async Task<List<TransformedTeamData>> ValidateBusAndTeamsAsync(List<TransformedTeamData> teams)
    {
        var serviceClient = SessionManager.Instance.GetClient();
        List<TransformedTeamData> validTeams = new();

        Console.WriteLine("\nStarting validation process...");

        foreach (var team in teams)
        {
            string baseBuName = team.Bu;
            string teamName = team.EquipaContrataContrata;
            bool hasValidEntity = false;

            Console.WriteLine("\nValidating:");
            Console.WriteLine($"BU: {baseBuName}");
            Console.WriteLine($"Team: {teamName}");

            bool buExists = await CheckBusinessUnitExistsAsync(serviceClient, baseBuName);
            bool teamExists = await CheckTeamExistsAsync(serviceClient, teamName);

            if (!buExists)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"- Business Unit '{baseBuName}' not found in Dynamics.");
                Console.ResetColor();
            }
            else
            {
                hasValidEntity = true;
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"- Business Unit '{baseBuName}' found successfully.");
                Console.ResetColor();
            }

            if (!teamExists)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"- Team '{teamName}' not found in Dynamics.");
                Console.ResetColor();
            }
            else
            {
                hasValidEntity = true;
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"- Team '{teamName}' found successfully.");
                Console.ResetColor();
            }

            if (hasValidEntity)
            {
                validTeams.Add(team);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Added to processing list - will extract users from found entities");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Neither Business Unit nor Team were found - skipping");
                Console.ResetColor();
            }

            
        }

        if (validTeams.Any())
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\nValidation complete. Found {validTeams.Count} items with valid entities to process.");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("\nNo valid Business Units or Teams found at all. Please check the datacenter XLS file and correct the information.");
            Console.ResetColor();
        }

        return validTeams;
    }

    private static async Task<bool> CheckBusinessUnitExistsAsync(ServiceClient service, string businessUnitName)
    {
        var query = new QueryExpression("businessunit")
        {
            ColumnSet = new ColumnSet("businessunitid"),
            Criteria = new FilterExpression()
        };
        query.Criteria.AddCondition("name", ConditionOperator.Equal, businessUnitName);

        var results = await Task.Run(() => service.RetrieveMultiple(query));
        return results.Entities.Count > 0;
    }

    private static async Task<bool> CheckTeamExistsAsync(ServiceClient service, string teamName)
    {
        var query = new QueryExpression("team")
        {
            ColumnSet = new ColumnSet("teamid"),
            Criteria = new FilterExpression()
        };
        query.Criteria.AddCondition("name", ConditionOperator.Equal, teamName);

        var results = await Task.Run(() => service.RetrieveMultiple(query));
        return results.Entities.Count > 0;
    }

    public static async Task<List<BuUserDomains>> CreateExcel(List<TransformedTeamData> transformedBus)
    {
        List<BuUserDomains> result = new();
        var serviceClient = SessionManager.Instance.GetClient();

        try
        {
            string excelFolderPath = CreateExcelFolder();

            foreach (var team in transformedBus)
            {
                Console.WriteLine("\nProcessing team for Excel creation:");
                Console.WriteLine($"Bu: {team.Bu}");
                Console.WriteLine($"Team: {team.EquipaContrataContrata}");
                Console.WriteLine($"FileName: {team.FileName}");

                if (string.IsNullOrEmpty(team.FileName) || string.IsNullOrEmpty(team.EquipaContrataContrata))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Warning: Missing required data for team processing");
                    Console.WriteLine($"FileName present: {!string.IsNullOrEmpty(team.FileName)}");
                    Console.WriteLine($"Team name present: {!string.IsNullOrEmpty(team.EquipaContrataContrata)}");
                    Console.ResetColor();
                    continue;
                }

                var allUsers = await RetrieveAllUsersAsync(serviceClient, team);

                if (allUsers.Count == 0)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"\nNo users found for {team.FileName} - skipping Excel file creation");
                    Console.ResetColor();
                    continue;
                }

                var buUserDomains = CreateBuUserDomains(allUsers, team);
                result.Add(buUserDomains);

                await CreateExcelFileAsync(allUsers, team.FileName, excelFolderPath);
            }

            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("\n\rExcel file creation completed");
            if (result.Count > 0)
            {
                Console.WriteLine($"Created {result.Count} Excel files with users");
            }
            else
            {
                Console.WriteLine("No Excel files were created as no users were found");
            }
            Console.ResetColor();
            Console.WriteLine("\nPress any key to continue");
            Console.ReadKey();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex.Message}");
            Console.WriteLine($"Stack Trace: {ex.StackTrace}");
        }

        return result;
    }

    private static void DisplayTransformedTeamData(List<TransformedTeamData> dynamicTeams)
    {
        foreach (var team in dynamicTeams)
        {
            Console.Write("\nTransformed Team Data:\n");
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.Write("BU");
            Console.ResetColor();
            Console.WriteLine($": {team.Bu}");
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.Write("Equipa contrata Contrata");
            Console.ResetColor();
            Console.WriteLine($": {team.EquipaContrataContrata}");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Users will be stored in the file: {team.FileName}.xls\n");
            Console.ResetColor();
        }
    }

    private static bool GetUserConfirmation()
    {
        string input;
        do
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("Do you want to extract the users from these BUs and their corresponding general Team (the Contrata contrata Team)?");
            Console.WriteLine("Each BU-Team pair will have its own excel file inside the 'generated excels' folder in your Downloads folder\n");
            Console.WriteLine("The users from each BU and it's general team will be processed, duplicates will be removed and only active users will be included\n");
            Console.ResetColor();
            Console.Write("Enter your choice (y/n): ");
            input = Console.ReadLine()?.ToLower() ?? "n";

            if (input == "y")
            {
                return true;
            }
            else if (input == "n")
            {
                Console.ForegroundColor = ConsoleColor.Blue;
                Console.WriteLine("You chose No. Returning to the previous menu.");
                Console.ResetColor();
                return false;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Invalid input. Please try again.");
                Console.ResetColor();
                Console.WriteLine();
            }
        } while (input != "y" && input != "n");

        return false;
    }

    private static string CreateExcelFolder()
    {
        string downloadsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        string excelFolderPath = Path.Combine(downloadsFolder, "generated excels");
        Directory.CreateDirectory(excelFolderPath);
        return excelFolderPath;
    }

    private static async Task<List<UserData>> RetrieveAllUsersAsync(ServiceClient service, TransformedTeamData team)
    {
        List<UserData> allUsers = new();

        bool buExists = await CheckBusinessUnitExistsAsync(service, team.Bu);
        if (buExists)
        {
            List<UserData> usersFromBu = await RetrieveUsersFromBuAsync(service, team.Bu);
            Console.WriteLine($"Retrieved {usersFromBu.Count} users from BU {team.Bu}");
            allUsers.AddRange(usersFromBu);
        }

        bool teamExists = await CheckTeamExistsAsync(service, team.EquipaContrataContrata);
        if (teamExists)
        {
            List<UserData> usersFromTeam = await RetrieveUsersFromTeamAsync(service, team.EquipaContrataContrata);
            Console.WriteLine($"Retrieved {usersFromTeam.Count} users from Team {team.EquipaContrataContrata}");
            allUsers.AddRange(usersFromTeam);
        }

        var uniqueUsers = allUsers.GroupBy(u => u.YomiFullName)
                                 .Select(g => g.First())
                                 .ToList();

        Console.WriteLine($"Total unique users: {uniqueUsers.Count}");
        return uniqueUsers;
    }

    private static BuUserDomains CreateBuUserDomains(List<UserData> allUsers, TransformedTeamData team)
    {
        return new BuUserDomains
        {
            NewCreatedPark = $"Equipo contrata {team.FullBuName}".Trim(),
            UserDomains = allUsers.Select(u => u.DomainName).ToList()
        };
    }

    private static async Task<List<UserData>> RetrieveUsersFromBuAsync(ServiceClient service, string businessUnitName)
    {
        var query = new QueryExpression("businessunit")
        {
            ColumnSet = new ColumnSet("businessunitid"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("name", ConditionOperator.Equal, businessUnitName)
                }
            }
        };

        var buResult = await Task.Run(() => service.RetrieveMultiple(query));
        if (buResult.Entities.Count == 0)
        {
            Console.WriteLine($"Business Unit '{businessUnitName}' not found.");
            return new List<UserData>();
        }

        var businessUnitId = buResult.Entities[0].Id;

        var userQuery = new QueryExpression("systemuser")
        {
            ColumnSet = new ColumnSet("yomifullname", "domainname", "isdisabled"),
            Criteria = new FilterExpression()
        };

        userQuery.Criteria.AddCondition("businessunitid", ConditionOperator.Equal, businessUnitId);
        userQuery.Criteria.AddCondition("isdisabled", ConditionOperator.Equal, false);

        var results = await Task.Run(() => service.RetrieveMultiple(userQuery));

        return results.Entities.Select(e => new UserData
        {
            YomiFullName = e.GetAttributeValue<string>("yomifullname"),
            DomainName = e.GetAttributeValue<string>("domainname"),
            BusinessUnit = businessUnitName,
            Team = ""
        }).ToList();
    }

    private static async Task<List<UserData>> RetrieveUsersFromTeamAsync(ServiceClient service, string teamName)
    {
        if (string.IsNullOrEmpty(teamName))
        {
            Console.WriteLine("Warning: Team name is null or empty.");
            return new List<UserData>();
        }

        var teamQuery = new QueryExpression("team")
        {
            ColumnSet = new ColumnSet("teamid"),
            Criteria = new FilterExpression()
        };
        teamQuery.Criteria.AddCondition("name", ConditionOperator.Equal, teamName);

        var teamResults = await Task.Run(() => service.RetrieveMultiple(teamQuery));

        if (teamResults.Entities.Count == 0)
        {
            Console.WriteLine($"Team '{teamName}' not found.");
            return new List<UserData>();
        }

        var teamId = teamResults.Entities[0].Id;

        var userQuery = new QueryExpression("systemuser")
        {
            ColumnSet = new ColumnSet("yomifullname", "domainname", "isdisabled", "businessunitid"),
            Criteria = new FilterExpression()
        };

        userQuery.Criteria.AddCondition("isdisabled", ConditionOperator.Equal, false);

        var teamLink = userQuery.AddLink("teammembership", "systemuserid", "systemuserid");
        teamLink.LinkCriteria.AddCondition("teamid", ConditionOperator.Equal, teamId);

        var buLink = userQuery.AddLink("businessunit", "businessunitid", "businessunitid");
        buLink.Columns = new ColumnSet("name");
        buLink.EntityAlias = "bu";

        var results = await Task.Run(() => service.RetrieveMultiple(userQuery));

        return results.Entities.Select(e => new UserData
        {
            YomiFullName = e.GetAttributeValue<string>("yomifullname"),
            DomainName = e.GetAttributeValue<string>("domainname"),
            BusinessUnit = e.GetAttributeValue<AliasedValue>("bu.name")?.Value?.ToString() ?? "Unknown",
            Team = teamName
        }).ToList();
    }

    private static async Task CreateExcelFileAsync(List<UserData> users, string fileName, string folderPath)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            Console.WriteLine("Warning: Filename is empty");
            return;
        }

        string filePath = Path.Combine(folderPath, $"{fileName}.xlsx");

        await Task.Run(() =>
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Users");

            // Add headers
            worksheet.Cell(1, 1).Value = "Yomi Full Name";
            worksheet.Cell(1, 2).Value = "Domain Name";
            worksheet.Cell(1, 3).Value = "Business Unit";
            worksheet.Cell(1, 4).Value = "Team";

            // Add data
            for (int i = 0; i < users.Count; i++)
            {
                worksheet.Cell(i + 2, 1).Value = users[i].YomiFullName;
                worksheet.Cell(i + 2, 2).Value = users[i].DomainName;
                worksheet.Cell(i + 2, 3).Value = users[i].BusinessUnit;
                worksheet.Cell(i + 2, 4).Value = users[i].Team;
            }

            // Auto-fit columns
            worksheet.Columns().AdjustToContents();

            // Save the workbook
            workbook.SaveAs(filePath);
        });

        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write($"\nExcel file created successfully at ");
        Console.ResetColor();
        Console.WriteLine(filePath);
    }
}