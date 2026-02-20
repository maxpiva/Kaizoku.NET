package extension.bridge.network

import extension.bridge.ProxySettings
import extension.bridge.logging.androidCompatLogger
import java.net.Authenticator
import java.net.PasswordAuthentication
import java.util.concurrent.locks.ReentrantLock
import kotlin.concurrent.withLock

object SystemProxyBridge {
    private const val SIGNATURE_KEY = "extension.bridge.socksProxy.signature"
    private val lock = ReentrantLock()
    private val logger = androidCompatLogger(SystemProxyBridge::class.java)

    fun apply(settings: ProxySettings) {
        lock.withLock {
            val signature = signature(settings)
            val currentSignature = System.getProperty(SIGNATURE_KEY)
            if (currentSignature == signature) {
                return
            }

            if (!settings.proxyEnabled) {
                clearProxy()
                return
            }

            System.setProperty("socksProxyHost", settings.proxyHost)
            System.setProperty("socksProxyPort", settings.proxyPort)
            System.setProperty("socksProxyVersion", settings.socksProxyVersion.toString())

            val authenticator =
                if (settings.proxyUsername.isNotBlank() || settings.proxyPassword.isNotBlank()) {
                    object : Authenticator() {
                        override fun getPasswordAuthentication(): PasswordAuthentication? {
                            return if (requestingProtocol.startsWith("SOCKS", ignoreCase = true)) {
                                PasswordAuthentication(settings.proxyUsername, settings.proxyPassword.toCharArray())
                            } else {
                                null
                            }
                        }
                    }
                } else {
                    null
                }
            Authenticator.setDefault(authenticator)
            System.setProperty(SIGNATURE_KEY, signature)
            logger.info { "Applied SOCKS proxy configuration: ${settings.proxyHost}:${settings.proxyPort}" }
        }
    }

    private fun clearProxy() {
        val currentSignature = System.getProperty(SIGNATURE_KEY) ?: return
        System.clearProperty("socksProxyHost")
        System.clearProperty("socksProxyPort")
        System.clearProperty("socksProxyVersion")
        Authenticator.setDefault(null)
        System.clearProperty(SIGNATURE_KEY)
        logger.info { "Cleared SOCKS proxy configuration (signature=$currentSignature)" }
    }

    private fun signature(settings: ProxySettings): String {
        if (!settings.proxyEnabled) return "disabled"
        return buildString {
            append(settings.proxyEnabled)
            append('|').append(settings.socksProxyVersion)
            append('|').append(settings.proxyHost)
            append('|').append(settings.proxyPort)
            append('|').append(settings.proxyUsername)
            append('|').append(settings.proxyPassword.hashCode())
        }
    }
}
