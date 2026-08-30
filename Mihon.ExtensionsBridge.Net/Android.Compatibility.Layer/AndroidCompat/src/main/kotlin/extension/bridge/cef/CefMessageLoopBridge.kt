package extension.bridge.cef

import extension.bridge.logging.AndroidCompatLogger
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicLong
import org.cef.CefApp
import xyz.nulldev.androidcompat.webkit.WebViewPool

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

    private const val SWEEP_INTERVAL_MS = 30_000L
    private val lastSweepMs = AtomicLong(0)

    /**
     * Performs one iteration of CEF message loop work.
     * Safe to call from any thread that is attached to IKVM (e.g. Avalonia UI thread).
     *
     * Every call also runs a time-gated idle sweep of the [WebViewPool]. This is the ONLY
     * watchdog mechanism, and it deliberately rides the existing safe pump path:
     *  - Docker/headless: the IKVM-attached `cef-message-loop` daemon thread.
     *  - Desktop (Avalonia): the UI thread via `CefPumpBridge.PumpWork`.
     *
     * The sweep itself is pure bookkeeping (lock-protected LRU/cap math inside the pool); actual
     * `WebView.destroy()` is posted to the Android main looper by the pool, so no JNI call is ever
     * made from a CEF native or unattached thread.
     */
    fun pumpWork(app: CefApp, delayMs: Long = 0L) {
        try {
            app.doMessageLoopWork(delayMs)
        } catch (t: Throwable) {
            logger.warn { "Error inside CEF message pump: ${'$'}t" }
        }

        // Time-gated watchdog sweep (at most once per SWEEP_INTERVAL_MS).
        val now = System.currentTimeMillis()
        val last = lastSweepMs.get()
        if (now - last >= SWEEP_INTERVAL_MS && lastSweepMs.compareAndSet(last, now)) {
            runCatching {
                WebViewPool.sweep()
            }.onFailure { t ->
                logger.warn { "WebViewPool sweep failed: ${'$'}t" }
            }
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
                        pumpWork(app, 0L)
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
