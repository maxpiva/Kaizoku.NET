package extension.bridge.cef

import extension.bridge.logging.androidCompatLogger
import java.util.concurrent.locks.ReentrantLock
import kotlin.concurrent.withLock
import org.cef.CefApp

object CefAppBridge {
    private val logger = androidCompatLogger(CefAppBridge::class.java)
    private val lock = ReentrantLock()
    @Volatile private var sharedApp: CefApp? = null

    /**
     * Returns the existing shared CefApp instance, or null if not yet initialized.
     */
    fun getSharedApp(): CefApp? = sharedApp

    /**
     * Enables or disables external message pump mode.
     * Must be called before [getOrCreate] to have effect.
     * When enabled, the internal CefMessageLoopBridge daemon thread is not started;
     * the host must call [CefMessageLoopBridge.pumpWork] from an external timer.
     */
    fun setExternalPump(enabled: Boolean) {
        CefMessageLoopBridge.setExternalPump(enabled)
    }

    fun getOrCreate(initializer: () -> CefApp): CefApp {
        sharedApp?.let { return it }

        return lock.withLock {
            sharedApp?.let { return it }

            val app = initializer()
            sharedApp = app
            logger.info { "Initialized shared CefApp instance" }
            CefMessageLoopBridge.start(app)
            app
        }
    }
}
