package extension.bridge.logging

import java.util.logging.Formatter
import java.util.logging.Handler
import java.util.logging.Level
import java.util.logging.LogRecord

class AndroidCompatJulHandler : Handler() {
    override fun publish(record: LogRecord) {
        val compatLevel = record.toCompatLevel()
        if (!AndroidCompatLogForwarder.isLoggable(compatLevel)) return

        val tag = record.simpleLoggerName()
        val message = record.renderMessage()
        val throwable = record.thrown

        AndroidCompatLogForwarder.submit(compatLevel, tag, message, throwable)
    }

    override fun flush() { /* no-op */ }

    override fun close() { flush() }

    private fun LogRecord.simpleLoggerName(): String =
        loggerName?.substringAfterLast('.')?.takeIf { it.isNotBlank() } ?: "JUL"

    private fun LogRecord.renderMessage(): String =
        MESSAGE_FORMATTER.format(this).trimEnd().ifEmpty { message ?: "" }

    private fun LogRecord.toCompatLevel(): AndroidCompatLogLevel =
        when {
            level === Level.SEVERE      -> AndroidCompatLogLevel.ERROR
            level === Level.WARNING     -> AndroidCompatLogLevel.WARN
            level === Level.INFO        -> AndroidCompatLogLevel.INFO
            level === Level.FINE        -> AndroidCompatLogLevel.DEBUG
            level === Level.FINER ||
            level === Level.FINEST      -> AndroidCompatLogLevel.VERBOSE
            level === Level.CONFIG      -> AndroidCompatLogLevel.DEBUG
            else                        -> AndroidCompatLogLevel.VERBOSE
        }

    private companion object {
        val MESSAGE_FORMATTER: Formatter = object : Formatter() {
            override fun format(record: LogRecord): String = formatMessage(record)
        }
    }
}
