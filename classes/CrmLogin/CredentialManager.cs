using Meziantou.Framework.Win32;

public static class CredentialManager
{
    public static (string? username, string? password) LoadCredentials()
    {
        // Verify we're running on Windows
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("This functionality is only supported on Windows.");
        }
        var credential = Meziantou.Framework.Win32.CredentialManager.ReadCredential(EnvironmentsDetails.CredentialTarget);
        if (credential != null)
        {
            return (credential.UserName, credential.Password);
        }
        return (null, null);
    }

    public static void SaveCredentials(string username, string password)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("This functionality is only supported on Windows.");
        }
        Meziantou.Framework.Win32.CredentialManager.WriteCredential(
            applicationName: EnvironmentsDetails.CredentialTarget,
            userName: username,
            secret: password,
            comment: "Dynamics 365 Connection Credentials",
            persistence: CredentialPersistence.LocalMachine);
    }

    // Renamed from DeleteCredentials to RemoveCredentials to match the SessionManager usage
    public static void RemoveCredentials()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("This functionality is only supported on Windows.");
        }
        try
        {
            Meziantou.Framework.Win32.CredentialManager.DeleteCredential(EnvironmentsDetails.CredentialTarget);
        }
        catch (Exception)
        {
            // Silently handle the case where credentials don't exist
        }
    }
}