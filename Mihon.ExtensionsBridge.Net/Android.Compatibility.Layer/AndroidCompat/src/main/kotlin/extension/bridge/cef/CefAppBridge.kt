package extension.bridge.cef

import extension.bridge.logging.androidCompatLogger
import java.util.concurrent.locks.ReentrantLock
import kotlin.concurrent.withLock
import org.cef.CefApp

object CefAppBridge {
    private val logger = androidCompatLogger(CefAppBridge::class.java)
    private val lock = ReentrantLock()
    @Volatile private var sharedApp: CefApp? = null

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
