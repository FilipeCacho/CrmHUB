using System;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using OfficeOpenXml;
using System.Threading;
using System.Linq;

public class ExcelReader : IDisposable
{
    private static readonly object excelLock = new object();
    private static string filePath;
    public static int CurrentEnvironment { get; set; } = 3; // Default to PRD (3)
    private bool disposed = false;
    private static readonly SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);

    static ExcelReader()
    {
        InitializeExcelPackage();
    }

    private static void InitializeExcelPackage()
    {
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

        if (!ExcelTemplateManager.CheckAndCreateExcelFile())
        {
            throw new FileNotFoundException("Failed to initialize Excel file.");
        }

        filePath = ExcelTemplateManager.GetExcelFilePath();
    }

    public static List<TeamRow> ReadBuInfoExcel()
    {
        if (!semaphore.Wait(TimeSpan.FromSeconds(30), CancellationToken.None))
        {
            throw new TimeoutException("Timeout waiting for Excel access");
        }

        try
        {
            lock (excelLock)
            {
                List<TeamRow> rows = new List<TeamRow>();
                using (var package = new ExcelPackage(new FileInfo(filePath)))
                {
                    ExcelWorksheet worksheet = package.Workbook.Worksheets["Create Teams"];
                    if (worksheet == null)
                    {
                        throw new ArgumentException("The specified tab 'Create Teams' does not exist");
                    }

                    int rowCount = worksheet.Dimension?.Rows ?? 0;
                    for (int row = 2; row <= rowCount; row++)
                    {
                        if (!IsRowEmpty(worksheet, row))
                        {
                            rows.Add(ProcessRow(worksheet, row));
                        }
                    }

                    package.Workbook.Dispose();
                    return rows;
                }
            }
        }
        finally
        {
            semaphore.Release();
            ForceGarbageCollection();
        }
    }

    public static List<TeamRow> ValidateTeamsToCreate()
    {
        lock (excelLock)
        {
            try
            {
                using (var package = new ExcelPackage(new FileInfo(filePath)))
                {
                    List<TeamRow> validRows = new List<TeamRow>();
                    List<TeamRow> createTeamData = ReadBuInfoExcel();

                    for (int i = 0; i < createTeamData.Count; i++)
                    {
                        var row = createTeamData[i];
                        row.ColumnA = row.ColumnA.ToUpper();
                        row.ColumnB = row.ColumnB.ToUpper();
                        row.ColumnC = row.ColumnC.ToUpper();

                        bool isValid = true;
                        string errorMessage = $"Row {i + 2}: ";

                        string[] parts = row.ColumnA.Split('-');
                        string countryCode = parts[1];

                        if (CodesAndRoles.CountryCodeEU.Contains(countryCode))
                        {
                            isValid = ValidateEUTeam(row, ref errorMessage);
                            if (isValid) validRows.Add(row);
                            else LogError(errorMessage);
                        }
                        else if (CodesAndRoles.CountryCodeNA.Contains(countryCode))
                        {
                            LogWarning($"Row {i + 2}: Found a BU with a US country code. No action taken.");
                        }
                        else
                        {
                            LogError($"Row {i + 2}: Found a BU with an invalid country code. No action taken.");
                        }
                    }

                    package.Workbook.Dispose();
                    return validRows;
                }
            }
            finally
            {
                ForceGarbageCollection();
            }
        }
    }

    private static bool ValidateEUTeam(TeamRow row, ref string errorMessage)
    {
        bool isValid = true;

        if (string.IsNullOrWhiteSpace(row.ColumnA) || string.IsNullOrWhiteSpace(row.ColumnB) ||
            string.IsNullOrWhiteSpace(row.ColumnC) || string.IsNullOrWhiteSpace(row.ColumnD) ||
            string.IsNullOrWhiteSpace(row.ColumnE))
        {
            errorMessage += "All cells from A-E of this row must contain text. ";
            isValid = false;
        }

        if (!Regex.IsMatch(row.ColumnA, @"^\d-[A-Z]{2}-[A-Z0-9]{3}-\d{2}$"))
        {
            errorMessage += "Column A must be in the format '0-XX-XXX-00' where X is a letter and 0 is any digit. ";
            isValid = false;
        }

        if (row.ColumnB.Length != 8)
        {
            errorMessage += "Column B must contain exactly 8 characters. ";
            isValid = false;
        }

        if (!Regex.IsMatch(row.ColumnC, @"^ZP[A-Za-z0-9]$"))
        {
            errorMessage += "Column C must start with 'ZP' followed by a single character. ";
            isValid = false;
        }

        if (row.ColumnD.Length != 4)
        {
            errorMessage += "Column D must contain exactly 4 characters. ";
            isValid = false;
        }

        return isValid;
    }

    private static TeamRow ProcessRow(ExcelWorksheet worksheet, int row)
    {
        return new TeamRow
        {
            ColumnA = worksheet.Cells[row, 1].Text.Trim(),
            ColumnB = ProcessSingleWordColumn(worksheet.Cells[row, 2].Text, "B", row),
            ColumnC = ProcessSingleWordColumn(worksheet.Cells[row, 3].Text, "C", row),
            ColumnD = ProcessSingleWordColumn(worksheet.Cells[row, 4].Text, "D", row),
            ColumnE = worksheet.Cells[row, 5].Text.Trim()
        };
    }

    private static string ProcessSingleWordColumn(string cellValue, string columnName, int rowNumber)
    {
        string processed = cellValue.Trim();

        if (string.IsNullOrWhiteSpace(processed))
        {
            LogWarning($"Empty value in column {columnName} at row {rowNumber}. Using default value.");
            return $"Default{columnName}";
        }

        if (processed.Contains(" "))
        {
            string[] words = processed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            LogWarning($"Multiple words found in column {columnName} at row {rowNumber}. Using only the first word.");
            return words[0];
        }

        return processed;
    }

    private static bool IsRowEmpty(ExcelWorksheet worksheet, int row)
    {
        for (int col = 1; col <= 5; col++)
        {
            if (!string.IsNullOrWhiteSpace(worksheet.Cells[row, col].Text))
            {
                return false;
            }
        }
        return true;
    }

    public static List<string>? ValidateNasDownloads()
    {
        if (!semaphore.Wait(TimeSpan.FromSeconds(30), CancellationToken.None))
        {
            throw new TimeoutException("Timeout waiting for Excel access");
        }
        try
        {
            lock (excelLock)
            {
                using (var package = new ExcelPackage(new FileInfo(filePath)))
                {
                    ExcelWorksheet? worksheet = package.Workbook.Worksheets["NAS Downloads"];
                    if (worksheet == null)
                    {
                        Console.WriteLine("Error: Worksheet 'NAS Downloads' not found.");
                        Console.ReadLine();
                        return null;
                    }

                    // Validate header
                    if (worksheet.Cells[1, 1].Text != "Functional Location (level 3)")
                    {
                        Console.WriteLine("Error: Invalid header in NAS Downloads worksheet.");
                        Console.ReadLine();
                        return null;
                    }

                    int rowCount = worksheet.Dimension?.Rows ?? 0;
                    List<string> downloads = new List<string>();

                    // Process rows
                    for (int row = 2; row <= rowCount; row++)
                    {
                        string location = worksheet.Cells[row, 1].Text.Trim();

                        // Skip completely empty rows
                        if (string.IsNullOrWhiteSpace(location))
                            continue;

                        // Count dashes in the location string
                        int dashCount = location.Count(c => c == '-');
                        if (dashCount != 2)
                        {
                            Console.WriteLine($"Error: Functional location in row {row} must contain exactly 2 dashes. Found: {dashCount} dashes in {location}");
                            Console.ReadKey();
                            return null;
                        }

                        // Validate location format
                        var parts = location.Split('-');
                        if (parts[0] != "0" || parts[1].Length != 2 ||
                            (!CodesAndRoles.CountryCodeNA.Contains(parts[1]) &&
                             !CodesAndRoles.CountryCodeEU.Contains(parts[1])))
                        {
                            Console.WriteLine($"Error: Invalid functional location format in row {row}: {location}");
                            Console.ReadKey();
                            return null;
                        }

                        downloads.Add(location);
                    }

                    // Additional validation: Check for empty worksheet
                    if (downloads.Count == 0)
                    {
                        Console.WriteLine("Error: No valid data found in the worksheet.");
                        Console.ReadKey();
                        return null;
                    }

                    return downloads;
                }
            }
        }
        finally
        {
            semaphore.Release();
            ForceGarbageCollection();
        }
    }

    private static void LogWarning(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    private static void LogError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    private static void ForceGarbageCollection()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposed)
        {
            if (disposing)
            {
                semaphore.Dispose();
                ForceGarbageCollection();
            }
            disposed = true;
        }
    }

    ~ExcelReader()
    {
        Dispose(false);
    }

    public static void Cleanup()
    {
        try
        {
            ForceGarbageCollection();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during cleanup: {ex.Message}");
        }
    }
}

public class TeamRow
{
    public string ColumnA { get; set; }
    public string ColumnB { get; set; }
    public string ColumnC { get; set; }
    public string ColumnD { get; set; }
    public string ColumnE { get; set; }
}

public struct NasDownload
{
    public string FunctionalLocation { get; set; }
    public int Year { get; set; }
}