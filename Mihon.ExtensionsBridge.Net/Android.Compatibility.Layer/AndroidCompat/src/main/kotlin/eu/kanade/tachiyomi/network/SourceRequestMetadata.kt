package eu.kanade.tachiyomi.network

import okhttp3.Request

data class SourceRequestMetadata(
    val packageName: String,
    val sourceName: String,
)

fun Request.sourceMetadata(): SourceRequestMetadata? = tag(SourceRequestMetadata::class.java)

fun Request.withSourceMetadata(metadata: SourceRequestMetadata): Request {
    val existing = tag(SourceRequestMetadata::class.java)
    if (existing === metadata) {
        return this
    }
    return newBuilder()
        .tag(SourceRequestMetadata::class.java, metadata)
        .build()
}
