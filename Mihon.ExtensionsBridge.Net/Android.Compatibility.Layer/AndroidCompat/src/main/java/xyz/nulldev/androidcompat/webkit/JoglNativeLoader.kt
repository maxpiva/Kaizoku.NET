package xyz.nulldev.androidcompat.webkit

import android.util.Log
import java.io.File
import java.io.IOException
import java.nio.file.Files
import java.nio.file.Path
import java.nio.file.StandardCopyOption
import java.util.Locale
import java.util.concurrent.atomic.AtomicBoolean

internal object JoglNativeLoader {
    private val nativesLoaded = AtomicBoolean(false)
    private val jawtLoaded = AtomicBoolean(false)

    fun ensureLoaded(jcefInstallDir: File? = null) {
        if (nativesLoaded.get()) {
            return
        }

        val platform = NativePlatform.detect() ?: run {
            Log.i(KcefWebViewProvider.TAG, "Unsupported platform ${System.getProperty("os.name")}/${System.getProperty("os.arch")} for bundled JOGL natives; relying on system libraries")
            return
        }

        try {
            if (platform.requiresJawt) {
                ensureJawtLoaded(jcefInstallDir)
            }

            val extractionRoot = Files.createTempDirectory("kcef-jogl-${platform.resourceDir}")
            extractionRoot.toFile().deleteOnExit()

            val extractedPaths = mutableListOf<Pair<NativeLibrary, Path>>()
            for (library in platform.libraries) {
                val extracted = extractLibrary(platform, library.fileName, extractionRoot)
                if (extracted == null) {
                    if (library.optional) {
                        Log.w(KcefWebViewProvider.TAG, "Optional JOGL native ${library.fileName} missing from resources")
                        continue
                    }
                    Log.w(KcefWebViewProvider.TAG, "Required JOGL native ${library.fileName} missing from resources")
                    return
                }
                extractedPaths += library to extracted
            }

            if (extractedPaths.isEmpty()) {
                Log.w(KcefWebViewProvider.TAG, "Unable to locate bundled JOGL natives for ${platform.resourceDir}; falling back to system libraries")
                return
            }

            extractedPaths.forEach { (library, path) ->
                runCatching {
                    System.load(path.toAbsolutePath().toString())
                }.onFailure { throwable ->
                    if (library.optional && throwable is UnsatisfiedLinkError) {
                        Log.w(KcefWebViewProvider.TAG, "Optional JOGL native ${library.fileName} failed to load", throwable)
                    } else {
                        throw throwable
                    }
                }
            }

            nativesLoaded.set(true)
            Log.d(KcefWebViewProvider.TAG, "Bundled JOGL natives loaded for ${platform.resourceDir}")
        } catch (t: Throwable) {
            Log.w(KcefWebViewProvider.TAG, "Failed to preload bundled JOGL natives; falling back to system lookup", t)
        }
    }

    private fun extractLibrary(platform: NativePlatform, libName: String, extractionRoot: Path): Path? {
        val resourcePath = "/natives/${platform.resourceDir}/$libName"
        val stream = JoglNativeLoader::class.java.getResourceAsStream(resourcePath)
            ?: return null

        val target = extractionRoot.resolve(libName)
        return try {
            stream.use { input ->
                Files.copy(input, target, StandardCopyOption.REPLACE_EXISTING)
            }
            target.toFile().deleteOnExit()
            target
        } catch (ioe: IOException) {
            Log.w(KcefWebViewProvider.TAG, "Failed to extract $resourcePath", ioe)
            null
        }
    }

    private fun ensureJawtLoaded(jcefInstallDir: File?) {
        if (jawtLoaded.get()) {
            return
        }

        val candidates = buildList {
            jcefInstallDir?.let {
                add(File(it, "jre/bin/jawt.dll"))
                add(File(it, "bin/jawt.dll"))
            }
            System.getProperty("java.home")?.let { add(File(it, "bin/jawt.dll")) }
            System.getenv("JAVA_HOME")?.let { add(File(it, "bin/jawt.dll")) }
        }.filter { it.exists() }

        for (candidate in candidates) {
            runCatching {
                System.load(candidate.absolutePath)
                jawtLoaded.set(true)
                Log.d(KcefWebViewProvider.TAG, "Loaded JAWT from ${candidate.absolutePath}")
                return
            }.onFailure {
                Log.w(KcefWebViewProvider.TAG, "Failed to load JAWT from ${candidate.absolutePath}", it)
            }
        }

        runCatching {
            System.loadLibrary("jawt")
            jawtLoaded.set(true)
            Log.d(KcefWebViewProvider.TAG, "Loaded JAWT via System.loadLibrary")
            return
        }

        Log.w(KcefWebViewProvider.TAG, "JAWT not found; nativewindow_awt.dll will be treated as optional")
    }

    private data class NativeLibrary(val fileName: String, val optional: Boolean = false)

    private enum class NativePlatform(
        val resourceDir: String,
        val libraries: List<NativeLibrary>,
        val requiresJawt: Boolean = false,
    ) {
        WINDOWS_X86_64(
            resourceDir = "windows-amd64",
            libraries = listOf(
                NativeLibrary("gluegen_rt.dll"),
                NativeLibrary("nativewindow_awt.dll", optional = true),
                NativeLibrary("nativewindow_win32.dll"),
                NativeLibrary("jogl_desktop.dll"),
                NativeLibrary("jogl_mobile.dll", optional = true),
                NativeLibrary("newt_head.dll", optional = true),
            ),
            requiresJawt = true,
        ),
        LINUX_X86_64(
            resourceDir = "linux-amd64",
            libraries = listOf(
                NativeLibrary("libgluegen_rt.so"),
                NativeLibrary("libnativewindow_awt.so", optional = true),
                NativeLibrary("libnativewindow_x11.so"),
                NativeLibrary("libjogl_desktop.so"),
                NativeLibrary("libjogl_mobile.so", optional = true),
                NativeLibrary("libnewt_head.so", optional = true),
            ),
        ),
        LINUX_AARCH64(
            resourceDir = "linux-aarch64",
            libraries = listOf(
                NativeLibrary("libgluegen_rt.so"),
                NativeLibrary("libnativewindow_awt.so", optional = true),
                NativeLibrary("libnativewindow_x11.so"),
                NativeLibrary("libjogl_desktop.so"),
                NativeLibrary("libjogl_mobile.so", optional = true),
                NativeLibrary("libnewt_head.so", optional = true),
            ),
        ),
        LINUX_ARMV6(
            resourceDir = "linux-armv6hf",
            libraries = listOf(
                NativeLibrary("libgluegen_rt.so"),
                NativeLibrary("libnativewindow_awt.so", optional = true),
                NativeLibrary("libnativewindow_x11.so"),
                NativeLibrary("libjogl_desktop.so"),
                NativeLibrary("libjogl_mobile.so", optional = true),
                NativeLibrary("libnewt_head.so", optional = true),
            ),
        ),
        MAC_UNIVERSAL(
            resourceDir = "macosx-universal",
            libraries = listOf(
                NativeLibrary("libgluegen_rt.dylib"),
                NativeLibrary("libnativewindow_awt.dylib", optional = true),
                NativeLibrary("libnativewindow_macosx.dylib"),
                NativeLibrary("libjogl_desktop.dylib"),
                NativeLibrary("libjogl_mobile.dylib", optional = true),
                NativeLibrary("libnewt_head.dylib", optional = true),
            ),
        );

        companion object {
            fun detect(): NativePlatform? {
                val osName = System.getProperty("os.name")?.lowercase(Locale.US).orEmpty()
                val archName = System.getProperty("os.arch")?.lowercase(Locale.US).orEmpty()

                return when {
                    osName.contains("win") && archName in setOf("amd64", "x86_64") -> WINDOWS_X86_64
                    osName.contains("linux") && archName in setOf("amd64", "x86_64") -> LINUX_X86_64
                    osName.contains("linux") && archName.contains("aarch64") -> LINUX_AARCH64
                    osName.contains("linux") && archName.startsWith("arm") -> LINUX_ARMV6
                    osName.contains("mac") -> MAC_UNIVERSAL
                    else -> null
                }
            }
        }
    }
}
