using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using ClosedXML.Excel;
using System.Net;

public static class ExtractUsersFromTeam
{
    public static async Task<List<TransformedTeamData>> FormatTeamData()
    {
        List<TransformedTeamData> dynamicTeams = new List<TransformedTeamData>();
        List<TeamRow> validTeams = ExcelReader.ValidateTeamsToCreate();

        if (validTeams.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\nNo valid teams found to process.");
            Console.ResetColor();
            return null;
        }

        foreach (var team in validTeams)
        {
            string bu = FormatBusinessUnitName(team);
            string buWithoutContractor = bu[..bu.LastIndexOf(' ')];

            dynamicTeams.Add(new TransformedTeamData
            {
                Bu = buWithoutContractor,
                EquipaContrataContrata = $"Equipo contrata {buWithoutContractor}".Trim(),
                FileName = team.ColumnA,
                FullBuName = bu
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
        bool hasInvalidTeams = false;

        foreach (var team in teams)
        {
            bool buExists = await CheckBusinessUnitExistsAsync(serviceClient, team.Bu);
            bool teamExists = await CheckTeamExistsAsync(serviceClient, team.EquipaContrataContrata);

            if (!buExists)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\nBusiness Unit '{team.Bu}' not found in Dynamics.");
                Console.ResetColor();
                hasInvalidTeams = true;
            }

            if (!teamExists)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\nTeam '{team.EquipaContrataContrata}' not found in Dynamics.");
                Console.ResetColor();
                hasInvalidTeams = true;
            }

            if (buExists && teamExists)
            {
                validTeams.Add(team);
            }
        }

        if (hasInvalidTeams)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\nProcessing will continue with {validTeams.Count} valid team(s).");
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
                var allUsers = await RetrieveAllUsersAsync(serviceClient, team);
                var buUserDomains = CreateBuUserDomains(allUsers, team);
                result.Add(buUserDomains);

                await CreateExcelFileAsync(allUsers, team.FileName, excelFolderPath);
            }

            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("\n\rAll excel files created, press any key to continue");
            Console.ResetColor();
            Console.ReadKey();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex.Message}");
            Console.WriteLine($"Stack Trace: {ex.StackTrace}");
        }

        return result;
    }

    private static string FormatBusinessUnitName(TeamRow team)
    {
        string bu = team.ColumnA;
        if (team.ColumnC != "ZP1")
        {
            bu += $" {team.ColumnC}";
        }
        return $"{bu} Contrata {team.ColumnB}";
    }

    private static void DisplayTransformedTeamData(List<TransformedTeamData> dynamicTeams)
    {
        foreach (var team in dynamicTeams)
        {
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("\nTransformed Team Data:");
            Console.ResetColor();
            Console.WriteLine($"bu: {team.Bu}");
            Console.WriteLine($"Equipa contrata Contrata: {team.EquipaContrataContrata}");
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

    private static async Task<List<UserData>> RetrieveAllUsersAsync(ServiceClient serviceClient, TransformedTeamData team)
    {
        List<UserData> usersFromBu = await RetrieveUsersFromBuAsync(serviceClient, team.Bu);
        List<UserData> usersFromTeam = await RetrieveUsersFromTeamAsync(serviceClient, team.EquipaContrataContrata);

        return usersFromBu.Concat(usersFromTeam)
                         .GroupBy(u => u.YomiFullName)
                         .Select(g => g.First())
                         .ToList();
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
                Conditions = { new ConditionExpression("name", ConditionOperator.Equal, businessUnitName) }
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

        var userResults = await Task.Run(() => service.RetrieveMultiple(userQuery));

        return userResults.Entities.Select(e => new UserData
        {
            YomiFullName = e.GetAttributeValue<string>("yomifullname"),
            DomainName = e.GetAttributeValue<string>("domainname"),
            BusinessUnit = e.GetAttributeValue<AliasedValue>("bu.name")?.Value?.ToString() ?? "Unknown",
            Team = teamName
        }).ToList();
    }

    public static async Task CreateExcelFileAsync(List<UserData> users, string fileName, string folderPath)
    {
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
        Console.Write(filePath);
    }
}