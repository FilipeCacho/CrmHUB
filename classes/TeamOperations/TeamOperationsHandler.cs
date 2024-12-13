using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;



    public interface ITeamOperationsHandler
    {
        Task ExtractUsersFromTeams();
        Task AssignTeamsToUsers();
    }

public class TeamOperationsHandler : ITeamOperationsHandler
{
    private readonly ITeamUserService _teamUserService;
    private readonly IConsoleUI _consoleUI;
    private List<BuUserDomains>? _buUserDomainsList;

    public TeamOperationsHandler(ITeamUserService teamUserService, IConsoleUI consoleUI)
    {
        _teamUserService = teamUserService ?? throw new ArgumentNullException(nameof(teamUserService));
        _consoleUI = consoleUI ?? throw new ArgumentNullException(nameof(consoleUI));
    }

    public async Task ExtractUsersFromTeams()
    {
        try
        {
            // Format team data with proper transformation
            var transformedTeams = await FormatTeamDataAsync();
            if (transformedTeams == null || !transformedTeams.Any())
            {
                _consoleUI.DisplayWarning("No teams found to process.");
                return;
            }

            // Validate the transformed data before proceeding
            foreach (var team in transformedTeams)
            {
                if (string.IsNullOrEmpty(team.FileName) || string.IsNullOrEmpty(team.EquipaContrataContrata))
                {
                    _consoleUI.DisplayError($"Invalid team data: {team.Bu}",
                        new Exception("FileName or EquipaContrataContrata is missing"));
                    return;
                }
            }

            // Extract users and store the result
            var (extractedUsers, messages) = await _teamUserService.ExtractUsersFromTeamsAsync(transformedTeams);
            _buUserDomainsList = extractedUsers;

            // Only display error messages if they exist
            if (messages.Any())
            {
                _consoleUI.DisplayMessages(messages);
            }

          
        }
        catch (Exception ex)
        {
            _consoleUI.DisplayError("Error during user extraction", ex);
        }
    }

    private async Task<List<TransformedTeamData>> FormatTeamDataAsync()
    {
        var transformedTeams = FormatBUandTeams.FormatTeamData();

        if (transformedTeams == null || !transformedTeams.Any())
        {
            return new List<TransformedTeamData>();
        }

        return transformedTeams;
    }

    public async Task AssignTeamsToUsers()
    {
        try
        {
            if (_buUserDomainsList == null || !_buUserDomainsList.Any())
            {
                _consoleUI.DisplayWarning("No users available to process. Please run option 2 first.");
                return;
            }

            var results = await _teamUserService.AssignTeamsToUsersAsync(_buUserDomainsList);

            if (results?.Any() == true)
            {
                _consoleUI.DisplayMessages(new[]
                {
                    $"Successfully processed {results.Count} users.",
                    "Check the previous messages for details of each operation."
                });
            }

            var operationMessages = _teamUserService.GetLastOperationMessages();
            if (operationMessages.Any())
            {
                _consoleUI.DisplayMessages(operationMessages);
            }
        }
        catch (Exception ex)
        {
            _consoleUI.DisplayError("Error during team assignment", ex);
        }
    }
}

public interface IConsoleUI
    {
        void DisplayMessages(IEnumerable<string> messages);
        void DisplayExtractedUsers(List<BuUserDomains> users);
        void DisplayWarning(string message);
        void DisplayError(string message, Exception ex);
    }

    public class ConsoleUI : IConsoleUI
    {
        public void DisplayMessages(IEnumerable<string> messages)
        {
            if (messages == null) return;

            foreach (var message in messages)
            {
                Console.WriteLine(message);
            }
            WaitForKeyPress();
        }

        public void DisplayExtractedUsers(List<BuUserDomains> users)
        {
            if (users == null) return;

            Console.Clear();
            Console.WriteLine("Processed Users (only active and distinct) from each BU:\n");

            foreach (var buUsers in users)
            {
                Console.WriteLine($"{buUsers.NewCreatedPark.Replace("Equipo contrata", "").Trim()}:");
                foreach (var userDomain in buUsers.UserDomains)
                {
                    Console.WriteLine($"  {userDomain}");
                }
                Console.WriteLine();
            }
            WaitForKeyPress();
        }

        public void DisplayWarning(string message)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\nWarning: {message}");
            Console.ResetColor();
            WaitForKeyPress();
        }

        public void DisplayError(string message, Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\nError: {message}");
            Console.WriteLine($"Details: {ex.Message}");
            Console.ResetColor();
            WaitForKeyPress();
        }

        private void WaitForKeyPress()
        {
            Console.WriteLine("\nPress any key to continue...");
            Console.ReadKey();
        }
    }

public interface ITeamUserService
{
    Task<(List<BuUserDomains> Users, List<string> Messages)> ExtractUsersFromTeamsAsync(List<TransformedTeamData> teams);
    Task<List<ProcessedUser>> AssignTeamsToUsersAsync(List<BuUserDomains> userDomains);
    List<string> GetLastOperationMessages();
}

public class TeamUserService : ITeamUserService
{
    private readonly List<string> _operationMessages = new List<string>();

    public async Task<(List<BuUserDomains> Users, List<string> Messages)> ExtractUsersFromTeamsAsync(List<TransformedTeamData> teams)
    {
        _operationMessages.Clear();
        var users = await ExtractUsersFromTeam.CreateExcel(teams);

        if (users != null)
        {
           // foreach (var buUsers in users)
            //{
              //  _operationMessages.Add($"Extracted {buUsers.UserDomains.Count} users from {buUsers.NewCreatedPark}");
            //}
            return (users, new List<string>(_operationMessages));
        }

        return (new List<BuUserDomains>(), new List<string>(_operationMessages));
    }

    public async Task<List<ProcessedUser>> AssignTeamsToUsersAsync(List<BuUserDomains> userDomains)
    {
        _operationMessages.Clear();

        if (userDomains == null || userDomains.Count == 0)
        {
            _operationMessages.Add("No user domains provided for team assignment.");
            return new List<ProcessedUser>();
        }

        try
        {
            var processor = new AssignNewTeamToUser(userDomains);
            var results = await processor.ProcessUsersAsync();

            if (results != null)
            {
                foreach (var result in results)
                {
                    _operationMessages.Add($"Processed user {result.UserDomain} for {result.AssignedPark}");
                }
                return results;
            }

            return new List<ProcessedUser>();
        }
        catch (Exception ex)
        {
            _operationMessages.Add($"Error assigning teams: {ex.Message}");
            return new List<ProcessedUser>();
        }
    }

    public List<string> GetLastOperationMessages()
    {
        return new List<string>(_operationMessages);
    }
}