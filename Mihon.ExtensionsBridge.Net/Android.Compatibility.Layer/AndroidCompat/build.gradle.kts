import org.gradle.kotlin.dsl.withType
import org.jetbrains.kotlin.gradle.tasks.KotlinCompile
import de.undercouch.gradle.tasks.download.Download
import org.gradle.api.tasks.bundling.Jar
import org.jetbrains.kotlin.gradle.tasks.KotlinJvmCompile
import java.time.Instant

plugins {
    id(libs.plugins.kotlin.jvm.get().pluginId)
    id(libs.plugins.kotlin.serialization.get().pluginId)
}

dependencies {
    // implementation(project(":SerializationPatch"))
    // Shared
    implementation(libs.bundles.shared)
    implementation(libs.serialization.json.okio)

    testImplementation(libs.bundles.sharedTest)

    // Android stub library
    implementation(libs.android.stubs)

    // XML
    compileOnly(libs.xmlpull)

    // Config API
    implementation(projects.androidCompat.config)

    // APK sig verifier
    compileOnly(libs.apksig)

    // AndroidX annotations
    compileOnly(libs.android.annotations)

    // substitute for duktape-android/quickjs
    implementation(libs.bundles.polyglot)

    // Kotlin wrapper around Java Preferences, makes certain things easier
    implementation(libs.bundles.settings)

    // Android version of SimpleDateFormat
    implementation(libs.icu4j)

    // OpenJDK lacks native JPEG encoder and native WEBP decoder
    implementation(libs.bundles.twelvemonkeys)
    implementation(libs.imageio.webp)
}
tasks {
        withType<KotlinCompile>().configureEach {
            kotlinOptions.freeCompilerArgs += "-Xcontext-receivers"
            kotlinOptions.freeCompilerArgs += "-opt-in=kotlinx.serialization.ExperimentalSerializationApi"

        }
        withType<KotlinJvmCompile> {
            compilerOptions {
                freeCompilerArgs.add("-Xcontext-receivers")
                freeCompilerArgs.add("-opt-in=kotlinx.serialization.ExperimentalSerializationApi")
            }
        }
    }
// ---- Fat jar task (library; no Main-Class) ----
tasks.register<org.gradle.jvm.tasks.Jar>("fatJar") {
    group = "build"
    description = "Assembles an uber/fat jar containing this library + runtime dependencies."
    archiveClassifier.set("all")

    duplicatesStrategy = DuplicatesStrategy.EXCLUDE

    // your compiled classes/resources
    from(sourceSets.main.get().output)

    // runtime deps exploded into the jar
    dependsOn(configurations.runtimeClasspath)
    from({
        configurations.runtimeClasspath.get().map { file ->
            if (file.isDirectory) file else zipTree(file)
        }
    })

    // avoid broken signature metadata in fat jars
    exclude("META-INF/*.SF", "META-INF/*.DSA", "META-INF/*.RSA")

    // optional metadata
    manifest {
        attributes(
            mapOf(
                "Implementation-Title" to project.name,
                "Implementation-Version" to project.version.toString(),
            )
        )
    }
}
