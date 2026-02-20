package eu.kanade.tachiyomi


/**
 * Used by extensions.
 *
 * @since extension-lib 1.3
 */
object AppInfo {
    /**
     *
     * should be something like 74
     *
     * @since extension-lib 1.3
     */
    fun getVersionCode() = 10

    /**
     * should be something like "0.13.1"
     *
     * @since extension-lib 1.3
     */
    fun getVersionName() = "1.0"

    /**
     * A list of supported image MIME types by the reader.
     * e.g. ["image/jpeg", "image/png", ...]
     *
     * @since extension-lib 1.5
     */
    fun getSupportedImageMimeTypes(): List<String> = ImageType.entries.map { it.mime }

    enum class ImageType(
        val mime: String,
        val extension: String,
    ) {
        AVIF("image/avif", "avif"),
        GIF("image/gif", "gif"),
        HEIF("image/heif", "heif"),
        JPEG("image/jpeg", "jpg"),
        JXL("image/jxl", "jxl"),
        PNG("image/png", "png"),
        WEBP("image/webp", "webp"),
    }
}
