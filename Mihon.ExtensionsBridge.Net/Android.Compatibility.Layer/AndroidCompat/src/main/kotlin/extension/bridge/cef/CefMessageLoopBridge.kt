package extension.bridge.cef

import extension.bridge.logging.AndroidCompatLogger
import java.util.concurrent.atomic.AtomicBoolean
import org.cef.CefApp

object CefMessageLoopBridge {
    private val logger = AndroidCompatLogger.forClass(CefMessageLoopBridge::class.java)
    private val running = AtomicBoolean(false)
    @Volatile private var loopThread: Thread? = null

    fun start(app: CefApp) {
        if (!running.compareAndSet(false, true)) return

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
