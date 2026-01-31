package extension.bridge

/**
 * Holds SOCKS proxy configuration mirrored from [SettingsConfig.Settings].
 */
data class ProxySettings(
    val proxyEnabled: Boolean,
    val socksProxyVersion: Int,
    val proxyHost: String,
    val proxyPort: String,
    val proxyUsername: String,
    val proxyPassword: String,
)

fun SettingsConfig.Settings.toProxySettings(): ProxySettings =
    ProxySettings(
        proxyEnabled = socksProxyEnabled,
        socksProxyVersion = socksProxyVersion,
        proxyHost = socksProxyHost,
        proxyPort = socksProxyPort,
        proxyUsername = socksProxyUsername,
        proxyPassword = socksProxyPassword,
    )
