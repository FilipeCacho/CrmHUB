public static class ErrorHandler
{
    public static void HandleException(Exception ex)
    {
        Console.WriteLine($"\nAn error occurred: {ex.Message}");
        if (ex.InnerException != null)
        {
            Console.WriteLine($"Inner error: {ex.InnerException.Message}");
        }
        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }
}