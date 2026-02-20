package extension.bridge.logging

/**
 * Lightweight logger facade that forwards AndroidCompat logs to the .NET host.
 */
class AndroidCompatLogger private constructor(private val tag: String) {
    companion object {
        @JvmStatic
        fun withTag(tag: String): AndroidCompatLogger = AndroidCompatLogger(tag)

        @JvmStatic
        fun forClass(clazz: Class<*>): AndroidCompatLogger = withTag(clazz.simpleName ?: clazz.name)
    }

    fun verbose(message: () -> String) {
        emit(AndroidCompatLogLevel.VERBOSE, message(), null)
    }

    fun verbose(message: String) {
        emit(AndroidCompatLogLevel.VERBOSE, message, null)
    }

    fun verbose(message: String, throwable: Throwable?) {
        emit(AndroidCompatLogLevel.VERBOSE, message, throwable)
    }

    fun debug(message: () -> String) {
        emit(AndroidCompatLogLevel.DEBUG, message(), null)
    }

    fun debug(message: String) {
        emit(AndroidCompatLogLevel.DEBUG, message, null)
    }

    fun debug(message: String, throwable: Throwable?) {
        emit(AndroidCompatLogLevel.DEBUG, message, throwable)
    }

    fun debug(throwable: Throwable, message: () -> String) {
        emit(AndroidCompatLogLevel.DEBUG, message(), throwable)
    }

    fun info(message: () -> String) {
        emit(AndroidCompatLogLevel.INFO, message(), null)
    }

    fun info(message: String) {
        emit(AndroidCompatLogLevel.INFO, message, null)
    }

    fun info(message: String, throwable: Throwable?) {
        emit(AndroidCompatLogLevel.INFO, message, throwable)
    }

    fun info(throwable: Throwable, message: () -> String) {
        emit(AndroidCompatLogLevel.INFO, message(), throwable)
    }

    fun warn(message: () -> String) {
        emit(AndroidCompatLogLevel.WARN, message(), null)
    }

    fun warn(message: String) {
        emit(AndroidCompatLogLevel.WARN, message, null)
    }

    fun warn(message: String, throwable: Throwable?) {
        emit(AndroidCompatLogLevel.WARN, message, throwable)
    }

    fun warn(throwable: Throwable, message: () -> String) {
        emit(AndroidCompatLogLevel.WARN, message(), throwable)
    }

    fun error(message: () -> String) {
        emit(AndroidCompatLogLevel.ERROR, message(), null)
    }

    fun error(message: String) {
        emit(AndroidCompatLogLevel.ERROR, message, null)
    }

    fun error(message: String, throwable: Throwable?) {
        emit(AndroidCompatLogLevel.ERROR, message, throwable)
    }

    fun error(throwable: Throwable, message: () -> String) {
        emit(AndroidCompatLogLevel.ERROR, message(), throwable)
    }

    fun wtf(message: () -> String) {
        emit(AndroidCompatLogLevel.ASSERT, message(), null)
    }

    fun wtf(message: String) {
        emit(AndroidCompatLogLevel.ASSERT, message, null)
    }

    fun wtf(message: String, throwable: Throwable?) {
        emit(AndroidCompatLogLevel.ASSERT, message, throwable)
    }

    fun wtf(throwable: Throwable, message: () -> String) {
        emit(AndroidCompatLogLevel.ASSERT, message(), throwable)
    }

    private fun emit(level: AndroidCompatLogLevel, message: String, throwable: Throwable?) {
        if (!AndroidCompatLogForwarder.isLoggable(level)) {
            return
        }
        AndroidCompatLogForwarder.submit(level, tag, message, throwable)
    }
}

@JvmName("androidCompatLogger")
fun androidCompatLogger(tag: String): AndroidCompatLogger = AndroidCompatLogger.withTag(tag)

fun androidCompatLogger(clazz: Class<*>): AndroidCompatLogger = AndroidCompatLogger.forClass(clazz)
