package xyz.nulldev.ts.config

/*
 * Copyright (C) Contributors to the Suwayomi project
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

import com.typesafe.config.Config
import com.typesafe.config.ConfigFactory
import com.typesafe.config.ConfigObject
import com.typesafe.config.ConfigValue
import com.typesafe.config.parser.ConfigDocument
import com.typesafe.config.parser.ConfigDocumentFactory
import io.github.config4k.toConfig
import xyz.nulldev.ts.config.logging.BridgeLogger
import xyz.nulldev.ts.config.logging.ConfigLogger
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock

/**
 * Manages app config.
 */
open class ConfigManager {
    internal val logger: ConfigLogger = BridgeLogger("ConfigManager")
    private val generatedModules = mutableMapOf<Class<out ConfigModule>, ConfigModule>()
    private val userConfigFile: java.io.File? = null
    private val compatReferenceConfig by lazy {
        ConfigFactory.parseString(COMPAT_REFERENCE_FALLBACK)
    }
    private val serverReferenceConfig by lazy {
        ConfigFactory.parseString(SERVER_REFERENCE_FALLBACK)
    }
    private var internalConfig = loadConfigs()
    val config: Config
        get() = internalConfig

    // Public read-only view of modules
    val loadedModules: Map<Class<out ConfigModule>, ConfigModule>
        get() = generatedModules

    private val mutex = Mutex()

    /**
     * Get a config module
     */
    inline fun <reified T : ConfigModule> module(): T = loadedModules[T::class.java] as T

    /**
     * Get a config module (Java API)
     */
    @Suppress("UNCHECKED_CAST")
    fun <T : ConfigModule> module(type: Class<T>): T = loadedModules[type] as T

    private fun getUserConfig(): Config = ConfigFactory.empty()

    /**
     * Load configs
     */
    fun loadConfigs(): Config {
        // Load reference configs
        val compatConfig = compatReferenceConfig
        val serverConfig = serverReferenceConfig
        val baseConfig =
            ConfigFactory.parseMap(
                mapOf(
                    // override AndroidCompat's rootDir
                    "androidcompat.rootDir" to "$ApplicationRootDir/android-compat",
                ),
            )

        // Load user config
        val userConfig = getUserConfig()

        val config =
            ConfigFactory
                .empty()
                .withFallback(baseConfig)
                .withFallback(userConfig)
                .withFallback(compatConfig)
                .withFallback(serverConfig)
                .resolve()

        // set log level early
        if (debugLogsEnabled(config)) {
            setLogLevelFor(BASE_LOGGER_NAME, "DEBUG")
        }

        return config
    }

    fun registerModule(module: ConfigModule) {
        generatedModules[module.javaClass] = module
    }

    fun registerModules(vararg modules: ConfigModule) {
        modules.forEach {
            registerModule(it)
        }
    }

    private fun updateUserConfigFile(
        path: String,
        value: ConfigValue,
    ) {
        val file = userConfigFile ?: return
        val userConfigDoc = ConfigDocumentFactory.parseFile(file)
        val updatedConfigDoc = userConfigDoc.withValue(path, value)
        val newFileContent = updatedConfigDoc.render()
        file.writeText(newFileContent)
    }

    suspend fun updateValue(
        path: String,
        value: Any,
    ) {
        mutex.withLock {
            val configValue = value.toConfig("internal").getValue("internal")

            updateUserConfigFile(path, configValue)
            internalConfig = internalConfig.withValue(path, configValue)
        }
    }

    private fun createConfigDocumentFromReference(): ConfigDocument {
        val rendered = serverReferenceConfig.root().render()
        return ConfigDocumentFactory.parseString(rendered)
    }

    fun resetUserConfig(): ConfigDocument {
        val serverConfigDoc = createConfigDocumentFromReference()

        userConfigFile?.writeText(serverConfigDoc.render())
        getUserConfig().entrySet().forEach { internalConfig = internalConfig.withValue(it.key, it.value) }

        return serverConfigDoc
    }

    /**
     * Makes sure the "UserConfig" is up-to-date.
     *
     *  - Adds missing settings
     *  - Migrates deprecated settings
     *  - Removes outdated settings
     */
    fun updateUserConfig(migrate: ConfigDocument.(Config) -> ConfigDocument) {
        val serverConfig = serverReferenceConfig
        val userConfig = getUserConfig()

        // NOTE: if more than 1 dot is included, that's a nested setting, which we need to filter out here
        val refKeys =
            serverConfig.root().entries.flatMap {
                (it.value as? ConfigObject)?.entries?.map { e -> "${it.key}.${e.key}" }.orEmpty()
            }
        val hasMissingSettings = refKeys.any { !userConfig.hasPath(it) }
        val hasOutdatedSettings = userConfig.entrySet().any { !refKeys.contains(it.key) && it.key.count { c -> c == '.' } <= 1 }

        val isUserConfigOutdated = hasMissingSettings || hasOutdatedSettings
        if (!isUserConfigOutdated) {
            return
        }

        logger.debug {
            "user config is out of date, updating... (missingSettings= $hasMissingSettings, outdatedSettings= $hasOutdatedSettings)"
        }

        var newUserConfigDoc: ConfigDocument = createConfigDocumentFromReference()
        userConfig
            .entrySet()
            .filter {
                serverConfig.hasPath(
                    it.key,
                ) ||
                    it.key.count { c -> c == '.' } > 1
            }.forEach { newUserConfigDoc = newUserConfigDoc.withValue(it.key, it.value) }

        newUserConfigDoc =
            migrate(newUserConfigDoc, internalConfig)

        userConfigFile?.writeText(newUserConfigDoc.render())
        getUserConfig().entrySet().forEach { internalConfig = internalConfig.withValue(it.key, it.value) }
    }

    fun getRedactedConfig(nonPrivacySafeKeys: List<String>): Config {
        val entries =
            config.entrySet().associate { entry ->
                val key = entry.key
                val value =
                    if (nonPrivacySafeKeys.any { key.split(".").getOrNull(1) == it }) {
                        "[REDACTED]"
                    } else {
                        entry.value.unwrapped()
                    }

                key to value
            }

        return ConfigFactory.parseMap(entries)
    }
}

object GlobalConfigManager : ConfigManager()

private val COMPAT_REFERENCE_FALLBACK =
    """
    #
    androidcompat.rootDir = androidcompat-root
    android.files.rootDir = ${'$'}{androidcompat.rootDir}/appdata
    android.files.externalStorageDir = ${'$'}{androidcompat.rootDir}/extappdata
    android.files.dataDir = ${'$'}{android.files.rootDir}/data
    android.files.filesDir = ${'$'}{android.files.rootDir}/files
    android.files.cacheDir = ${'$'}{android.files.rootDir}/cache
    android.files.codeCacheDir = ${'$'}{android.files.rootDir}/code_cache
    android.files.noBackupFilesDir = ${'$'}{android.files.rootDir}/no_backup
    android.files.databasesDir = ${'$'}{android.files.rootDir}/databases
    android.files.prefsDir = ${'$'}{android.files.rootDir}/shared_prefs
    android.files.externalFilesDirs = [${'$'}{android.files.externalStorageDir}/files]
    android.files.obbDirs = [${'$'}{android.files.externalStorageDir}/obb]
    android.files.externalCacheDirs = [${'$'}{android.files.externalStorageDir}/cache]
    android.files.externalMediaDirs = [${'$'}{android.files.externalStorageDir}/media]
    android.files.downloadCacheDir = ${'$'}{android.files.externalStorageDir}/downloadCache
    android.files.packageDir = ${'$'}{androidcompat.rootDir}/android-compat/packages
    android.app.packageName = eu.kanade.tachiyomi
    android.app.debug = true
    android.system.isDebuggable = true
    #
    """.trimIndent()

private val SERVER_REFERENCE_FALLBACK =
    """
    #
    server.socksProxyEnabled = false
    server.socksProxyVersion = 5
    server.socksProxyHost = ""
    server.socksProxyPort = ""
    server.socksProxyUsername = ""
    server.socksProxyPassword = ""
    server.flareSolverrEnabled = false
    server.flareSolverrUrl = "http://localhost:8191"
    server.flareSolverrTimeout = 60 # time in seconds
    server.flareSolverrSessionName = "extension.bridge"
    server.flareSolverrSessionTtl = 15 # time in minutes
    server.flareSolverrAsResponseFallback = false
    server.debugLogsEnabled = false
    #
    """.trimIndent()
