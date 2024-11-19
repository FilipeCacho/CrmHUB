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

    private const string TokenCacheExtension = ".token";
    private const string TokenLifetimeExtension = ".lifetime";
    private readonly string _tokenLifetimePath;

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

    // Token cache configuration
    private const string TokenCacheFolder = "CrmHub";
    private const string TokenCacheName = "TokenCache";
    private readonly string _tokenCachePath;

    // Track token expiration to proactively refresh before it expires
    private DateTime _tokenExpirationTime = DateTime.MinValue;
    private const int TokenExpirationBufferMinutes = 5;

    public enum AuthenticationState
    {
        NotAuthenticated,
        CachedTokenValid,
        RequiresInteractive
    }

    private AuthenticationState _authState = AuthenticationState.NotAuthenticated;

    private SessionManager(ILogger<SessionManager> logger)
    {
        _logger = logger;
        _tokenCachePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            TokenCacheFolder,
            EnvironmentsDetails.CurrentEnvironment,
            TokenCacheName + TokenCacheExtension);

        _tokenLifetimePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            TokenCacheFolder,
            EnvironmentsDetails.CurrentEnvironment,
            TokenCacheName + TokenLifetimeExtension);

        InitializeTokenCache();
    }

    private void SaveTokenLifetime()
    {
        if (_tokenExpirationTime <= DateTime.Now)
        {
            _tokenExpirationTime = DateTime.Now.AddHours(1);
        }

        try
        {
            File.WriteAllText(_tokenLifetimePath, _tokenExpirationTime.ToString("O"));
            _logger.LogInformation($"Token lifetime saved. Expiration: {_tokenExpirationTime}");
            Console.Clear();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save token lifetime");
        }
    }

    private void LoadTokenLifetime()
    {
        try
        {
            if (File.Exists(_tokenLifetimePath))
            {
                var lifetimeStr = File.ReadAllText(_tokenLifetimePath);
                if (DateTime.TryParse(lifetimeStr, out DateTime lifetime))
                {
                    _tokenExpirationTime = lifetime;
                    _logger.LogInformation($"Loaded token lifetime. Expiration: {_tokenExpirationTime}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load token lifetime");
        }
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
            LogConnectionDetails();

            if (_serviceClient?.IsReady == true && _isConnected && !IsTokenExpired())
            {
                _logger.LogInformation("Using existing connection with valid token");
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
                // First attempt: Silent token authentication
                _logger.LogInformation("Attempting silent token authentication");
                if (TryTokenAuthentication())
                {
                    return;
                }

                // If silent auth failed but we have credentials, try those
                if (_authState == AuthenticationState.RequiresInteractive)
                {
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
    private bool IsTokenExpired()
    {
        // First check if token expiration time is min value (uninitialized)
        if (_tokenExpirationTime == DateTime.MinValue)
            return true;

        try
        {
            // Add buffer time and compare with current time
            var expirationWithBuffer = _tokenExpirationTime.AddMinutes(-TokenExpirationBufferMinutes);
            return DateTime.Now >= expirationWithBuffer;
        }
        catch (ArgumentOutOfRangeException)
        {
            _logger.LogWarning("Token expiration time is invalid, treating as expired");
            return true;
        }
    }


    // Attempts to authenticate using cached token before trying other methods
    private bool TryTokenAuthentication()
    {
        try
        {
            // If no token file exists or it's empty, require interactive auth
            if (!File.Exists(_tokenCachePath) || new FileInfo(_tokenCachePath).Length == 0)
            {
                _authState = AuthenticationState.RequiresInteractive;
                return false;
            }

            // Try to connect with existing token first
            string connectionString = BuildTokenConnectionString(_tokenCachePath, true);
            _serviceClient?.Dispose();
            _serviceClient = new ServiceClient(connectionString);

            // If connection successful with existing token, we're done
            if (_serviceClient.IsReady && VerifyConnectionQuietly())
            {
                _isConnected = true;
                _tokenExpirationTime = DateTime.Now.AddHours(1);
                SaveTokenLifetime();
                return true;
            }

            // If token is expired, try silent refresh
            string refreshConnectionString = connectionString +
                ";ForceTokenRefresh=true;Browser=NoBrowser;Prompt=none;";

            _serviceClient?.Dispose();
            _serviceClient = new ServiceClient(refreshConnectionString);

            if (_serviceClient.IsReady && VerifyConnectionQuietly())
            {
                _isConnected = true;
                _tokenExpirationTime = DateTime.Now.AddHours(1);
                SaveTokenLifetime();
                return true;
            }

            _authState = AuthenticationState.RequiresInteractive;
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Token authentication failed");
            _authState = AuthenticationState.RequiresInteractive;
            return false;
        }
    }

    private bool RefreshTokenSilently()
    {
        try
        {
            _logger.LogInformation("Attempting silent token refresh");

            var refreshConnectionString = BuildTokenConnectionString(_tokenCachePath, true) +
                $";ForceTokenRefresh=true;" +
                $"TokenRefreshAttempts=3;" +
                $"Browser=NoBrowser;" +
                $"Prompt=none;";

            using var tempClient = new ServiceClient(refreshConnectionString);

            if (tempClient.IsReady && VerifyConnectionQuietly())
            {
                _serviceClient?.Dispose();
                _serviceClient = new ServiceClient(refreshConnectionString);
                _isConnected = true;
                _tokenExpirationTime = DateTime.Now.AddHours(1);
                SaveTokenLifetime(); // Save the new expiration time
                _logger.LogInformation("Token refreshed successfully");
                return true;
            }

            _logger.LogInformation("Token refresh failed");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Token refresh failed with error: " + ex.Message);
            return false;
        }
    }

    // Verify connection without triggering MFA
    private bool VerifyConnectionQuietly()
    {
        try
        {
            if (_serviceClient == null) return false;

            var start = DateTime.Now;
            var request = new WhoAmIRequest();
            var response = _serviceClient.Execute(request) as WhoAmIResponse;
            var duration = DateTime.Now - start;

            _logger.LogInformation($"Silent verification took: {duration.TotalMilliseconds}ms");

            if (response != null)
            {
                _logger.LogInformation($"Verified connection for user: {response.UserId}");
                return true;
            }

            _logger.LogInformation("Verification returned null response");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogInformation($"Silent verification failed: {ex.Message}");
            return false;
        }
    }

    private void LogConnectionDetails()
    {
        _logger.LogInformation($"Current Environment: {EnvironmentsDetails.CurrentEnvironment}");
        _logger.LogInformation($"Token Cache Path: {_tokenCachePath}");
        _logger.LogInformation($"Token Expiration: {_tokenExpirationTime}");
        _logger.LogInformation($"Is Connected: {_isConnected}");
        _logger.LogInformation($"Service Client Ready: {_serviceClient?.IsReady}");
    }

    // Attempts to connect using stored credentials with retry logic
    private bool TryStoredCredentialsWithRetry(string username, string password)
    {
        int retryCount = 0;
        bool authSuccess = false;

        while (!authSuccess && retryCount <= MaxCredentialRetries)
        {
            try
            {
                Console.WriteLine($"\nAttempting to connect with stored credentials (Attempt {retryCount + 1}/{MaxCredentialRetries + 1})...");

                // Use stored credentials to get a new token
                string connectionString = BuildCredentialConnectionString(username, password);

                _serviceClient?.Dispose();
                _serviceClient = new ServiceClient(connectionString);

                if (!_serviceClient.IsReady)
                {
                    throw new Exception(_serviceClient.LastError ?? "Service client initialization failed");
                }

                // After successful authentication, token will be cached automatically
                VerifyConnection();
                _isConnected = true;
                _tokenExpirationTime = DateTime.Now.AddHours(1); // Set expiration for new token
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
    private string BuildTokenConnectionString(string tokenCachePath, bool silent = false) =>
    $"AuthType=OAuth;" +
    $"Url={EnvironmentsDetails.DynamicsUrl};" +
    $"AppId={EnvironmentsDetails.AppId};" +
    $"RedirectUri={EnvironmentsDetails.RedirectUri};" +
    $"TokenCacheStorePath={tokenCachePath};" +
    $"RequireNewInstance=False;" +
    $"LoginPrompt=Never;" +
    $"UseWebApi=true;" +
    $"CacheRetrievalTimeout=120;" +
    $"TokenCacheTimeout=120;" +
    $"SkipDiscovery=true;" +
    $"MaxRetries=3;" +
    $"RetryDelay=10;" +
    $"PreventBrowserPrompt=true;" +
    $"PreferConnectionFromTokenCache=true;" +
    $"OfflineAccess=true;" +
    $"UseDefaultWebBrowser=false;" +
    $"NoPrompt=true;" +
    $"Browser=NoBrowser;" +               // Add this line
    $"Prompt=none;" +                     // Add this line
    $"TokenRefreshAttempts=3;" +
    $"InteractiveLogin=false;" +
    $"ForceTokenRefresh=false;" +
    $"DisableTokenStorageProvider=false;" + // Add this line
    $"EnableConnectionStatusEvents=true;" + // Add this line
    $"ClientSecret={EnvironmentsDetails.AppId}";

    // Builds connection string for credential-based authentication
    private string BuildCredentialConnectionString(string username, string password) =>
    $"AuthType=OAuth;" +
    $"Url={EnvironmentsDetails.DynamicsUrl};" +
    $"Username={username};" +
    $"Password={password};" +
    $"AppId={EnvironmentsDetails.AppId};" +
    $"RedirectUri={EnvironmentsDetails.RedirectUri};" +
    $"TokenCacheStorePath={_tokenCachePath};" +
    $"RequireNewInstance=True;" +
    $"LoginPrompt=Auto;" +
    $"UseWebApi=true;" +
    $"ClientSecret={EnvironmentsDetails.AppId};" +
    $"CacheRetrievalTimeout=120;" +
    $"TokenCacheTimeout=120;" +
    $"InteractiveLogin=true;" +
    $"PreferConnectionFromTokenCache=false;" + // Add this line
    $"OfflineAccess=true;" +                  // Add this line
    $"TokenRefreshAttempts=3";                // Add this line

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

    // Add a method to handle token cache initialization
    private void InitializeTokenCache()
    {
        try
        {
            _logger.LogInformation($"Initializing token cache at: {_tokenCachePath}");

            var cacheDir = Path.GetDirectoryName(_tokenCachePath);
            if (cacheDir != null && !Directory.Exists(cacheDir))
            {
                _logger.LogInformation($"Creating token cache directory: {cacheDir}");
                Directory.CreateDirectory(cacheDir);
            }

            // Don't create an empty file - let the authentication process handle file creation
            var fileInfo = new FileInfo(_tokenCachePath);
            if (fileInfo.Exists)
            {
                _logger.LogInformation($"Existing token cache found. Size: {fileInfo.Length} bytes");
                if (fileInfo.Length == 0)
                {
                    _logger.LogInformation("Token cache file exists but is empty");
                    File.Delete(_tokenCachePath);
                    _logger.LogInformation("Deleted empty token cache file");
                }
            }
            else
            {
                _logger.LogInformation("No existing token cache found - will be created during authentication");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize token cache");
            throw; // Rethrow to ensure setup problems are visible
        }
    }

    private void LogTokenCacheStatus()
    {
        try
        {
            if (File.Exists(_tokenCachePath))
            {
                var fileInfo = new FileInfo(_tokenCachePath);
                _logger.LogInformation($"Token cache status - Exists: true, Size: {fileInfo.Length} bytes, Last Write: {fileInfo.LastWriteTime}");
            }
            else
            {
                _logger.LogInformation("Token cache status - Exists: false");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking token cache status");
        }
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