using Microsoft.Extensions.Logging;
using Mihon.ExtensionsBridge.Core.Utilities;
using Mihon.ExtensionsBridge.Models.Abstractions;
using RensaioBackend.Utils;
using System;


namespace RensaioTray.Utils
{
    public class CefTimer : IStartCefTimer, IDisposable
    {
        private bool _isDisposed = false;
        private readonly ILogger _logger;
        // CEF message pump timer — drives CefApp.doMessageLoopWork() from the
        // Avalonia UI thread instead of relying on a Java daemon thread that can
        // trigger reverse-JNI crashes from CEF native threads.
        private Avalonia.Threading.DispatcherTimer? _cefPumpTimer;


        public CefTimer(ILogger<CefTimer> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Starts an Avalonia DispatcherTimer that drives CEF's message loop
        /// from the UI thread at 10ms intervals. This replaces the internal
        /// Java daemon thread, avoiding reverse-JNI crashes from CEF native
        /// threads entering IKVM's JNI without a valid GetEnv() attachment.
        /// </summary>
        public void StartCefPumpTimer()
        {

              
            if (_cefPumpTimer != null)
                return;
            Avalonia.Threading.Dispatcher.UIThread.Invoke(() =>
            {
                if (!CefPumpBridge.IsAvailable)
                {
                    // JCEF not initialized (e.g. Docker/headless) — nothing to pump
                    return;
                }

                _cefPumpTimer = new Avalonia.Threading.DispatcherTimer(
                    TimeSpan.FromMilliseconds(10),
                    Avalonia.Threading.DispatcherPriority.Background,
                    (sender, e) =>
                    {
                        try
                        {
                            CefPumpBridge.PumpWork();
                        }
                        catch (Exception ex)
                        {
                            FallbackCrashLogger.WriteException(ex, "TRAY CEF PUMP ERROR");
                        }
                    });

                _cefPumpTimer.Start();
                _logger.LogInformation("CEF external message pump timer started (DispatcherTimer, 10ms interval)");

            });

        }



        public void Dispose()
        {
            if (!_isDisposed)
            {
                /// <summary>
                /// Stops the CEF message pump timer.
                /// </summary>
                if (_cefPumpTimer == null)
                    return;

                _cefPumpTimer.Stop();
                _cefPumpTimer = null;
                CefPumpBridge.Invalidate();
                _logger.LogInformation("CEF external message pump timer stopped");

                // Dispose of unmanaged resources here
                _isDisposed = true;
            }
        }
    }
}
