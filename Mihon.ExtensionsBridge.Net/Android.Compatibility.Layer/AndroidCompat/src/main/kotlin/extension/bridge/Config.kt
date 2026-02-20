package extension.bridge

import com.typesafe.config.Config
import com.typesafe.config.ConfigFactory
import com.typesafe.config.ConfigObject
import extension.bridge.ProxySettings
import extension.bridge.network.SystemProxyBridge
import kotlinx.coroutines.runBlocking
import xyz.nulldev.ts.config.ConfigModule
import xyz.nulldev.ts.config.GlobalConfigManager

internal const val SERVER_CONFIG_MODULE_NAME = "server"

class SettingsConfig(
    private val serverConfigProvider: () -> Config,
) : ConfigModule(serverConfigProvider) {

    data class Settings(
        var socksProxyEnabled: Boolean = false,
        var socksProxyVersion: Int = 5,
        var socksProxyHost: String = "",
        var socksProxyPort: String = "",
        var socksProxyUsername: String = "",
        var socksProxyPassword: String = "",
        var flareSolverrEnabled: Boolean = false,
        var flareSolverrUrl: String = "http://localhost:8191",
        var flareSolverrTimeout: Int = 60,
        var flareSolverrSessionName: String = "extension.bridge",
        var flareSolverrSessionTtl: Int = 15,
        var flareSolverrAsResponseFallback: Boolean = false,
        var interceptorOverrides: MutableMap<String, MutableMap<String, Boolean>> = mutableMapOf(),
    )

    private val defaults = Settings()

    fun getSettings(): Settings = serverConfigProvider().toSettings()

    suspend fun setSettings(settings: Settings): Settings {
        val current = getSettings()
        if (current == settings) {
            return settings
        }

        updateIfChanged("socksProxyEnabled", current.socksProxyEnabled, settings.socksProxyEnabled)
        updateIfChanged("socksProxyVersion", current.socksProxyVersion, settings.socksProxyVersion)
        updateIfChanged("socksProxyHost", current.socksProxyHost, settings.socksProxyHost)
        updateIfChanged("socksProxyPort", current.socksProxyPort, settings.socksProxyPort)
        updateIfChanged("socksProxyUsername", current.socksProxyUsername, settings.socksProxyUsername)
        updateIfChanged("socksProxyPassword", current.socksProxyPassword, settings.socksProxyPassword)

        updateIfChanged("flareSolverrEnabled", current.flareSolverrEnabled, settings.flareSolverrEnabled)
        updateIfChanged("flareSolverrUrl", current.flareSolverrUrl, settings.flareSolverrUrl)
        updateIfChanged("flareSolverrTimeout", current.flareSolverrTimeout, settings.flareSolverrTimeout)
        updateIfChanged("flareSolverrSessionName", current.flareSolverrSessionName, settings.flareSolverrSessionName)
        updateIfChanged("flareSolverrSessionTtl", current.flareSolverrSessionTtl, settings.flareSolverrSessionTtl)
        updateIfChanged("flareSolverrAsResponseFallback", current.flareSolverrAsResponseFallback, settings.flareSolverrAsResponseFallback)
        updateIfChanged("interceptorOverrides", current.interceptorOverrides, settings.interceptorOverrides)

        return settings
    }

    private suspend fun updateIfChanged(path: String, current: Any, updated: Any) {
        if (current == updated) {
            return
        }
        GlobalConfigManager.updateValue("$SERVER_CONFIG_MODULE_NAME.$path", updated)
    }

    private fun Config.toSettings(): Settings =
        Settings(
            socksProxyEnabled = booleanOrDefault("socksProxyEnabled", defaults.socksProxyEnabled),
            socksProxyVersion = intOrDefault("socksProxyVersion", defaults.socksProxyVersion),
            socksProxyHost = stringOrDefault("socksProxyHost", defaults.socksProxyHost),
            socksProxyPort = stringOrDefault("socksProxyPort", defaults.socksProxyPort),
            socksProxyUsername = stringOrDefault("socksProxyUsername", defaults.socksProxyUsername),
            socksProxyPassword = stringOrDefault("socksProxyPassword", defaults.socksProxyPassword),
            flareSolverrEnabled = booleanOrDefault("flareSolverrEnabled", defaults.flareSolverrEnabled),
            flareSolverrUrl = stringOrDefault("flareSolverrUrl", defaults.flareSolverrUrl),
            flareSolverrTimeout = intOrDefault("flareSolverrTimeout", defaults.flareSolverrTimeout),
            flareSolverrSessionName = stringOrDefault("flareSolverrSessionName", defaults.flareSolverrSessionName),
            flareSolverrSessionTtl = intOrDefault("flareSolverrSessionTtl", defaults.flareSolverrSessionTtl),
            flareSolverrAsResponseFallback = booleanOrDefault(
                "flareSolverrAsResponseFallback",
                defaults.flareSolverrAsResponseFallback,
            ),
            interceptorOverrides = nestedBooleanMapOrDefault("interceptorOverrides", defaults.interceptorOverrides),
        )

    companion object {
        fun register(getConfig: () -> Config): SettingsConfig {
            return SettingsConfig {
                val rootConfig = getConfig()
                if (rootConfig.hasPath(SERVER_CONFIG_MODULE_NAME)) {
                    rootConfig.getConfig(SERVER_CONFIG_MODULE_NAME)
                } else {
                    ConfigFactory.empty()
                }
            }
        }
    }
}

private val settingsModule: SettingsConfig by lazy { GlobalConfigManager.module() }
@Volatile private var afterSet: ((SettingsConfig.Settings) -> Unit)? = null

object Settings {
    @Volatile private var initialized = false
    private val lock = Any()
    private val runtime = SettingsConfig.Settings()

    private fun state(): SettingsConfig.Settings {
        if (initialized) {
            return runtime
        }

        return synchronized(lock) {
            if (!initialized) {
                runtime.updateFrom(settingsModule.getSettings())
                initialized = true
            }
            runtime
        }
    }

    var socksProxyEnabled: Boolean
        get() = state().socksProxyEnabled
        set(value) {
            state().socksProxyEnabled = value
        }

    var socksProxyVersion: Int
        get() = state().socksProxyVersion
        set(value) {
            state().socksProxyVersion = value
        }

    var socksProxyHost: String
        get() = state().socksProxyHost
        set(value) {
            state().socksProxyHost = value
        }

    var socksProxyPort: String
        get() = state().socksProxyPort
        set(value) {
            state().socksProxyPort = value
        }

    var socksProxyUsername: String
        get() = state().socksProxyUsername
        set(value) {
            state().socksProxyUsername = value
        }

    var socksProxyPassword: String
        get() = state().socksProxyPassword
        set(value) {
            state().socksProxyPassword = value
        }

    var flareSolverrEnabled: Boolean
        get() = state().flareSolverrEnabled
        set(value) {
            state().flareSolverrEnabled = value
        }

    var flareSolverrUrl: String
        get() = state().flareSolverrUrl
        set(value) {
            state().flareSolverrUrl = value
        }

    var flareSolverrTimeout: Int
        get() = state().flareSolverrTimeout
        set(value) {
            state().flareSolverrTimeout = value
        }

    var flareSolverrSessionName: String
        get() = state().flareSolverrSessionName
        set(value) {
            state().flareSolverrSessionName = value
        }

    var flareSolverrSessionTtl: Int
        get() = state().flareSolverrSessionTtl
        set(value) {
            state().flareSolverrSessionTtl = value
        }

    var flareSolverrAsResponseFallback: Boolean
        get() = state().flareSolverrAsResponseFallback
        set(value) {
            state().flareSolverrAsResponseFallback = value
        }

    var interceptorOverrides: MutableMap<String, MutableMap<String, Boolean>>
        get() = state().interceptorOverrides
        set(value) {
            state().interceptorOverrides = value
        }

    internal fun replaceWith(newSettings: SettingsConfig.Settings) {
        synchronized(lock) {
            runtime.updateFrom(newSettings)
            initialized = true
        }
    }

    fun snapshot(): SettingsConfig.Settings = state().deepCopy()

    fun toProxySettings(): ProxySettings =
        ProxySettings(
            proxyEnabled = socksProxyEnabled,
            socksProxyVersion = socksProxyVersion,
            proxyHost = socksProxyHost,
            proxyPort = socksProxyPort,
            proxyUsername = socksProxyUsername,
            proxyPassword = socksProxyPassword,
        )
}

fun getSettings(): SettingsConfig.Settings = Settings.snapshot()

fun setSettings(settings: SettingsConfig.Settings): SettingsConfig.Settings {
    val persisted = runBlocking { settingsModule.setSettings(settings) }
    Settings.replaceWith(persisted)
    SystemProxyBridge.apply(Settings.toProxySettings())
    afterSet?.invoke(persisted)
    return persisted
}

fun setSettings(update: (SettingsConfig.Settings) -> SettingsConfig.Settings): SettingsConfig.Settings {
    val updated = update(getSettings())
    return setSettings(updated)
}

internal fun onSettingsMutated(listener: (SettingsConfig.Settings) -> Unit) {
    afterSet = listener
}

private fun Config.booleanOrDefault(path: String, default: Boolean): Boolean =
    if (hasPath(path)) getBoolean(path) else default

private fun Config.intOrDefault(path: String, default: Int): Int =
    if (hasPath(path)) getInt(path) else default

private fun Config.stringOrDefault(path: String, default: String): String =
    if (hasPath(path)) getString(path) else default

private fun Config.nestedBooleanMapOrDefault(
    path: String,
    default: MutableMap<String, MutableMap<String, Boolean>>,
): MutableMap<String, MutableMap<String, Boolean>> {
    if (!hasPath(path)) {
        return default.deepCopy()
    }

    val rootObject: ConfigObject = getObject(path)
    val result = mutableMapOf<String, MutableMap<String, Boolean>>()

    for ((packageName, configValue) in rootObject) {
        val nestedMap =
            (configValue as? ConfigObject)
                ?.unwrapped()
                ?.mapValues { (_, value) ->
                    when (value) {
                        is Boolean -> value
                        else -> false
                    }
                }?.mapValues { it.value }?.toMutableMap()
                ?: mutableMapOf()

        result[packageName] = nestedMap
    }

    return result
}

private fun MutableMap<String, MutableMap<String, Boolean>>.deepCopy(): MutableMap<String, MutableMap<String, Boolean>> =
    mapValues { entry -> entry.value.toMutableMap() }.toMutableMap()

private fun SettingsConfig.Settings.updateFrom(other: SettingsConfig.Settings) {
    socksProxyEnabled = other.socksProxyEnabled
    socksProxyVersion = other.socksProxyVersion
    socksProxyHost = other.socksProxyHost
    socksProxyPort = other.socksProxyPort
    socksProxyUsername = other.socksProxyUsername
    socksProxyPassword = other.socksProxyPassword
    flareSolverrEnabled = other.flareSolverrEnabled
    flareSolverrUrl = other.flareSolverrUrl
    flareSolverrTimeout = other.flareSolverrTimeout
    flareSolverrSessionName = other.flareSolverrSessionName
    flareSolverrSessionTtl = other.flareSolverrSessionTtl
    flareSolverrAsResponseFallback = other.flareSolverrAsResponseFallback
    interceptorOverrides = other.interceptorOverrides.deepCopy()
}

private fun SettingsConfig.Settings.deepCopy(): SettingsConfig.Settings =
    copy(interceptorOverrides = interceptorOverrides.deepCopy())
