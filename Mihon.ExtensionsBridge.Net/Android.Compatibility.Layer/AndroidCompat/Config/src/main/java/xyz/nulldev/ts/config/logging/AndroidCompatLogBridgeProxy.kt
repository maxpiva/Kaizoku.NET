package xyz.nulldev.ts.config.logging

interface ConfigLogger {
    fun debug(message: () -> String)
    fun debug(throwable: Throwable, message: () -> String)
    fun warn(message: () -> String)
}

internal object AndroidCompatLogBridgeProxy {
    private const val BRIDGE_CLASS_NAME = "xyz.nulldev.androidcompat.logging.AndroidCompatLogBridge"
    private const val FORWARDER_CLASS_NAME = "xyz.nulldev.androidcompat.logging.AndroidCompatLogForwarder"
    private const val LEVEL_CLASS_NAME = "xyz.nulldev.androidcompat.logging.AndroidCompatLogLevel"

    private val bridgeClass: Class<*>? = runCatching { Class.forName(BRIDGE_CLASS_NAME) }.getOrNull()
    private val forwarderClass: Class<*>? = runCatching { Class.forName(FORWARDER_CLASS_NAME) }.getOrNull()
    private val levelClass: Class<*>? = runCatching { Class.forName(LEVEL_CLASS_NAME) }.getOrNull()

    private val setMinimumLevelByNameMethod = bridgeClass?.getMethod("setMinimumLevelByName", String::class.java)
    private val setMinimumLevelMethod = forwarderClass?.getMethod("setMinimumLevel", levelClass)
    private val submitMethod = forwarderClass?.getMethod(
        "submit",
        levelClass,
        String::class.java,
        String::class.java,
        Throwable::class.java,
    )
    private val levelValueOfMethod = levelClass?.getMethod("valueOf", String::class.java)

    fun setMinimumLevelName(levelName: String) {
        if (!invokeSetMinimumLevelByName(levelName)) {
            invokeSetMinimumLevel(levelName)
        }
    }

    fun log(levelName: String, tag: String, message: String, throwable: Throwable? = null): Boolean {
        val submit = submitMethod ?: return false
        val level = levelFromName(levelName) ?: return false
        return runCatching {
            submit.invoke(null, level, tag, message, throwable)
            true
        }.getOrDefault(false)
    }

    private fun invokeSetMinimumLevelByName(levelName: String): Boolean {
        val method = setMinimumLevelByNameMethod ?: return false
        return runCatching {
            method.invoke(null, levelName)
            true
        }.getOrDefault(false)
    }

    private fun invokeSetMinimumLevel(levelName: String): Boolean {
        val method = setMinimumLevelMethod ?: return false
        val level = levelFromName(levelName) ?: return false
        return runCatching {
            method.invoke(null, level)
            true
        }.getOrDefault(false)
    }

    private fun levelFromName(levelName: String): Any? {
        val method = levelValueOfMethod ?: return null
        return runCatching { method.invoke(null, levelName) }.getOrNull()
    }

}

internal class BridgeLogger(private val tag: String) : ConfigLogger {
    override fun debug(message: () -> String) {
        log("DEBUG", message, null)
    }

    override fun debug(throwable: Throwable, message: () -> String) {
        log("DEBUG", message, throwable)
    }

    override fun warn(message: () -> String) {
        log("WARN", message, null)
    }

    private fun log(levelName: String, messageSupplier: () -> String, throwable: Throwable?) {
        val message = runCatching(messageSupplier).getOrElse { error ->
            fallbackLog("ERROR", "Log supplier failed: ${error.message}", error)
            return
        }

        if (AndroidCompatLogBridgeProxy.log(levelName, tag, message, throwable)) {
            return
        }

        fallbackLog(levelName, message, throwable)
    }

    private fun fallbackLog(levelName: String, message: String, throwable: Throwable?) {
        val base = "[$levelName][$tag] $message"
        if (throwable != null) {
            System.err.println(base)
            throwable.printStackTrace(System.err)
        } else {
            println(base)
        }
    }
}
