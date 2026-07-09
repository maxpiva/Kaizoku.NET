using RensaioBackend.Models;
using RensaioBackend.Services;
using Microsoft.AspNetCore.SignalR;

namespace RensaioBackend.Hubs
{
    public class ProgressHub : Hub
    {
        private readonly ILogger<ProgressHub> _logger;
        public ProgressHub(ILogger<ProgressHub> logger)
        {
            _logger = logger;
        }
        public override Task OnConnectedAsync()
        {
            _logger.LogDebug($"SignalR Client connected: {Context.ConnectionId}");
            return base.OnConnectedAsync();
        }

        public override Task OnDisconnectedAsync(Exception? exception)
        {
            _logger.LogDebug($"SignalR Client disconnected: {Context.ConnectionId}");
            return base.OnDisconnectedAsync(exception);
        }



    }
}
