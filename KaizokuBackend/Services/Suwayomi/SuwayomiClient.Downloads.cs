using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using KaizokuBackend.Models;

namespace KaizokuBackend.Services
{
    public partial class SuwayomiClient
    {
        /// <summary>
        /// Starts the download manager to begin processing queued downloads
        /// </summary>
        /// <returns>True if successful</returns>
        public async Task<bool> StartDownloadsAsync(CancellationToken token = default)
        {
            var url = $"{_apiUrl}/downloads/start";
            var response = await _http.GetAsync(url, token).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }

        /// <summary>
        /// Stops the download manager to pause processing queued downloads
        /// </summary>
        /// <returns>True if successful</returns>
        public async Task<bool> StopDownloadsAsync(CancellationToken token = default)
        {
            var url = $"{_apiUrl}/downloads/stop";
            var response = await _http.GetAsync(url, token).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }

        /// <summary>
        /// Clears the download queue
        /// </summary>
        /// <returns>True if successful</returns>
        public async Task<bool> ClearDownloadsAsync(CancellationToken token = default)
        {
            var url = $"{_apiUrl}/downloads/clear";
            var response = await _http.GetAsync(url, token).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }

        /// <summary>
        /// Delegate for handling download status updates
        /// </summary>
        /// <param name="status">The download status JSON</param>
        public delegate void DownloadStatusUpdateHandler(string status);

        /// <summary>
        /// Event triggered when download status is updated
        /// </summary>
        public event DownloadStatusUpdateHandler? OnDownloadStatusUpdate;

        private ClientWebSocket? _downloadsWebSocket;
        private CancellationTokenSource? _downloadsCancellationTokenSource;
        private Task? _downloadsListenTask;

        /// <summary>
        /// Connects to the download status WebSocket to receive real-time updates
        /// </summary>
        /// <returns>True if connection was established successfully</returns>
        public async Task<bool> ConnectToDownloadsWebSocketAsync(CancellationToken token = default)
        {
            try
            {
                // Clean up any existing connection
                await DisconnectFromDownloadsWebSocketAsync().ConfigureAwait(false);

                _downloadsCancellationTokenSource = new CancellationTokenSource();
                _downloadsWebSocket = new ClientWebSocket();
                
                // Get the base URL without the API version
                string baseUrl = _config["SuwayomiApi"]?.Replace("/api/v1", "") ?? string.Empty;
                if (string.IsNullOrEmpty(baseUrl))
                {
                    return false;
                }

                // Convert http(s):// to ws(s):// for WebSocket connection
                string wsUrl = baseUrl
                    .Replace("http://", "ws://")
                    .Replace("https://", "wss://");

                // Connect to the WebSocket endpoint
                await _downloadsWebSocket.ConnectAsync(
                    new Uri($"{wsUrl}/api/v1/downloads"), 
                    _downloadsCancellationTokenSource.Token).ConfigureAwait(false);

                // Start listening for messages
                _downloadsListenTask = ListenForDownloadUpdatesAsync(_downloadsCancellationTokenSource.Token);

                return _downloadsWebSocket.State == WebSocketState.Open;
            }
            catch (Exception)
            {
                await DisconnectFromDownloadsWebSocketAsync().ConfigureAwait(false);
                return false;
            }
        }

        /// <summary>
        /// Disconnects from the download status WebSocket
        /// </summary>
        public async Task DisconnectFromDownloadsWebSocketAsync()
        {
            try
            {
                if (_downloadsCancellationTokenSource != null)
                {
                    _downloadsCancellationTokenSource.Cancel();
                    _downloadsCancellationTokenSource = null;
                }

                if (_downloadsWebSocket != null)
                {
                    if (_downloadsWebSocket.State == WebSocketState.Open)
                    {
                        await _downloadsWebSocket.CloseAsync(
                            WebSocketCloseStatus.NormalClosure, 
                            "Client disconnecting", 
                            CancellationToken.None).ConfigureAwait(false);
                    }
                    
                    _downloadsWebSocket.Dispose();
                    _downloadsWebSocket = null;
                }

                // Wait for the listen task to complete if it's running
                if (_downloadsListenTask != null)
                {
                    try
                    {
                        await _downloadsListenTask.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // This is expected when cancellation is requested
                    }
                    
                    _downloadsListenTask = null;
                }
            }
            catch (Exception)
            {
                // Ignore exceptions during cleanup
            }
        }

        private async Task ListenForDownloadUpdatesAsync(CancellationToken cancellationToken)
        {
            var buffer = new byte[4096];
            var stringBuilder = new StringBuilder();

            try
            {
                while (!cancellationToken.IsCancellationRequested && 
                       _downloadsWebSocket != null &&
                       _downloadsWebSocket.State == WebSocketState.Open)
                {
                    stringBuilder.Clear();
                    WebSocketReceiveResult result;

                    do
                    {
                        result = await _downloadsWebSocket.ReceiveAsync(
                            new ArraySegment<byte>(buffer),
                            cancellationToken).ConfigureAwait(false);

                        if (result.MessageType == WebSocketMessageType.Text)
                        {
                            string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                            stringBuilder.Append(message);
                        }
                    }
                    while (!result.EndOfMessage && !cancellationToken.IsCancellationRequested);

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        string message = stringBuilder.ToString();
                        OnDownloadStatusUpdate?.Invoke(message);
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // This is expected when cancellation is requested
            }
            catch (WebSocketException)
            {
                // Connection was closed or interrupted
            }
            catch (Exception)
            {
                // Handle other exceptions
            }
        }

        /// <summary>
        /// Represents the download status with details about current and queued downloads
        /// </summary>
        public class DownloadStatus
        {
            /// <summary>
            /// Status of the download manager
            /// </summary>
            public string Status { get; set; } = string.Empty;
            
            /// <summary>
            /// Currently downloading chapter
            /// </summary>
            public CurrentDownload? Current { get; set; }
            
            /// <summary>
            /// List of queued chapters
            /// </summary>
            public List<QueuedDownload> Queue { get; set; } = new List<QueuedDownload>();
        }

        /// <summary>
        /// Represents a currently downloading chapter
        /// </summary>
        public class CurrentDownload
        {
            /// <summary>
            /// Chapter ID
            /// </summary>
            public int ChapterId { get; set; }
            
            /// <summary>
            /// Manga ID
            /// </summary>
            public int MangaId { get; set; }
            
            /// <summary>
            /// Chapter title
            /// </summary>
            public string ChapterTitle { get; set; } = string.Empty;
            
            /// <summary>
            /// Manga title
            /// </summary>
            public string MangaTitle { get; set; } = string.Empty;
            
            /// <summary>
            /// Progress as a percentage
            /// </summary>
            public double Progress { get; set; }
            
            /// <summary>
            /// Downloaded page count
            /// </summary>
            public int DownloadedPageCount { get; set; }
            
            /// <summary>
            /// Total page count
            /// </summary>
            public int TotalPageCount { get; set; }
        }

        /// <summary>
        /// Represents a chapter in the download queue
        /// </summary>
        public class QueuedDownload
        {
            /// <summary>
            /// Chapter ID
            /// </summary>
            public int ChapterId { get; set; }
            
            /// <summary>
            /// Manga ID
            /// </summary>
            public int MangaId { get; set; }
            
            /// <summary>
            /// Chapter title
            /// </summary>
            public string ChapterTitle { get; set; } = string.Empty;
            
            /// <summary>
            /// Manga title
            /// </summary>
            public string MangaTitle { get; set; } = string.Empty;
        }
    }
}