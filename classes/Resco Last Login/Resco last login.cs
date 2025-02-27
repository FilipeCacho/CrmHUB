using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using ClosedXML.Excel;
using System.Text;
using CsvHelper;
using System.Globalization;
using System.Security;
using Microsoft.Xrm.Sdk.Messages;
using CsvHelper.Configuration;
using System.Security.Cryptography;
using DocumentFormat.OpenXml.Spreadsheet;
using System.Formats.Asn1;

public class RescoUserData
{
    public string FullName { get; set; }
    public string Email { get; set; }
    public string BusinessUnit { get; set; }
    public DateTime? LastLogin { get; set; }
    public bool IsDisabled { get; set; }
    public string DeviceName { get; set; }
    public string DeviceOS { get; set; }
}

public sealed class RescoLicenseProcessor
{
    private ServiceClient _serviceClient;
    private CancellationTokenSource _cancellationTokenSource;
    private int _totalUsers;
    private int _processedUsers;
    private string _selectedFilePath;
    private const int BATCH_SIZE = 50;
    private const int MAX_RETRIES = 3;
    private readonly IProgress<int> _progress;

    public RescoLicenseProcessor()
    {
        _serviceClient = SessionManager.Instance.GetClient();

        _progress = new Progress<int>(percent =>
        {
            Console.Write($"\r\nProgress: {percent}% ({_processedUsers}/{_totalUsers})");
        });
    }

    public async Task ProcessRescoLicensesAsync()
    {
        Console.WriteLine("This code will extract from the DB the last Resco login for users with Resco license");
        Console.WriteLine("\nIt will ask for a file, this file is obtained from woodford, by pressing the filter button on the right side of the search box in mobile users");
        Console.WriteLine("\nThen select only Product-Inspections and License-Assigned and then press EXPORT USERS (the option with exactly this name) on the top left");
        Console.WriteLine("\nThis is the file you pass on to this program");

        try
        {
            _cancellationTokenSource = new CancellationTokenSource();

            await ShowFileDialogAsync();
            if (string.IsNullOrEmpty(_selectedFilePath))
            {
                Console.WriteLine("No file selected.");
                return;
            }

            Console.WriteLine("Reading CSV file...");
            var userData = await ReadCsvDataAsync(_selectedFilePath);
            if (!userData.Any())
            {
                Console.WriteLine("No data found in CSV file.");
                return;
            }

            _totalUsers = userData.Count;
            Console.WriteLine($"Processing {_totalUsers} users...");

            _ = ListenForEscapeKeyAsync();

            var lastLoginData = await GetLastLoginDataAsync(userData, _cancellationTokenSource.Token);
            var mergedData = MergeUserData(userData, lastLoginData);

            await GenerateExcelFileAsync(mergedData);

            Console.WriteLine("\nProcess completed successfully!");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("\nOperation cancelled by user.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nError: {ex.Message}");
            Console.WriteLine($"Stack Trace: {ex.StackTrace}");
        }
        finally
        {
            _cancellationTokenSource?.Dispose();
        }
    }

    private string BuildLatestAuditFetchXml(string fullName)
    {
        var fetchXml = $@"
        <fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='true' top='1'>
          <entity name='resco_mobileaudit'>
            <attribute name='createdon' />
            <attribute name='createdbyname' />
            <filter>
              <condition attribute='createdbyname' operator='eq' value='{SecurityElement.Escape(fullName)}' />
              <condition attribute='resco_entityname' operator='eq' value='synchronization_finish' />
            </filter>
            <order attribute='createdon' descending='true' />
          </entity>
        </fetch>".Trim();

        return fetchXml;
    }

    private string BuildDeviceInfoFetchXml(string fullName)
    {
        var fetchXml = $@"
<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='true' top='1'>
  <entity name='resco_mobiledevice'>
    <attribute name='createdbyname' />
    <attribute name='resco_devicename' />
    <attribute name='resco_deviceos' />
    <filter>
      <condition attribute='createdbyname' operator='eq' value='{SecurityElement.Escape(fullName)}' />
    </filter>
    <order attribute='createdon' descending='true' />
  </entity>
</fetch>".Trim();
        return fetchXml;
    }

    private async Task<List<RescoUserData>> ReadCsvDataAsync(string filePath)
    {
        var userData = new List<RescoUserData>();

        await Task.Run(() =>
        {
            using (var reader = new StreamReader(filePath, Encoding.UTF8))
            using (var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                MissingFieldFound = null,
                TrimOptions = TrimOptions.Trim
            }))
            {
                csv.Read();
                csv.ReadHeader();

                while (csv.Read())
                {
                    try
                    {
                        var email = csv.GetField<string>("Primary Email")?.Trim();

                        if (string.IsNullOrWhiteSpace(email))
                            continue;

                        if (email.Length > 32 && email.Contains("@"))
                        {
                            var atIndex = email.IndexOf("@");
                            if (atIndex > 32)
                            {
                                email = email.Substring(atIndex - 20);
                                var beforeAt = email.Substring(0, email.IndexOf("@"));
                                var afterAt = email.Substring(email.IndexOf("@"));
                                email = beforeAt + afterAt;
                            }
                        }

                        var record = new RescoUserData
                        {
                            FullName = csv.GetField<string>("Full Name")?.Trim(),
                            Email = email.ToLower(),
                            BusinessUnit = csv.GetField<string>("Business Unit")?.Trim()
                        };

                        if (!string.IsNullOrWhiteSpace(record.Email))
                        {
                            userData.Add(record);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"\nError reading CSV row: {ex.Message}");
                        Console.WriteLine($"Current row: {csv.GetField<string>("Full Name")}, {csv.GetField<string>("Primary Email")}");
                    }
                }
            }
        });

        Console.WriteLine($"Successfully read {userData.Count} valid user records from CSV.");
        return userData;
    }

    private async Task<Dictionary<string, (DateTime LastLogin, bool IsDisabled, string DeviceName, string DeviceOS)>> GetLastLoginDataAsync(List<RescoUserData> userData, CancellationToken cancellationToken)
    {
        var results = new Dictionary<string, (DateTime LastLogin, bool IsDisabled, string DeviceName, string DeviceOS)>();
        _processedUsers = 0;

        for (int i = 0; i < userData.Count; i += BATCH_SIZE)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var batch = userData.Skip(i).Take(BATCH_SIZE).ToList();
            var batchResults = await ProcessBatchAsync(batch, cancellationToken);

            foreach (var kvp in batchResults)
            {
                results[kvp.Key] = kvp.Value;
            }

            _processedUsers += batch.Count;
            var progressPercentage = (_processedUsers * 100) / _totalUsers;
            (_progress as IProgress<int>)?.Report(progressPercentage);
        }

        return results;
    }

    private async Task<Dictionary<string, (DateTime LastLogin, bool IsDisabled, string DeviceName, string DeviceOS)>> ProcessBatchAsync(List<RescoUserData> batch, CancellationToken cancellationToken)
    {
        var batchResults = new Dictionary<string, (DateTime LastLogin, bool IsDisabled, string DeviceName, string DeviceOS)>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var user in batch)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                if (string.IsNullOrEmpty(user.FullName))
                    continue;

                // First get the latest audit record
                var auditFetchXml = BuildLatestAuditFetchXml(user.FullName);
                var auditQuery = new FetchExpression(auditFetchXml);
                var auditResult = await ExecuteRetrieveMultipleWithRetryAsync(auditQuery, cancellationToken);

                DateTime? lastLogin = null;
                if (auditResult.Entities.Any())
                {
                    lastLogin = auditResult.Entities[0].GetAttributeValue<DateTime>("createdon");
                }

                // Then get the device info
                var deviceFetchXml = BuildDeviceInfoFetchXml(user.FullName);
                var deviceQuery = new FetchExpression(deviceFetchXml);
                var deviceResult = await ExecuteRetrieveMultipleWithRetryAsync(deviceQuery, cancellationToken);

                string deviceName = null;
                string deviceOS = null;
                if (deviceResult.Entities.Any())
                {
                    var deviceEntity = deviceResult.Entities[0];
                    deviceName = deviceEntity.GetAttributeValue<string>("resco_devicename");
                    deviceOS = deviceEntity.GetAttributeValue<string>("resco_deviceos");
                }

                if (lastLogin.HasValue)
                {
                    batchResults[user.Email] = (lastLogin.Value, false, deviceName, deviceOS);
                    Console.WriteLine($"Found data for {user.FullName}: Last Login: {lastLogin}, Device: {deviceName}, OS: {deviceOS}");
                }
            }

            return batchResults;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n=== ERROR PROCESSING BATCH ===");
            Console.WriteLine($"Exception Type: {ex.GetType().Name}");
            Console.WriteLine($"Message: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"Inner Exception: {ex.InnerException.Message}");
            }
            throw;
        }
    }

    private async Task<EntityCollection> ExecuteRetrieveMultipleWithRetryAsync(FetchExpression query, CancellationToken cancellationToken)
    {
        for (int retryCount = 0; retryCount <= MAX_RETRIES; retryCount++)
        {
            try
            {
                Console.WriteLine($"\n=== Query Attempt {retryCount + 1} of {MAX_RETRIES + 1} ===");
                var startTime = DateTime.Now;

                var result = await Task.Run(() =>
                {
                    Console.WriteLine("Executing RetrieveMultiple...");
                    return _serviceClient.RetrieveMultiple(query);
                }, cancellationToken);

                var duration = DateTime.Now - startTime;
                Console.WriteLine($"Query execution completed in {duration.TotalSeconds:F2} seconds");
                Console.WriteLine($"Retrieved {result.Entities.Count} records");

                return result;
            }
            catch (Exception ex) when (!(ex is OperationCanceledException) && retryCount < MAX_RETRIES)
            {
                Console.WriteLine($"\n=== Query Attempt {retryCount + 1} Failed ===");
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine($"Exception Type: {ex.GetType().Name}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner Exception: {ex.InnerException.Message}");
                }

                var delay = TimeSpan.FromSeconds(Math.Pow(2, retryCount));
                Console.WriteLine($"Waiting {delay.TotalSeconds} seconds before retry...");
                await Task.Delay(delay, cancellationToken);
            }
        }

        throw new Exception("Failed to execute query after maximum retries");
    }

    private Task ShowFileDialogAsync()
    {
        var tcs = new TaskCompletionSource<object>();

        var thread = new Thread(() =>
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                using (OpenFileDialog openFileDialog = new OpenFileDialog())
                {
                    openFileDialog.Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*";
                    openFileDialog.FilterIndex = 1;
                    openFileDialog.Title = "Select Resco Users CSV File";

                    if (openFileDialog.ShowDialog() == DialogResult.OK)
                    {
                        _selectedFilePath = openFileDialog.FileName;
                        Console.WriteLine($"Selected file: {_selectedFilePath}");
                    }
                }
                tcs.SetResult(null);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return tcs.Task;
    }

    private async Task ListenForEscapeKeyAsync()
    {
        await Task.Run(() =>
        {
            Console.WriteLine("Press ESC to cancel the operation...");
            while (true)
            {
                if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Escape)
                {
                    _cancellationTokenSource.Cancel();
                    break;
                }
                Thread.Sleep(100);
            }
        });
    }

    private List<RescoUserData> MergeUserData(List<RescoUserData> userData,
     Dictionary<string, (DateTime LastLogin, bool IsDisabled, string DeviceName, string DeviceOS)> lastLoginData)
    {
        foreach (var user in userData)
        {
            if (!string.IsNullOrEmpty(user.Email) && lastLoginData.TryGetValue(user.Email.ToLower(), out var data))
            {
                user.LastLogin = data.LastLogin;
                user.IsDisabled = data.IsDisabled;
                user.DeviceName = data.DeviceName;
                user.DeviceOS = data.DeviceOS;
            }
        }
        return userData;
    }

    private string DetermineRegion(string businessUnit)
    {
        if (string.IsNullOrEmpty(businessUnit))
            return string.Empty;

        // First check for direct NA or EUR values
        if (businessUnit == "NA")
            return "NA";
        if (businessUnit == "EUR")
            return "EU";

        // Then check the format 0-XX-...
        var parts = businessUnit.Split('-');
        if (parts.Length >= 2 && parts[0] == "0")
        {
            var countryCode = parts[1];

            if (CodesAndRoles.CountryCodeNA.Contains(countryCode))
                return "NA";

            if (CodesAndRoles.CountryCodeEU.Contains(countryCode))
                return "EU";
        }

        return string.Empty;
    }

    private async Task GenerateExcelFileAsync(List<RescoUserData> userData)
    {
        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var timestamp = DateTime.Now.ToString("dd_MM_yyyy");
        var filePath = Path.Combine(desktopPath, $"resco_licenses_{timestamp}.xlsx");

        await Task.Run(() =>
        {
            using (var workbook = new XLWorkbook())
            {
                var mainSheet = workbook.Worksheets.Add("Resco Licenses");
                var disabledSheet = workbook.Worksheets.Add("Users to Consider Disable");

                var badColor = XLColor.FromHtml("#FFC7CE");
                var goodColor = XLColor.FromHtml("#C6EFCE");

                void AddHeaders(IXLWorksheet sheet)
                {
                    sheet.Cell(1, 1).Value = "Full Name";
                    sheet.Cell(1, 2).Value = "Email";
                    sheet.Cell(1, 3).Value = "Business Unit";
                    sheet.Cell(1, 4).Value = "Region";
                    sheet.Cell(1, 5).Value = "Last Login";
                    sheet.Cell(1, 6).Value = "Device Name";
                    sheet.Cell(1, 7).Value = "Device OS";
                    sheet.Cell(1, 8).Value = "Disabled Status";

                    var headerRow = sheet.Row(1);
                    headerRow.Style.Font.Bold = true;
                    headerRow.Style.Fill.BackgroundColor = XLColor.LightGray;
                }

                AddHeaders(mainSheet);
                AddHeaders(disabledSheet);

                int disabledRow = 2;

                void PopulateRow(IXLWorksheet sheet, int rowNum, RescoUserData user, string region)
                {
                    var rowRange = sheet.Range(rowNum, 1, rowNum, 8);

                    if (!user.LastLogin.HasValue)
                    {
                        rowRange.Style.Fill.BackgroundColor = badColor;
                        rowRange.Style.Font.FontColor = XLColor.FromHtml("#9C0006");
                    }
                    else
                    {
                        rowRange.Style.Fill.BackgroundColor = goodColor;
                        rowRange.Style.Font.FontColor = XLColor.FromHtml("#006100");
                    }

                    sheet.Cell(rowNum, 1).Value = user.FullName;
                    sheet.Cell(rowNum, 2).Value = user.Email;
                    sheet.Cell(rowNum, 3).Value = user.BusinessUnit;
                    sheet.Cell(rowNum, 4).Value = region;

                    if (user.LastLogin.HasValue)
                    {
                        sheet.Cell(rowNum, 5).Value = user.LastLogin.Value;
                        sheet.Cell(rowNum, 5).Style.DateFormat.Format = "dd-mm-yyyy";
                    }

                    sheet.Cell(rowNum, 6).Value = user.DeviceName ?? "";
                    sheet.Cell(rowNum, 7).Value = user.DeviceOS ?? "";
                    sheet.Cell(rowNum, 8).Value = user.IsDisabled ? "Yes" : "No";
                }

                for (int i = 0; i < userData.Count; i++)
                {
                    var row = i + 2;
                    var user = userData[i];
                    var region = DetermineRegion(user.BusinessUnit);

                    // Add to main sheet
                    PopulateRow(mainSheet, row, user, region);

                    // If user is disabled, add to disabled sheet
                    if (user.IsDisabled)
                    {
                        PopulateRow(disabledSheet, disabledRow, user, region);
                        disabledRow++;
                    }
                }

                void FormatSheet(IXLWorksheet sheet, int lastRow)
                {
                    sheet.Columns().AdjustToContents();

                    if (lastRow > 1)
                    {
                        var usedRange = sheet.Range(1, 1, lastRow, 8);
                        usedRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                        usedRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                    }
                }

                FormatSheet(mainSheet, userData.Count + 1);
                FormatSheet(disabledSheet, disabledRow - 1);

                try
                {
                    workbook.SaveAs(filePath);
                }
                catch (IOException)
                {
                    var retryTimestamp = DateTime.Now.ToString("dd_MM_yyyy_HHmmss");
                    var retryFilePath = Path.Combine(desktopPath, $"resco_licenses_{retryTimestamp}.xlsx");
                    workbook.SaveAs(retryFilePath);
                    filePath = retryFilePath;
                }
            }
        });

        Console.WriteLine($"\n\rExcel file created: {filePath}");
    }
}