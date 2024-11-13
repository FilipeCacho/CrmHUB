using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Extensions.Logging;

public class SessionManager : IDisposable
{
    // Singleton instance and thread-safety lock
    private static readonly SessionManager _instance = CreateInstance();
    private static readonly object _lock = new object();
    private readonly ILogger<SessionManager> _logger;

    private volatile bool _isDisposed;

    // Main service client for Dynamics 365 connection
    private ServiceClient? _serviceClient;

    // Track connection state to avoid unnecessary reconnection attempts
    private volatile bool _isConnected;

    // Timestamp of last connection attempt for throttling
    private DateTime _lastConnectionAttempt = DateTime.MinValue;

    // Controls connection throttling to prevent too frequent reconnection attempts
    private static readonly TimeSpan ConnectionThrottle = TimeSpan.FromSeconds(30);

    // Maximum number of retry attempts for credential authentication
    private const int MaxCredentialRetries = 2;

    private const string TokenCacheFolder = "CrmHub";
    private const string TokenCacheName = "TokenCache";

    

    private SessionManager(ILogger<SessionManager> logger)
    {
        _logger = logger;
    }

    private static SessionManager CreateInstance()
    {
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder
                .AddConsole()
                .AddDebug()
                .SetMinimumLevel(LogLevel.Information);
        });

        var logger = loggerFactory.CreateLogger<SessionManager>()
            ?? throw new InvalidOperationException("Failed to create logger instance");

        return new SessionManager(logger);
    }

    // Returns the singleton instance of SessionManager
    public static SessionManager Instance => _instance;

    // Gets or initializes the Dynamics 365 service client, ensuring thread-safe access
    public ServiceClient GetClient()
    {
        // Synchronization lock for thread-safe connection management
        lock (_lock)
        {
            if (!_isConnected || _serviceClient == null)
            {
                ConnectToService();
            }
            // Validate service client initialization before returning
            return _serviceClient ?? throw new InvalidOperationException("Service client is not initialized");
        }
    }

    public void Disconnect()
    {
        lock (_lock)
        {
            _logger.LogInformation("Disconnecting from Dynamics 365");
            _serviceClient?.Dispose();
            _serviceClient = null;
            _isConnected = false;
        }
    }

    // Attempts to establish a connection and returns success status without throwing exceptions
    public bool TryConnect()
    {
        try
        {
            ConnectToService();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to Dynamics 365");
            return false;
        }
    }

    // Verifies if the current connection is active and valid
    public bool CheckConnection()
    {
        lock (_lock)
        {
            if (!_isConnected || _serviceClient == null)
                return false;

            try
            {
                // Only perform actual connection check if sufficient time has passed
                if (DateTime.Now - _lastConnectionAttempt >= ConnectionThrottle)
                {
                    IOrganizationService orgService = _serviceClient;

                    // Execute WhoAmI request to verify connection is active
                    _ = ((WhoAmIResponse)orgService.Execute(new WhoAmIRequest())).UserId;
                    _lastConnectionAttempt = DateTime.Now;
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Connection check failed");
                _isConnected = false;
                return false;
            }
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;

        lock (_lock)
        {
            if (_isDisposed) return;
            _isDisposed = true;

            _logger.LogInformation("Disposing SessionManager");
            Disconnect();
        }

        GC.SuppressFinalize(this);
    }

    private void ConnectToService()
    {
        lock (_lock)
        {
            if (_serviceClient?.IsReady == true && _isConnected)
            {
                _logger.LogInformation("Using existing connection");
                return;
            }

            if (DateTime.Now - _lastConnectionAttempt < ConnectionThrottle)
            {
                _logger.LogInformation("Connection attempt throttled");
                return;
            }

            _lastConnectionAttempt = DateTime.Now;

            try
            {
                Console.WriteLine("\nAttempting to use existing authentication token...");
                if (TryTokenAuthentication())
                {
                    Console.WriteLine("Successfully connected using existing token.");
                    return;
                }

                var (username, password) = CredentialManager.LoadCredentials();
                bool isFirstTimeSetup = string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password);

                if (isFirstTimeSetup)
                {
                    HandleFirstTimeSetup();
                    return;
                }

                if (TryStoredCredentialsWithRetry(username!, password!))
                {
                    return;
                }

                HandleCredentialFailure();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Connection process failed");
                CleanupFailedConnection();
                throw;
            }
        }
    }

    // Attempts to authenticate using cached token before trying other methods
    private bool TryTokenAuthentication()
    {
        lock (_lock)
        {
            try
            {
                string tokenCachePath = GetTokenCachePath();

                // Skip if no token cache exists
                if (!Directory.Exists(tokenCachePath) || !Directory.GetFiles(tokenCachePath).Any())
                {
                    return false;
                }

                string tokenConnectionString = BuildTokenConnectionString(tokenCachePath);

                // Dispose of any existing client before creating a new one
                _serviceClient?.Dispose();
                _serviceClient = new ServiceClient(tokenConnectionString);

                if (!_serviceClient.IsReady)
                {
                    return false;
                }

                // Verify connection and handle MFA if needed
                VerifyConnection();
                _isConnected = true;
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogInformation($"Token authentication failed: {ex.Message}");
                CleanupFailedConnection();
                return false;
            }
        }
    }

    // Attempts to connect using stored credentials with retry logic
    private bool TryStoredCredentialsWithRetry(string username, string password)
    {
        // Needs lock for ServiceClient operations
        lock (_lock)
        {
            int retryCount = 0;
            bool authSuccess = false;

            while (!authSuccess && retryCount <= MaxCredentialRetries)
            {
                try
                {
                    Console.WriteLine($"\nAttempting to connect with stored credentials (Attempt {retryCount + 1}/{MaxCredentialRetries + 1})...");
                    string connectionString = BuildCredentialConnectionString(username, password);

                    _serviceClient?.Dispose();
                    _serviceClient = new ServiceClient(connectionString);

                    if (!_serviceClient.IsReady)
                    {
                        throw new Exception(_serviceClient.LastError ?? "Service client initialization failed");
                    }

                    VerifyConnection();
                    _isConnected = true;
                    authSuccess = true;
                    Console.WriteLine("Successfully connected with stored credentials.");
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Authentication attempt {retryCount + 1} failed");
                    CleanupFailedConnection();
                    retryCount++;

                    if (retryCount <= MaxCredentialRetries)
                    {
                        Console.WriteLine($"\nAuthentication failed: {ex.Message}");
                        Console.WriteLine("Would you like to retry with the same credentials? (y/n)");
                        if (Console.ReadLine()?.ToLower() != "y")
                        {
                            break;
                        }
                    }
                }
            }
            return false;
        }
    }

    // Handles first-time setup process including MFA authentication
    private void HandleFirstTimeSetup()
    {
        lock (_lock)
        {
            Console.Clear();
            Console.WriteLine("=== First Time Setup ===");
            Console.WriteLine("Welcome! You'll need to set up your credentials for Dynamics 365.");
            Console.WriteLine("Note: Multi-factor authentication (MFA) will be required.\n");

            while (true)
            {
                try
                {
                    var (username, password) = PromptForCredentials();
                    string connectionString = BuildCredentialConnectionString(username, password);

                    Console.WriteLine("\nInitiating connection and MFA authentication...");
                    Console.WriteLine("A browser window will open for MFA verification.");

                    _serviceClient?.Dispose();
                    _serviceClient = new ServiceClient(connectionString);

                    if (!_serviceClient.IsReady)
                    {
                        throw new Exception(_serviceClient.LastError ?? "Service client initialization failed");
                    }

                    VerifyConnection();
                    CredentialManager.SaveCredentials(username, password);
                    _isConnected = true;
                    Console.WriteLine("\nSetup completed successfully!");
                    return;
                }
                catch (Exception ex) // The ex variable was not being used
                {
                    Console.WriteLine($"\nSetup failed: {ex.Message}"); // Add this line to use the exception
                    Console.WriteLine("Would you like to try again? (y/n)");
                    if (Console.ReadLine()?.ToLower() != "y")
                    {
                        throw new Exception("First time setup abandoned by user");
                    }
                    CleanupFailedConnection();
                }
            }
        }
    }

    // Manages authentication failures and provides user options for resolution
    private void HandleCredentialFailure()
    {
        lock (_lock)
        {

            while (true)
            {
                Console.WriteLine("\nWould you like to:");
                Console.WriteLine("1. Enter new credentials");
                Console.WriteLine("2. Exit");
                Console.Write("\nChoice (1-2): ");

                string? choice = Console.ReadLine();
                if (choice == "1")
                {
                    try
                    {
                        var (username, password) = PromptForCredentials();

                        // Create new connection with these credentials
                        string connectionString = BuildCredentialConnectionString(username, password);

                        Console.WriteLine("\nAttempting to connect with new credentials...");
                        Console.WriteLine("A browser window may open for MFA verification.");

                        // Dispose of any existing client
                        _serviceClient?.Dispose();
                        _serviceClient = new ServiceClient(connectionString);

                        if (!_serviceClient.IsReady)
                        {
                            throw new Exception(_serviceClient.LastError ?? "Service client initialization failed");
                        }

                        // Verify the connection works
                        VerifyConnection();
                        _isConnected = true;

                        // Only save credentials after successful connection
                        CredentialManager.SaveCredentials(username, password);
                        return;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"\nConnection failed with new credentials: {ex.Message}");
                        Console.WriteLine("Would you like to try again? (y/n)");
                        if (Console.ReadLine()?.ToLower() != "y")
                        {
                            throw new Exception("Authentication process cancelled by user");
                        }
                        CleanupFailedConnection();
                    }
                }
                else if (choice == "2")
                {
                    throw new Exception("Authentication process cancelled by user");
                }
                else
                {
                    Console.WriteLine("Invalid choice. Please try again.");
                }
            }
        }
    }

   

    // Builds connection string for token-based authentication
    private string BuildTokenConnectionString(string tokenCachePath) =>
        $"AuthType=OAuth;" +
        $"Url={EnvironmentsDetails.DynamicsUrl};" +
        $"AppId={EnvironmentsDetails.AppId};" +
        $"RedirectUri={EnvironmentsDetails.RedirectUri};" +
        $"TokenCacheStorePath={tokenCachePath};" +
        $"RequireNewInstance=False;" +  // Changed to False to reuse token
        $"LoginPrompt=Never;" +         // Changed to Never to avoid unnecessary prompts
        $"UseWebApi=true;" +
        $"InteractiveLogin=true";

    // Builds connection string for credential-based authentication
    private string BuildCredentialConnectionString(string username, string password) =>
        $"AuthType=OAuth;" +
        $"Url={EnvironmentsDetails.DynamicsUrl};" +
        $"Username={username};" +
        $"Password={password};" +
        $"AppId={EnvironmentsDetails.AppId};" +
        $"RedirectUri={EnvironmentsDetails.RedirectUri};" +
        $"TokenCacheStorePath={GetTokenCachePath()};" +
        $"RequireNewInstance=True;" +   // Keep True for new credentials
        $"LoginPrompt=Auto;" +          // Keep Auto for new credentials
        $"UseWebApi=true;" +
        $"InteractiveLogin=true";

    // Gets the secure path for storing authentication tokens
    private string GetTokenCachePath()
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            TokenCacheFolder,
            TokenCacheName);

        // Create token cache directory if it doesn't exist
        Directory.CreateDirectory(path);
        return path;
    }

    // Securely prompts user for credentials with validation
    private (string username, string password) PromptForCredentials()
    {
        _logger.LogInformation("Prompting user for credentials");
        try
        {
            Console.Write("\nUsername (email format): ");
            string? username = Console.ReadLine();

            // Ensure both username and password are provided
            if (string.IsNullOrEmpty(username))
            {
                throw new ArgumentException("Username cannot be empty.");
            }

            Console.Write("Password: ");

            // Handle secure password input from console
            string password = PasswordHelper.GetSecurePassword();

            if (string.IsNullOrEmpty(password))
            {
                throw new ArgumentException("Password cannot be empty.");
            }

            return (username, password);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to collect credentials");
            throw new Exception("Failed to collect credentials.", ex);
        }
    }

    // Verifies connection by executing a WhoAmI request to Dynamics 365
    private void VerifyConnection()
    {
        // Validate service client before verification attempt
        if (_serviceClient == null)
        {
            throw new InvalidOperationException("Service client is not initialized");
        }

        try
        {
            IOrganizationService orgService = _serviceClient;

            // Execute WhoAmI request to confirm connection and get user context
            var response = orgService.Execute(new WhoAmIRequest()) as WhoAmIResponse
                ?? throw new Exception("Failed to verify connection - null response from WhoAmI request");

            _logger.LogInformation("Connection verified. Connected as User ID: {UserId}", response.UserId);
            Console.WriteLine($"\nConnected successfully! User ID: {response.UserId}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying connection");
            throw new Exception("Error verifying connection", ex);
        }
    }

    // Performs cleanup operations when connection attempts fail
    private void CleanupFailedConnection()
    {
        _logger.LogInformation("Cleaning up failed connection");
        if (_serviceClient != null)
        {
            _serviceClient.Dispose();
            _serviceClient = null;
        }
        _isConnected = false;
    }
}