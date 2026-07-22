package eu.kanade.tachiyomi.source.model

@Suppress("UNUSED")
data class SMangaUpdate(
    val manga: SManga,
    val chapters: List<SChapter>,
)
