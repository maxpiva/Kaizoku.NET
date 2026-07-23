package extension.bridge.cef

import extension.bridge.logging.AndroidCompatLogger
import java.util.concurrent.atomic.AtomicBoolean
import org.cef.CefApp

object CefMessageLoopBridge {
    private val logger = AndroidCompatLogger.forClass(CefMessageLoopBridge::class.java)
    private val running = AtomicBoolean(false)
    @Volatile private var loopThread: Thread? = null
    @Volatile private var externalPumpEnabled = false

    /**
     * Enables or disables external pump mode.
     * When enabled, [start] does not create the internal daemon thread;
     * the caller must drive the message pump via [pumpWork].
     */
    fun setExternalPump(enabled: Boolean) {
        externalPumpEnabled = enabled
    }

    /**
     * Performs one iteration of CEF message loop work.
     * Safe to call from any thread that is attached to IKVM (e.g. Avalonia UI thread).
     */
    fun pumpWork(app: CefApp, delayMs: Long = 0L) {
        try {
            app.doMessageLoopWork(delayMs)
        } catch (t: Throwable) {
            logger.warn { "Error inside CEF message loop pump: ${'$'}t" }
        }
    }

    fun start(app: CefApp) {
        if (!running.compareAndSet(false, true)) return

        if (externalPumpEnabled) {
            logger.info { "External pump mode enabled — skipping internal CEF message loop thread" }
            return
        }

        loopThread =
            Thread({
                logger.info { "Starting CEF message loop" }
                try {
                    while (running.get()) {
                        app.doMessageLoopWork(0L)
                        Thread.sleep(10L)
                    }
                } catch (interrupted: InterruptedException) {
                    Thread.currentThread().interrupt()
                } catch (t: Throwable) {
                    logger.warn { "Error inside CEF message loop: ${'$'}t" }
                } finally {
                    logger.info { "CEF message loop stopped" }
                }
            }).apply {
                isDaemon = true
                name = "cef-message-loop"
                start()
            }
    }

    fun stop() {
        if (!running.compareAndSet(true, false)) return

        loopThread?.interrupt()
        try {
            loopThread?.join(1_000L)
        } catch (_: InterruptedException) {
            Thread.currentThread().interrupt()
        } finally {
            loopThread = null
        }
    }
}
