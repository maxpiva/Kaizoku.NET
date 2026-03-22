using KaizokuBackend.Models;
using KaizokuBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace KaizokuBackend.Hubs
{
    [Authorize]
    public class ProgressHub : Hub
    {
        private readonly ILogger<ProgressHub> _logger;
        public ProgressHub(ILogger<ProgressHub> logger)
        {
            _logger = logger;
        }
        public override Task OnConnectedAsync()
        {
            var userId = Context.User?.FindFirst("UserId")?.Value ?? "unknown";
            _logger.LogInformation($"SignalR Client connected: {Context.ConnectionId} (User: {userId})");
            return base.OnConnectedAsync();
        }

        public override Task OnDisconnectedAsync(Exception? exception)
        {
            _logger.LogInformation($"SignalR Client disconnected: {Context.ConnectionId}");
            return base.OnDisconnectedAsync(exception);
        }
    }
}
