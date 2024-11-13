public static class ErrorHandler
{
    public static async Task HandleException(Exception ex)
    {
        try
        {
            Console.WriteLine($"\nAn error occurred: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"Inner error: {ex.InnerException.Message}");
            }
            Console.WriteLine("\nPress Enter to continue...");

            // Use ReadLine instead of ReadKey for better compatibility
            await Task.Run(() => Console.ReadLine());
        }
        catch (InvalidOperationException)
        {
            // If console operations fail, wait briefly
            await Task.Delay(1000);
        }
    }
}