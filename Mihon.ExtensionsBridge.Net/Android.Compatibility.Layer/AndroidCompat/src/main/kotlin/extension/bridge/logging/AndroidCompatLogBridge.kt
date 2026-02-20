package extension.bridge.logging

/**
 * Static helpers consumed by the .NET host to control AndroidCompat logging.
 */
object AndroidCompatLogBridge {
    @JvmStatic
    fun registerSink(sink: AndroidCompatLogSink) {
        AndroidCompatLogForwarder.registerSink(sink)
    }

    @JvmStatic
    fun clearSink() {
        AndroidCompatLogForwarder.clearSink()
    }

    @JvmStatic
    fun setMinimumLevel(level: AndroidCompatLogLevel) {
        AndroidCompatLogForwarder.setMinimumLevel(level)
    }

    @JvmStatic
    fun setMinimumLevelByName(levelName: String) {
        AndroidCompatLogForwarder.setMinimumLevel(levelName.toAndroidCompatLevel())
    }

    @JvmStatic
    fun setMinimumLevelByPriority(priority: Int) {
        AndroidCompatLogForwarder.setMinimumLevel(AndroidCompatLogLevel.fromPriority(priority))
    }

    @JvmStatic
    fun getMinimumLevel(): AndroidCompatLogLevel = AndroidCompatLogForwarder.getMinimumLevel()

    private fun String?.toAndroidCompatLevel(): AndroidCompatLogLevel {
        if (this.isNullOrBlank()) {
            return AndroidCompatLogLevel.INFO
        }
        return runCatching {
            AndroidCompatLogLevel.valueOf(this.uppercase())
        }.getOrDefault(AndroidCompatLogLevel.INFO)
    }
}
