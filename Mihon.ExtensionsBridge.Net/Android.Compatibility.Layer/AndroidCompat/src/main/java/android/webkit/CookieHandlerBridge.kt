package android.webkit

import eu.kanade.tachiyomi.network.NetworkHelper
import extension.bridge.logging.androidCompatLogger
import uy.kohesive.injekt.Injekt
import uy.kohesive.injekt.api.get
import java.net.CookieHandler
import java.net.CookiePolicy
import java.net.CookieManager as JavaCookieManager

internal object CookieHandlerBridge {
    private val logger = androidCompatLogger(CookieHandlerBridge::class.java)

    @JvmStatic
    fun ensureDefaultHandler(): JavaCookieManager {
        val current = CookieHandler.getDefault()
        if (current is JavaCookieManager) {
            return current
        }

        val manager =
            runCatching {
                val networkHelper = Injekt.get<NetworkHelper>()
                JavaCookieManager(networkHelper.cookieStore, CookiePolicy.ACCEPT_ALL)
            }.getOrElse { throwable ->
                logger.warn(throwable) {
                    "CookieHandler default missing; using local, non-persistent cookie store"
                }
                JavaCookieManager(null, CookiePolicy.ACCEPT_ALL)
            }

        CookieHandler.setDefault(manager)
        return manager
    }
}
