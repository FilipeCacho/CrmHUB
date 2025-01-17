using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Cors;

namespace CrmHub.Classes.ParkVisualizer
{
    public class ApiServer
{
    private WebApplication? _app;
    private readonly string _url = "http://localhost:5000";
    private bool _isRunning;
    private Task? _serverTask;

    public async Task StartAsync()
    {
        if (_isRunning) return;

            var builder = WebApplication.CreateBuilder();
            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer(); // Add this
            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowReactApp",
                    builder => builder
                        .WithOrigins("http://localhost:5173")
                        .AllowAnyMethod()
                        .AllowAnyHeader());
            });

            _app = builder.Build();

            _app.UseRouting(); // Add this
            _app.UseCors("AllowReactApp");
            _app.MapControllers();

            // Start the web server on a background task without awaiting it
            _serverTask = _app.RunAsync(_url);
        _isRunning = true;
    }

    public async Task StopAsync()
    {
        if (_app != null && _isRunning)
        {
            await _app.StopAsync();
            _isRunning = false;
            if (_serverTask != null)
            {
                await _serverTask;
            }
        }
    }
}
}