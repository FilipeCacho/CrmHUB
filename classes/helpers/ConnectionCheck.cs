public static class ConnectionCheck
{
    public static bool EnsureConnected()
    {
        if (SessionManager.Instance.CheckConnection())
            return true;

        Console.WriteLine($"\nNot connected to {EnvironmentsDetails.CurrentEnvironment} environment.");
        Console.WriteLine("Attempting to reconnect...");

        return SessionManager.Instance.TryConnect();
    }
}