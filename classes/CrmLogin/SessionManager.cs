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
    private string _tokenCachePath;
    private string _tokenLifetimePath;
    private string _currentEnvironment;
    private string _baseTokenPath;

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
        _currentEnvironment = EnvironmentsDetails.CurrentEnvironment;
        _baseTokenPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            TokenCacheFolder);

        // Initialize both paths with default values
        _tokenCachePath = Path.Combine(_baseTokenPath,
            EnvironmentsDetails.CurrentEnvironment,
            TokenCacheName + TokenCacheExtension);

        _tokenLifetimePath = Path.Combine(_baseTokenPath,
            EnvironmentsDetails.CurrentEnvironment,
            TokenCacheName + TokenLifetimeExtension);

        UpdateTokenPaths();
        InitializeTokenCache();
    }

    private void UpdateTokenPaths()
    {
        var envPath = Path.Combine(_baseTokenPath, EnvironmentsDetails.CurrentEnvironment);

        // Create environment directory if it doesn't exist
        if (!Directory.Exists(envPath))
        {
            Directory.CreateDirectory(envPath);
        }

        _tokenCachePath = Path.Combine(envPath, TokenCacheName + TokenCacheExtension);
        _tokenLifetimePath = Path.Combine(envPath, TokenCacheName + TokenLifetimeExtension);

        _logger.LogInformation($"Token paths updated for environment {EnvironmentsDetails.CurrentEnvironment}");
        _logger.LogInformation($"New token cache path: {_tokenCachePath}");
    }

    private void EnsureCorrectEnvironmentPaths()
    {
        if (_currentEnvironment != EnvironmentsDetails.CurrentEnvironment)
        {
            _logger.LogInformation($"Environment changed from {_currentEnvironment} to {EnvironmentsDetails.CurrentEnvironment}");
            _currentEnvironment = EnvironmentsDetails.CurrentEnvironment;
            UpdateTokenPaths();

            // Reset all connection state
            _isConnected = false;
            _serviceClient?.Dispose();
            _serviceClient = null;
            _tokenExpirationTime = DateTime.MinValue;
            _lastConnectionAttempt = DateTime.MinValue;
            _authState = AuthenticationState.RequiresInteractive;
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

    public static SessionManager Instance => _instance;

    public ServiceClient GetClient()
    {
        lock (_lock)
        {
            if (!_isConnected || _serviceClient == null)
            {
                ConnectToService();
            }
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
            _tokenExpirationTime = DateTime.MinValue;
            _lastConnectionAttempt = DateTime.MinValue;
            _authState = AuthenticationState.NotAuthenticated;
        }
    }

    public bool TryConnect()
    {
        try
        {
            // Force new connection when trying to connect explicitly
            _serviceClient?.Dispose();
            _serviceClient = null;
            _isConnected = false;
            _lastConnectionAttempt = DateTime.MinValue; // Reset throttling

            ConnectToService();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to Dynamics 365");
            return false;
        }
    }

    public bool CheckConnection()
    {
        lock (_lock)
        {
            if (!_isConnected || _serviceClient == null)
                return false;

            try
            {
                if (DateTime.Now - _lastConnectionAttempt >= ConnectionThrottle)
                {
                    IOrganizationService orgService = _serviceClient;
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

    private void ConnectToService()
    {
        lock (_lock)
        {
            try
            {
                EnsureCorrectEnvironmentPaths();
                LogConnectionDetails();

                // Reset throttling when environment changes
                if (_currentEnvironment != EnvironmentsDetails.CurrentEnvironment)
                {
                    _lastConnectionAttempt = DateTime.MinValue;
                }

                // Remove throttling check here to allow immediate connection after environment switch
                _lastConnectionAttempt = DateTime.Now;

                // Clear existing connection
                _serviceClient?.Dispose();
                _serviceClient = null;
                _isConnected = false;

                // Always try interactive auth when switching environments
                var (username, password) = CredentialManager.LoadCredentials();
                if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                {
                    HandleFirstTimeSetup();
                    return;
                }

                string connectionString = BuildCredentialConnectionString(username!, password!);
                _serviceClient = new ServiceClient(connectionString);

                if (!_serviceClient.IsReady)
                {
                    throw new Exception(_serviceClient.LastError ?? "Service client initialization failed");
                }

                VerifyConnection();
                _isConnected = true;
                _tokenExpirationTime = DateTime.Now.AddHours(1);
                SaveTokenLifetime();
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
        if (_tokenExpirationTime == DateTime.MinValue)
            return true;

        try
        {
            var expirationWithBuffer = _tokenExpirationTime.AddMinutes(-TokenExpirationBufferMinutes);
            return DateTime.Now >= expirationWithBuffer;
        }
        catch (ArgumentOutOfRangeException)
        {
            _logger.LogWarning("Token expiration time is invalid, treating as expired");
            return true;
        }
    }

    private bool TryTokenAuthentication()
    {
        try
        {
            if (!File.Exists(_tokenCachePath) || new FileInfo(_tokenCachePath).Length == 0)
            {
                _authState = AuthenticationState.RequiresInteractive;
                return false;
            }

            string connectionString = BuildTokenConnectionString(_tokenCachePath, true);
            _serviceClient?.Dispose();
            _serviceClient = new ServiceClient(connectionString);

            if (_serviceClient.IsReady && VerifyConnectionQuietly())
            {
                _isConnected = true;
                _tokenExpirationTime = DateTime.Now.AddHours(1);
                SaveTokenLifetime();
                return true;
            }

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

    private void LogConnectionDetails()
    {
        _logger.LogInformation($"Current Environment: {EnvironmentsDetails.CurrentEnvironment}");
        _logger.LogInformation($"Token Cache Path: {_tokenCachePath}");
        _logger.LogInformation($"Token Expiration: {_tokenExpirationTime}");
        _logger.LogInformation($"Is Connected: {_isConnected}");
        _logger.LogInformation($"Service Client Ready: {_serviceClient?.IsReady}");
    }

    private bool TryStoredCredentialsWithRetry(string username, string password)
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
                _tokenExpirationTime = DateTime.Now.AddHours(1);
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
                catch (Exception ex)
                {
                    Console.WriteLine($"\nSetup failed: {ex.Message}");
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
                        string connectionString = BuildCredentialConnectionString(username, password);

                        Console.WriteLine("\nAttempting to connect with new credentials...");
                        Console.WriteLine("A browser window may open for MFA verification.");

                        _serviceClient?.Dispose();
                        _serviceClient = new ServiceClient(connectionString);

                        if (!_serviceClient.IsReady)
                        {
                            throw new Exception(_serviceClient.LastError ?? "Service client initialization failed");
                        }

                        VerifyConnection();
                        _isConnected = true;
                        // Save credentials after successful connection
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

    private string BuildTokenConnectionString(string tokenCachePath, bool silent = false)
    {
        // Ensure we're using the correct path for the current environment
        EnsureCorrectEnvironmentPaths();

        return $"AuthType=OAuth;" +
        $"Url={EnvironmentsDetails.DynamicsUrl};" +
        $"AppId={EnvironmentsDetails.AppId};" +
        $"RedirectUri={EnvironmentsDetails.RedirectUri};" +
        $"TokenCacheStorePath={_tokenCachePath};" +
        $"RequireNewInstance=True;" +
        $"LoginPrompt=Never;" +
        $"UseWebApi=true;" +
        $"CacheRetrievalTimeout=120;" +
        $"TokenCacheTimeout=120;" +
        $"SkipDiscovery=true;" +
        $"MaxRetries=3;" +
        $"RetryDelay=10;" +
        $"PreventBrowserPrompt={silent};" +
        $"PreferConnectionFromTokenCache=true;" +
        $"OfflineAccess=true;" +
        $"UseDefaultWebBrowser=false;" +
        $"NoPrompt={silent};" +
        $"Browser={(silent ? "NoBrowser" : "DefaultBrowser")};" +
        $"Prompt={(silent ? "none" : "login")};" +
        $"TokenRefreshAttempts=3;" +
        $"InteractiveLogin={!silent};" +
        $"ForceTokenRefresh=false;" +
        $"DisableTokenStorageProvider=false;" +
        $"EnableConnectionStatusEvents=true;" +
        $"ClientSecret={EnvironmentsDetails.AppId}";
    }

    private string BuildCredentialConnectionString(string username, string password)
    {
        return $"AuthType=OAuth;" +
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
        $"PreferConnectionFromTokenCache=false;" +
        $"OfflineAccess=true;" +
        $"TokenRefreshAttempts=3;" +
        $"MaxRetries=3;" +
        $"RetryDelay=5;" +
        $"EnableConnectionStatusEvents=true";
    }

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

            LoadTokenLifetime();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize token cache");
            throw;
        }
    }

    private (string username, string password) PromptForCredentials()
    {
        _logger.LogInformation("Prompting user for credentials");
        try
        {
            Console.Write("\nUsername (email format): ");
            string? username = Console.ReadLine();

            if (string.IsNullOrEmpty(username))
            {
                throw new ArgumentException("Username cannot be empty.");
            }

            Console.Write("Password: ");
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

    private void VerifyConnection()
    {
        if (_serviceClient == null)
        {
            throw new InvalidOperationException("Service client is not initialized");
        }

        try
        {
            IOrganizationService orgService = _serviceClient;
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
}