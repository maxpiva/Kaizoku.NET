package extension.bridge.logging

import java.io.PrintWriter
import java.io.StringWriter
import java.util.concurrent.atomic.AtomicReference

/**
 * Bridge between the AndroidCompat layer and the .NET host logger.
 * Until a sink is registered, log events are ignored silently.
 */
object AndroidCompatLogForwarder {
    private val sinkRef = AtomicReference<AndroidCompatLogSink?>(null)
    private val minimumLevel = AtomicReference(AndroidCompatLogLevel.INFO)

    fun registerSink(sink: AndroidCompatLogSink) {
        sinkRef.set(sink)
    }

    fun clearSink() {
        sinkRef.set(null)
    }

    fun setMinimumLevel(level: AndroidCompatLogLevel) {
        minimumLevel.set(level)
    }

    fun getMinimumLevel(): AndroidCompatLogLevel = minimumLevel.get()

    fun isLoggable(level: AndroidCompatLogLevel): Boolean {
        val sink = sinkRef.get()
        return sink != null && level.priority >= minimumLevel.get().priority
    }

    fun submit(level: AndroidCompatLogLevel, tag: String, message: String, throwable: Throwable? = null) {
        val sink = sinkRef.get()
        if (sink == null) {
            return
        }
        if (level.priority < minimumLevel.get().priority) {
            return
        }
        val renderedThrowable = throwable?.let(::renderThrowable)
        sink.log(level, tag, message, renderedThrowable)
    }

    private fun renderThrowable(throwable: Throwable): String {
        val sw = StringWriter()
        val pw = PrintWriter(sw, true)
        throwable.printStackTrace(pw)
        return sw.buffer.toString()
    }
}

/** Android log levels mirrored for .NET consumption. */
enum class AndroidCompatLogLevel(val priority: Int) {
    VERBOSE(2),
    DEBUG(3),
    INFO(4),
    WARN(5),
    ERROR(6),
    ASSERT(7);

    companion object {
        fun fromPriority(priority: Int): AndroidCompatLogLevel = when (priority) {
            ASSERT.priority -> ASSERT
            DEBUG.priority -> DEBUG
            ERROR.priority -> ERROR
            INFO.priority -> INFO
            WARN.priority -> WARN
            else -> VERBOSE
        }
    }
}

fun interface AndroidCompatLogSink {
    fun log(level: AndroidCompatLogLevel, tag: String, message: String, throwable: String?)
}
