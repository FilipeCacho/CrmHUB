public static class EnvironmentsDetails
{
    public const string CredentialTarget = "DynamicsConnection";
    public const string AppId = "51f81489-12ee-4a9e-aaae-a2591f45987d";
    public const string RedirectUri = "http://localhost";
    public static string ClientSecret => AppId;


    // Environment URLs
    public static class Urls
    {
        public const string PRD = "https://edpr.crm4.dynamics.com/";
        public const string PRE = "https://edprpre.crm4.dynamics.com/";
        public const string DEV = "https://edprdesarrollo1.crm4.dynamics.com/";
    }

    public static class TokenConfig
    {
        public const int CacheTimeoutMinutes = 60;
        public const int TokenRefreshBufferMinutes = 5;
    }

    private static string _currentEnvironment = "PRD";
    public static string CurrentEnvironment
    {
        get => _currentEnvironment;
        set
        {
            if (value is "PRD" or "PRE" or "DEV")
                _currentEnvironment = value;
            else
                throw new ArgumentException("Invalid environment specified");
        }
    }

    // This property replaces the GetCurrentUrl() method and fixes the DynamicsUrl error
    public static string DynamicsUrl => CurrentEnvironment switch
    {
        "PRD" => Urls.PRD,
        "PRE" => Urls.PRE,
        "DEV" => Urls.DEV,
        _ => throw new InvalidOperationException("Invalid environment state")
    };
}