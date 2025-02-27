using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Text.RegularExpressions;
using System.Linq;


    public class JiraRitmLogger : IDisposable
    {
        private readonly HttpClient _client;
        private readonly string _jiraBaseUrl;
        private readonly string _personalAccessToken;
        private bool _isDisposed;

        public JiraRitmLogger()
        {
            var (jiraUrl, pat) = ExcelReader.ReadJiraCredentials();

            if (string.IsNullOrWhiteSpace(jiraUrl) || string.IsNullOrWhiteSpace(pat))
            {
                throw new ArgumentException("Jira URL and Personal Access Token must be configured in the Excel file 'Login' worksheet (cells A2 and B2)");
                throw new ArgumentException("To get the key you login into JIRA and go to your user profile and go to the 'personal access tokens', that values is what you place in cell B2");
            }

            _jiraBaseUrl = jiraUrl.TrimEnd('/');
            _personalAccessToken = pat;
            _client = new HttpClient();
            _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_personalAccessToken}");
        }

        public async Task<bool> TestAuthentication()
        {
            try
            {
                var response = await _client.GetAsync($"{_jiraBaseUrl}/rest/api/2/myself");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private string ExtractRitmNumber(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return null;

            // Remove any whitespace
            input = input.Trim();

            // If it starts with RITM (case insensitive), remove it
            var match = Regex.Match(input, @"^(?:RITM)?(\d+)$", RegexOptions.IgnoreCase);

            if (!match.Success)
                return null;

            return match.Groups[1].Value;
        }

        public async Task LogHoursMenu()
        {
            if (!await TestAuthentication())
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Failed to authenticate with Jira. Please check your credentials in the Excel file.");
                Console.ResetColor();
                Console.WriteLine("Press any key to return to main menu...");
                Console.ReadKey();
                return;
            }

            while (true)
            {
                Console.Clear();
                var hoursLogged = await GetTodayAlreadyLoggedHours();
                Console.WriteLine($"Hours logged today: {hoursLogged}h");
                Console.WriteLine($"Date: {DateTime.Now:dd/MMM/yy}");
                Console.WriteLine("\nEnter RITM number (with or without 'RITM' prefix, or 'exit' to return to main menu):");
                Console.WriteLine("Examples: '1234567' or 'RITM1234567'");

                var ritmInput = Console.ReadLine();
                if (ritmInput?.ToLower() == "exit")
                    break;

                var ritmNumber = ExtractRitmNumber(ritmInput);
                if (string.IsNullOrWhiteSpace(ritmNumber))
                {
                    Console.WriteLine("Invalid RITM number format. Press any key to continue...");
                    Console.ReadKey();
                    continue;
                }

                // Search for the issue using RITM number
                var actualIssue = await SearchForRitm(ritmNumber);
                if (actualIssue == null)
                {
                    Console.WriteLine($"RITM{ritmNumber} not found. Press any key to continue...");
                    Console.ReadKey();
                    continue;
                }

                Console.WriteLine($"\nFound RITM: RITM{ritmNumber}");
                Console.WriteLine($"Jira Issue: {actualIssue.Key} - {actualIssue.Fields.Summary}");
                Console.WriteLine("\nEnter time to log (e.g., 1h, 30m):");
                var timeInput = Console.ReadLine();

                if (!ValidateTimeInput(timeInput, out var timeInHours))
                {
                    Console.WriteLine("Invalid time format. Use format like '1h' or '30m'. Press any key to continue...");
                    Console.ReadKey();
                    continue;
                }

                if (hoursLogged + timeInHours > 8)
                {
                    Console.WriteLine("Cannot log more than 8 hours per day. Press any key to continue...");
                    Console.ReadKey();
                    continue;
                }

                var success = await LogTime(actualIssue.Key, timeInput);
                if (success)
                    Console.WriteLine("Time logged successfully!");
                else
                    Console.WriteLine("Failed to log time.");

                Console.WriteLine("\nPress any key to continue...");
                Console.ReadKey();
            }
        }

        private async Task<JiraIssue> SearchForRitm(string ritmNumber)
        {
            try
            {
                // Search using JQL to find the issue with the RITM number in its summary or description
                var jql = Uri.EscapeDataString($"text ~ \"RITM{ritmNumber}\" ORDER BY created DESC");
                var response = await _client.GetAsync($"{_jiraBaseUrl}/rest/api/2/search?jql={jql}");

                if (!response.IsSuccessStatusCode)
                    return null;

                var content = await response.Content.ReadAsStringAsync();
                var searchResult = JsonConvert.DeserializeObject<JiraSearchResult>(content);

                // Return the first matching issue (most recently created)
                return searchResult?.Issues?.FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        private async Task<decimal> GetTodayAlreadyLoggedHours()
        {
            try
            {
                var today = DateTime.Now.ToString("yyyy-MM-dd");

                var jql = Uri.EscapeDataString($"worklogDate = {today} AND worklogAuthor = currentUser()");
                var response = await _client.GetAsync($"{_jiraBaseUrl}/rest/api/2/search?jql={jql}&fields=worklog,summary&expand=changelog");

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Debug - Failed to get worklogs. Status: {(int)response.StatusCode}");
                    return 0;
                }

                var content = await response.Content.ReadAsStringAsync();
                var searchResult = JsonConvert.DeserializeObject<JiraWorklogSearchResult>(content);

                if (searchResult?.Issues == null)
                    return 0;

                decimal totalHours = 0;
                foreach (var issue in searchResult.Issues)
                {
                    if (issue.Fields?.Worklog?.Worklogs != null)
                    {
                        // Filter worklogs for today only
                        var todayWorklogs = issue.Fields.Worklog.Worklogs
                            .Where(w => DateTime.Parse(w.Started).Date == DateTime.Now.Date);

                        totalHours += todayWorklogs.Sum(w => w.TimeSpentSeconds) / 3600.0m;
                    }
                }

                return Math.Round(totalHours, 2);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Debug - Error getting logged hours: {ex.Message}");
                return 0;
            }
        }

        private async Task<bool> LogTime(string issueKey, string timeSpent)
        {
            var worklog = new
            {
                timeSpent,
                started = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fff+0000")
            };

            var content = new StringContent(
                JsonConvert.SerializeObject(worklog),
                Encoding.UTF8,
                "application/json");

            try
            {
                var response = await _client.PostAsync(
                    $"{_jiraBaseUrl}/rest/api/2/issue/{issueKey}/worklog",
                    content);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private bool ValidateTimeInput(string input, out decimal hours)
        {
            hours = 0;
            if (string.IsNullOrWhiteSpace(input))
                return false;

            var match = Regex.Match(input, @"^(\d+)(h|m)$");
            if (!match.Success)
                return false;

            var value = decimal.Parse(match.Groups[1].Value);
            var unit = match.Groups[2].Value;

            if (unit == "h")
                hours = value;
            else if (unit == "m")
                hours = value / 60m;

            return true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                {
                    _client?.Dispose();
                }
                _isDisposed = true;
            }
        }

        ~JiraRitmLogger()
        {
            Dispose(false);
        }
    }

    public class JiraIssue
    {
        public string Key { get; set; }
        public JiraIssueFields Fields { get; set; }
    }

    public class JiraIssueFields
    {
        public string Summary { get; set; }
    }

    public class JiraSearchResult
    {
        public JiraIssue[] Issues { get; set; }
    }

    public class TempoWorklog
    {
        public int TimeSpentSeconds { get; set; }
    }

    public class JiraWorklogSearchResult
    {
        public JiraIssueWithWorklog[] Issues { get; set; }
    }

    public class JiraIssueWithWorklog
    {
        public JiraWorklogFields Fields { get; set; }
    }

    public class JiraWorklogFields
    {
        public Worklog Worklog { get; set; }
    }

    public class Worklog
    {
        public WorklogEntry[] Worklogs { get; set; }
    }

    public class WorklogEntry
    {
        public int TimeSpentSeconds { get; set; }
        public string Started { get; set; }
    }

    public class TempoSearchResponse
    {
        public TempoWorklogResult[] Results { get; set; }
    }

    public class TempoWorklogResult
    {
        public int TimeSpentSeconds { get; set; }
    }

    public class TempoWorklogLegacy
    {
        public int TimeSpentSeconds { get; set; }
    }
