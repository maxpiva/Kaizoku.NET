package extension.bridge

import extension.bridge.ChildFirstURLClassLoader
import java.io.File
import java.net.URL
import java.net.URLClassLoader
import java.nio.file.Files
import java.nio.file.Path
import javax.xml.parsers.DocumentBuilderFactory
import kotlin.io.path.Path
import kotlin.io.path.relativeTo
import eu.kanade.tachiyomi.source.CatalogueSource
import eu.kanade.tachiyomi.source.Source
import eu.kanade.tachiyomi.source.SourceFactory

object Extensions {
   
    fun loadExtensionSources(
        jarPath: String,
        className: String,
    ): List<CatalogueSource> {
        val extensionMainClassInstance = loadExtension(jarPath, className)
        val sources: List<CatalogueSource> =
            when (extensionMainClassInstance) {
                is Source -> listOf(extensionMainClassInstance)
                is SourceFactory -> extensionMainClassInstance.createSources()
                else -> throw RuntimeException("Unknown source class type! ${extensionMainClassInstance.javaClass}")
            }.map { it as CatalogueSource }
        return sources
    }

    /**
     * loads the extension main class called [className] from the jar located at [jarPath]
     * It may return an instance of HttpSource or SourceFactory depending on the extension.
     */
    fun loadExtension(
        jarPath: String,
        className: String,
    ): Any {
        try {
            val classLoader = ChildFirstURLClassLoader(arrayOf<URL>(Path(jarPath).toUri().toURL()))
            val classToLoad = Class.forName(className, false, classLoader)
            return classToLoad.getDeclaredConstructor().newInstance()
        } catch (e: Exception) {
            throw e
        }
    }
}
