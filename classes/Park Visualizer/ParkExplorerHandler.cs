using System.Diagnostics;

namespace CrmHub.Classes.ParkVisualizer
{
    public class ParkExplorerHandler
    {
        private readonly string _reactAppUrl = "http://localhost:5173";
        private readonly ApiServer _apiServer;
        private Process _devServerProcess = null;
        private readonly CancellationTokenSource _cts;
        private bool _isShuttingDown = false;

        public ParkExplorerHandler()
        {
            _apiServer = new ApiServer();
            _cts = new CancellationTokenSource();

            // Set up Ctrl+C handler
            Console.CancelKeyPress += async (sender, e) =>
            {
                e.Cancel = true; // Prevent default Ctrl+C behavior
                if (!_isShuttingDown)
                {
                    await CleanupAsync();
                }
            };
        }

        // New method to handle cleanup
        private async Task CleanupAsync()
        {
            try
            {
                _isShuttingDown = true;
                Console.WriteLine("\nShutting down services...");

                // Kill the dev server process if it's running
                if (_devServerProcess != null && !_devServerProcess.HasExited)
                {
                    Console.WriteLine("Stopping dev server...");
                    _devServerProcess.Kill(true);
                    _devServerProcess = null;
                }

                // Stop the API server
                Console.WriteLine("Stopping API server...");
                await _apiServer.StopAsync();

                _cts.Cancel(); // Signal cancellation to any ongoing tasks

                Console.WriteLine("All processes stopped successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during cleanup: {ex.Message}");
            }
            finally
            {
                _isShuttingDown = false;
            }
        }

        public async Task LaunchParkExplorer()
        {
            try
            {
                Console.WriteLine("Starting API server...");
                await _apiServer.StartAsync();
                Console.WriteLine("API server started successfully");

                var currentDir = Directory.GetCurrentDirectory();
                var projectRoot = Directory.GetParent(currentDir)?.Parent?.Parent?.FullName;
                var parkVisualizerPath = Path.Combine(projectRoot, "parkvisualizer");

                Console.WriteLine($"Looking for React app in: {parkVisualizerPath}");
                if (!Directory.Exists(parkVisualizerPath))
                {
                    throw new DirectoryNotFoundException($"Could not find parkvisualizer directory at {parkVisualizerPath}");
                }

                Console.WriteLine("Installing npm packages...");
                var npmInstall = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c cd \"{parkVisualizerPath}\" && npm install",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = false,
                        WorkingDirectory = parkVisualizerPath
                    }
                };
                npmInstall.Start();
                await npmInstall.WaitForExitAsync(_cts.Token);

                Console.WriteLine("Starting React dev server...");
                _devServerProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c cd \"{parkVisualizerPath}\" && npm run dev",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = false,
                        WorkingDirectory = parkVisualizerPath
                    }
                };

                _devServerProcess.OutputDataReceived += (sender, e) => {
                    if (!string.IsNullOrEmpty(e.Data))
                        Console.WriteLine($"Dev server: {e.Data}");
                };
                _devServerProcess.ErrorDataReceived += (sender, e) => {
                    if (!string.IsNullOrEmpty(e.Data))
                        Console.WriteLine($"Dev server error: {e.Data}");
                };

                _devServerProcess.Start();
                _devServerProcess.BeginOutputReadLine();
                _devServerProcess.BeginErrorReadLine();

                await Task.Delay(5000, _cts.Token); // Wait for dev server to start

                Console.WriteLine("Opening browser...");
                Process.Start(new ProcessStartInfo
                {
                    FileName = _reactAppUrl,
                    UseShellExecute = true
                });

                Console.WriteLine("Park Explorer is running. Press Ctrl+C to exit...");

                // Wait for cancellation
                await Task.Delay(-1, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation, no need to handle
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error launching Park Explorer: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                await CleanupAsync();
            }
        }
    }
}