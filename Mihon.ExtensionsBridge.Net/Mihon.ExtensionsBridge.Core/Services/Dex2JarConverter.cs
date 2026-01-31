using com.googlecode.d2j.dex;
using com.googlecode.d2j.reader;
using com.googlecode.dex2jar.tools;
using java.nio.file;
using Microsoft.Extensions.Logging;
using org.objectweb.asm;
using System.IO.Compression;
using System.Linq;
using Mihon.ExtensionsBridge.Core.Extensions;
using Mihon.ExtensionsBridge.Core.Models;
using Mihon.ExtensionsBridge.Models;
using Mihon.ExtensionsBridge.Models.Abstractions;

namespace Mihon.ExtensionsBridge.Core.Services
{
    /// <summary>
    /// Converts Android APK <c>.dex</c> bytecode to a <c>.jar</c> file and applies class transformations
    /// to ensure compatibility, while also extracting embedded assets into the resulting JAR.
    /// </summary>
    /// <remarks>
    /// This service wraps dex2jar conversion with error handling, synchronized execution, and ASM-based
    /// bytecode transformations to replace unsupported Android classes. It also merges APK assets into
    /// the JAR output for downstream consumers.
    /// </remarks>
    public class Dex2JarConverter : IDex2JarConverter
    {
        /// <summary>
        /// Synchronization primitive that ensures only one conversion runs at a time to avoid
        /// concurrent access to dex2jar and file-system resources.
        /// </summary>
        private static readonly SemaphoreSlim dexLock = new(1, 1);

        /// <summary>
        /// Logger instance for diagnostic output and error reporting.
        /// </summary>
        private readonly ILogger _logger;

        /// <summary>
        /// Provides working folder paths where APK and JAR files are read or written.
        /// </summary>
        private readonly IWorkingFolderStructure _folder;

        /// <summary>
        /// Current converter version used to tag conversion results.
        /// </summary>
        public const string Version = "1.0.0";

        /// <summary>
        /// Classpath prefix used to redirect certain Android framework classes to compatibility replacements.
        /// </summary>
        private const string REPLACEMENT_PATH = "xyz/nulldev/androidcompat/replace";
        private const string REPLACEMENT_CLASS_LOADER_DESC = "L" + REPLACEMENT_PATH + "/java/lang/ClassLoader;";
        private const string CLASS_LOADER_HOOK_OWNER = REPLACEMENT_PATH + "/java/lang/ClassLoaderHooks";

        private static readonly string[] _packagesToSkip = new[]
        {
            "kotlin/",
            "kotlinx/",
            "org/jetbrains/kotlin/",
        };



        /// <summary>
        /// Set of class internal names to be replaced either directly or indirectly during ASM transformations.
        /// </summary>
        private static readonly HashSet<string> _classesToReplace = new(StringComparer.Ordinal)
        {
            "java/text/SimpleDateFormat",
            //"java/lang/ClassLoader"
        };
        private static readonly HashSet<string> _removenameSpaceClasses = new(StringComparer.Ordinal)
        {
            "org.apache.commons.lang3",
            "org.apache.commons.text",
            "org.brotli.dec"
        };

        string IDex2JarConverter.Version => Version;

        /// <summary>
        /// Initializes a new instance of the <see cref="Dex2JarConverter"/> class.
        /// </summary>
        /// <param name="folder">A working folder structure with the output extensions directory.</param>
        /// <param name="logger">A logger for diagnostic information.</param>
        public Dex2JarConverter(IWorkingFolderStructure folder, ILogger<Dex2JarConverter> logger)
        {
            _folder = folder;
            _logger = logger;
        }

        /// <summary>
        /// Converts the specified repository entry's APK into a JAR, applies Android class fixes,
        /// and merges APK assets into the generated JAR.
        /// </summary>
        /// <param name="workUnit">The repository entry containing the APK file information.</param>
        /// <param name="token">A cancellation token to observe while awaiting the conversion.</param>
        /// <returns><c>true</c> if conversion succeeded; otherwise <c>false</c>.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="workUnit"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown if the APK file name is empty or whitespace.</exception>
        /// <exception cref="FileNotFoundException">Thrown if the APK path does not exist.</exception>
        public async Task<bool> ConvertAsync(ExtensionWorkUnit workUnit, CancellationToken token = default)
        {
            if (workUnit == null)
                throw new ArgumentNullException(nameof(workUnit));
            if (workUnit.Entry == null)
                throw new ArgumentException("Entry cannot be null.", nameof(workUnit));
            if (workUnit.Entry.Apk == null)
                throw new ArgumentException("APK cannot be null.", nameof(workUnit));
            if (string.IsNullOrWhiteSpace(workUnit.Entry.Apk.FileName))
                throw new ArgumentException("APK file name cannot be empty.", nameof(workUnit));
            string apkFile = System.IO.Path.Combine(workUnit.WorkingFolder.Path, workUnit.Entry.Apk.FileName);
            if (!File.Exists(apkFile))
                throw new FileNotFoundException("APK file not found.", apkFile);
            string jarFile = System.IO.Path.ChangeExtension(apkFile, ".jar");
            _logger.LogInformation("Starting Dex2Jar conversion for APK {ApkFileName}.", workUnit.Entry.Apk.FileName);
            byte[] data = await File.ReadAllBytesAsync(apkFile, token).ConfigureAwait(false);
            bool result = await dexLock.RunBlockingIoAsync<bool>(() =>
            {
                var jarFilePath = new java.io.File(jarFile).toPath();
                var reader = MultiDexFileReader.open(data);
                var handler = new BaksmaliBaseDexExceptionHandler();
                Dex2jar
                  .from(reader)
                  .withExceptionHandler(handler)
                  .reUseReg(false)
                  .topoLogicalSort()
                  .skipDebug(true)
                  .optimizeSynchronized(false)
                  .printIR(false)
                  .noCode(false)
                  .skipExceptions(false)
                  .dontSanitizeNames(true)
                  .to(jarFilePath);
                if (handler.hasException())
                {
                    return false;
                }
                FixAndroidClasses(jarFilePath);
                ExtractAssetsFromApk(apkFile, jarFile);
                return true;
            }, token).ConfigureAwait(false);
            if (!result || !File.Exists(jarFile))
            {
                _logger.LogError("Dex2Jar conversion failed for APK {ApkFileName}.", workUnit.Entry.Apk.FileName);
                return false;
            }
            workUnit.Entry.Jar = await jarFile.CalculateFileHashAsync(Version).ConfigureAwait(false);
            _logger.LogInformation("Dex2Jar conversion succeeded for APK {ApkFileName}.", workUnit.Entry.Apk.FileName);
            return true;
        }

        /// <summary>
        /// Opens the output JAR as a file system and applies bytecode transformations
        /// to replace Android classes with compatibility implementations.
        /// </summary>
        /// <param name="jarFile">The path to the generated JAR file.</param>
        public void FixAndroidClasses(java.nio.file.Path jarFile)
        {
            FileSystem fs = null;
            try
            {
                fs = FileSystems.newFileSystem(jarFile, (java.lang.ClassLoader?)null);

                var root = fs.getPath("/");

                using var stream = Files.walk(root);

                var it = stream.iterator();
                while (it.hasNext())
                {
                    var p = (java.nio.file.Path)it.next();
                    if (Files.isDirectory(p))
                        continue;

                    var pair = GetClassBytes(p);
                    if (pair == null)
                        continue;

                    // Determine class name and delete entries that start with any of the remove namespaces
                    var cr = new ClassReader(pair.Value.bytes);
                    var className = cr.getClassName();
                    if (ShouldRemoveNamespace(className))
                    {
                        try
                        {
                            _logger.LogDebug("Removing class due to namespace filter: {ClassName}", className);
                            Files.delete(p);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to remove class {ClassName} at path {Path}", className, p.toString());
                        }
                        continue;
                    }

                    var transformed = Transform(pair.Value);
                    Write(transformed);
                }
            }
            finally
            {
                try { fs?.close(); } catch { /* ignore */ }
            }

        }

        private static bool ShouldRemoveNamespace(string className)
        {
            if (string.IsNullOrEmpty(className))
                return false;

            // Convert internal name "org/apache/commons/lang3/..." to dotted form "org.apache.commons.lang3..."
            var dotted = className.Replace('/', '.');

            foreach (var ns in _removenameSpaceClasses)
            {
                if (dotted.StartsWith(ns, StringComparison.Ordinal))
                    return true;
            }
                
            return false;
        }

        /// <summary>
        /// Extracts files under the APK's <c>assets/</c> directory and injects them into a new JAR,
        /// excluding <c>META-INF/</c> entries, then replaces the original JAR.
        /// </summary>
        /// <param name="apkPath">Full path to the source APK file.</param>
        /// <param name="jarPath">Full path to the target JAR file to be rewritten.</param>
        /// <exception cref="FileNotFoundException">Thrown when APK or JAR paths are not found.</exception>
        /// <exception cref="InvalidOperationException">Thrown when parent directories cannot be determined.</exception>
        private void ExtractAssetsFromApk(string apkPath, string jarPath)
        {
            var apkFile = new FileInfo(apkPath);
            var jarFile = new FileInfo(jarPath);

            if (!apkFile.Exists)
                throw new FileNotFoundException("APK not found", apkPath);

            if (!jarFile.Exists)
                throw new FileNotFoundException("JAR not found", jarPath);

            var apkDir = apkFile.Directory?.FullName ?? throw new InvalidOperationException("APK directory not found");
            var jarDir = jarFile.Directory?.FullName ?? throw new InvalidOperationException("JAR directory not found");

            var assetsFolder = System.IO.Path.Combine(apkDir, $"{System.IO.Path.GetFileNameWithoutExtension(apkFile.Name)}_assets");

            Directory.CreateDirectory(assetsFolder);

            // 1) Extract assets/ from APK into assetsFolder, preserving subfolders
            using (var apkStream = File.OpenRead(apkFile.FullName))
            using (var apkZip = new ZipArchive(apkStream, ZipArchiveMode.Read, leaveOpen: false))
            {
                foreach (var entry in apkZip.Entries)
                {
                    // ZipArchive: directory entries usually end with "/"
                    if (!entry.FullName.StartsWith("assets/", StringComparison.Ordinal))
                        continue;

                    if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
                        continue; // directory

                    // Kotlin used File(assetsFolder, zipEntry.name) which includes "assets/..."
                    // So we replicate that: assetsFolder/assets/...
                    var outPath = System.IO.Path.Combine(assetsFolder, entry.FullName.Replace('/', System.IO.Path.DirectorySeparatorChar));

                    var outDir = System.IO.Path.GetDirectoryName(outPath);
                    if (!string.IsNullOrEmpty(outDir))
                        Directory.CreateDirectory(outDir);

                    using var entryStream = entry.Open();
                    using var outFile = File.Create(outPath);
                    entryStream.CopyTo(outFile);
                }
            }

            // 2) Create temp JAR excluding META-INF/**, then add files from assetsFolder
            var tempJarPath =
                System.IO.Path.Combine(jarDir, $"{System.IO.Path.GetFileNameWithoutExtension(jarFile.Name)}_temp.jar");

            // Overwrite if exists
            if (File.Exists(tempJarPath))
                File.Delete(tempJarPath);

            using (var jarReadStream = File.OpenRead(jarFile.FullName))
            using (var jarReadZip = new ZipArchive(jarReadStream, ZipArchiveMode.Read, leaveOpen: false))
            using (var jarWriteStream = File.Create(tempJarPath))
            using (var jarWriteZip = new ZipArchive(jarWriteStream, ZipArchiveMode.Create, leaveOpen: false))
            {
                // Copy everything except META-INF/
                foreach (var entry in jarReadZip.Entries)
                {
                    if (entry.FullName.StartsWith("META-INF/", StringComparison.Ordinal))
                        continue;

                    // Preserve directory entries if present (optional)
                    if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
                    {
                        jarWriteZip.CreateEntry(entry.FullName);
                        continue;
                    }

                    var newEntry = jarWriteZip.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                    using var src = entry.Open();
                    using var dst = newEntry.Open();
                    src.CopyTo(dst);
                }

                // Add extracted assetsFolder files into jar
                foreach (var filePath in Directory.EnumerateFiles(assetsFolder, "*", SearchOption.AllDirectories))
                {
                    var rel = System.IO.Path.GetRelativePath(assetsFolder, filePath);

                    // Kotlin: relativeTo(assetsFolder).toString().replace("\\", "/")
                    var entryName = rel.Replace('\\', '/');

                    // Ensure no leading "./" etc.
                    entryName = entryName.TrimStart('/');

                    var newEntry = jarWriteZip.CreateEntry(entryName, CompressionLevel.Optimal);
                    using var src = File.OpenRead(filePath);
                    using var dst = newEntry.Open();
                    src.CopyTo(dst);
                }
            }

            // 3) Replace original jar with temp jar
            jarFile.Delete();

            // FileInfo.MoveTo overwrites only in newer frameworks; be explicit:
            File.Move(tempJarPath, jarFile.FullName);

            // 4) Cleanup extracted assets folder
            try
            {
                Directory.Delete(assetsFolder, recursive: true);
            }
            catch
            {
                // best effort cleanup (matches Kotlin deleteRecursively() semantics loosely)
            }
        }

        /// <summary>
        /// Reads a class file from the given path and validates it contains the CAFEBABE header.
        /// </summary>
        /// <param name="path">The JAR-internal path to the class file.</param>
        /// <returns>
        /// A tuple with the original path and the class bytes if valid; otherwise <c>null</c>.
        /// </returns>
        private (java.nio.file.Path path, byte[] bytes)? GetClassBytes(java.nio.file.Path path)
        {
            try
            {
                var pathStr = path.toString();
                if (!pathStr.EndsWith(".class", StringComparison.Ordinal))
                    return null;

                var bytes = Files.readAllBytes(path);
                if (bytes == null || bytes.Length < 4)
                    return null; // invalid size

                // Kotlin checks CAFEBABE by formatting first 4 bytes.
                string cafebabe = $"{bytes[0]:X2}{bytes[1]:X2}{bytes[2]:X2}{bytes[3]:X2}";

                if (!cafebabe.Equals("CAFEBABE", StringComparison.OrdinalIgnoreCase))
                    return null; // corrupted

                return (path, bytes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error loading class from Path: {path}");
                return null;
            }
        }

        /// <summary>
        /// Applies ASM transformations to a class file byte array to replace targeted types and descriptors.
        /// </summary>
        /// <param name="pair">The path and raw class bytes to transform.</param>
        /// <returns>The transformed class bytes alongside the original path.</returns>
        private (java.nio.file.Path path, byte[] bytes) Transform((java.nio.file.Path path, byte[] bytes) pair)
        {
            var cr = new ClassReader(pair.bytes);
            var className = cr.getClassName();
            /* if (ShouldSkipClass(className))
            {
                _logger.LogDebug("Skipping rewrite for class {ClassName}", className);
                return pair;
            }*/
            var cw = new ClassWriter(cr, 0);

            cr.accept(new TransformingClassVisitor(cw, _logger), 0);

            return (pair.path, cw.toByteArray());
        }

        private static bool ShouldSkipClass(string className)
        {
            if (string.IsNullOrEmpty(className))
            {
                return false;
            }
            return _packagesToSkip.Any(prefix => className.StartsWith(prefix, StringComparison.Ordinal));
        }

        /// <summary>
        /// Writes transformed class bytes back into the JAR file system.
        /// </summary>
        /// <param name="pair">The path and transformed bytes to write.</param>
        private void Write((java.nio.file.Path path, byte[] bytes) pair)
        {
            Files.write(
                pair.path,
                pair.bytes,
                StandardOpenOption.CREATE,
                StandardOpenOption.TRUNCATE_EXISTING
            );
        }

        /// <summary>
        /// Replaces a direct internal type name with its compatibility counterpart if listed.
        /// </summary>
        /// <param name="s">The type internal name to evaluate.</param>
        /// <returns>The replaced type name if matched; otherwise the original value.</returns>
        private static string? ReplaceDirectly(string? s)
        {
            if (s == null) return null;
            return _classesToReplace.Contains(s) ? $"{REPLACEMENT_PATH}/{s}" : s;
        }

        /// <summary>
        /// Replaces occurrences of targeted internal type names inside a type descriptor string.
        /// </summary>
        /// <param name="s">The descriptor string to evaluate.</param>
        /// <returns>The descriptor with replacements applied; otherwise the original value.</returns>
        private static string? ReplaceIndirectly(string? s)
        {
            if (s == null) return null;

            var result = s;
            foreach (var cls in _classesToReplace)
            {
                result = result.Replace(cls, $"{REPLACEMENT_PATH}/{cls}", StringComparison.Ordinal);
            }
            return result;
        }

        /// <summary>
        /// ASM <see cref="ClassVisitor"/> that applies targeted replacements to class, field, and method
        /// metadata as well as emits diagnostics during visitation.
        /// </summary>
        private sealed class TransformingClassVisitor : ClassVisitor
        {
            /// <summary>
            /// Logger used for visitation diagnostics.
            /// </summary>
            private ILogger _logger;

            /// <summary>
            /// Initializes a new instance of the <see cref="TransformingClassVisitor"/>.
            /// </summary>
            /// <param name="cw">The downstream class writer receiving transformed classes.</param>
            /// <param name="logger">Logger for debug output.</param>
            public TransformingClassVisitor(ClassWriter cw, ILogger logger)
                : base(Opcodes.ASM5, cw)
            {
                _logger = logger;
            }

            /// <summary>
            /// Visits a field and applies indirect descriptor replacements.
            /// </summary>
            /// <param name="access">Field access flags.</param>
            /// <param name="name">Field name.</param>
            /// <param name="desc">Field descriptor.</param>
            /// <param name="signature">Field signature, if any.</param>
            /// <param name="value">Initial field value, if any.</param>
            /// <returns>The field visitor to continue visitation.</returns>
            public override FieldVisitor? visitField(
                int access,
                string? name,
                string? desc,
                string? signature,
                object? value)
            {
                string? indirectly = ReplaceIndirectly(desc);
                _logger.LogTrace($"Class Field: {indirectly ?? ""}: {value?.GetType().Name}: {value}");

                return base.visitField(access, name, indirectly, signature, value);
            }

            /// <summary>
            /// Visits the class header.
            /// </summary>
            /// <param name="version">Class file version.</param>
            /// <param name="access">Class access flags.</param>
            /// <param name="name">Internal name of the class.</param>
            /// <param name="signature">Generic signature, if any.</param>
            /// <param name="superName">Internal name of the super class.</param>
            /// <param name="interfaces">Implemented interface internal names.</param>
            public override void visit(
                int version,
                int access,
                string? name,
                string? signature,
                string? superName,
                string[]? interfaces)
            {
                _logger.LogTrace($"Visiting {name}: {signature}: {superName}");
                base.visit(version, access, name, signature, superName, interfaces);
            }

            /// <summary>
            /// Visits a method and applies indirect descriptor replacements.
            /// </summary>
            /// <param name="access">Method access flags.</param>
            /// <param name="name">Method name.</param>
            /// <param name="desc">Method descriptor.</param>
            /// <param name="signature">Generic signature, if any.</param>
            /// <param name="exceptions">Declared exceptions, if any.</param>
            /// <returns>A method visitor that performs instruction-level transformations.</returns>
            public override MethodVisitor visitMethod(
                int access,
                string name,
                string desc,
                string? signature,
                string[]? exceptions)
            {
                string? indirectly = ReplaceIndirectly(desc);

                _logger.LogTrace($"Processing method {name}: {indirectly ?? ""}: {signature}");

                var mv = base.visitMethod(
                    access,
                    name,
                    indirectly ?? desc,
                    signature,
                    exceptions
                );

                return new TransformingMethodVisitor(mv, _logger);
            }
        }

        /// <summary>
        /// ASM <see cref="MethodVisitor"/> that performs instruction-level transformations,
        /// including type and descriptor replacements in method calls, field access, and type instructions.
        /// </summary>
        private sealed class TransformingMethodVisitor : MethodVisitor
        {
            /// <summary>
            /// Logger used for instruction-level diagnostics.
            /// </summary>
            private ILogger _logger;

            /// <summary>
            /// Initializes a new instance of the <see cref="TransformingMethodVisitor"/>.
            /// </summary>
            /// <param name="mv">The downstream method visitor.</param>
            /// <param name="logger">Logger for debug output.</param>
            public TransformingMethodVisitor(MethodVisitor mv, ILogger logger)
                : base(Opcodes.ASM5, mv)
            {
                _logger = logger;
            }

            /// <summary>
            /// Visits a constant load instruction.
            /// </summary>
            /// <param name="cst">The constant value.</param>
            public override void visitLdcInsn(object? cst)
            {
                _logger.LogTrace($"Ldc: {(cst == null ? "null" : $"{cst.GetType().Name}: {cst}")}");
                base.visitLdcInsn(cst);
            }

            /// <summary>
            /// Visits a type instruction and applies direct replacements to type names.
            /// </summary>
            /// <param name="opcode">The opcode of the type instruction.</param>
            /// <param name="type">The internal type name.</param>
            public override void visitTypeInsn(int opcode, string? type)
            {
                string? replaced = ReplaceDirectly(type);
                _logger.LogTrace($"Type: {opcode}: {replaced ?? ""}");
                base.visitTypeInsn(opcode, replaced);
            }

            /// <summary>
            /// Visits a method invocation and applies direct owner replacements and indirect descriptor replacements.
            /// </summary>
            /// <param name="opcode">The method invocation opcode.</param>
            /// <param name="owner">The internal name of the method owner.</param>
            /// <param name="name">The method name.</param>
            /// <param name="desc">The method descriptor.</param>
            /// <param name="itf"><c>true</c> if the owner is an interface; otherwise <c>false</c>.</param>
            public override void visitMethodInsn(
                int opcode,
                string? owner,
                string? name,
                string? desc,
                bool itf)
            {
                bool isClassLoaderGetter = owner == "java/lang/Class" && name == "getClassLoader" && desc == "()Ljava/lang/ClassLoader;";
                /*
                if (isClassLoaderGetter)
                {
                    _logger.LogDebug($"Method: {opcode}: {owner}: {name}: {desc} -> hook");
                    base.visitMethodInsn(
                        Opcodes.INVOKESTATIC,
                        CLASS_LOADER_HOOK_OWNER,
                        "getClassLoader",
                        "(Ljava/lang/Class;)" + REPLACEMENT_CLASS_LOADER_DESC,
                        false);
                    return;
                }
                */
                string? directly = ReplaceDirectly(owner);
                string? indirectly = ReplaceIndirectly(desc);

                _logger.LogTrace($"Method: {opcode}: {directly ?? ""}: {name}: {indirectly ?? ""}");

                base.visitMethodInsn(
                   opcode,
                   directly,
                   name,
                   indirectly,
                   itf
               );
            }

            /// <summary>
            /// Visits a field access and applies indirect descriptor replacements.
            /// </summary>
            /// <param name="opcode">The field access opcode.</param>
            /// <param name="owner">The internal name of the field owner.</param>
            /// <param name="name">The field name.</param>
            /// <param name="desc">The field descriptor.</param>
            public override void visitFieldInsn(int opcode, string? owner, string? name, string? desc)
            {
                string? indirectly = ReplaceIndirectly(desc);
                _logger.LogTrace($"Field: {opcode}: {owner}: {name}: {indirectly ?? ""}");
                base.visitFieldInsn(opcode, owner, name, indirectly);
            }

            /// <summary>
            /// Visits an invokedynamic instruction.
            /// </summary>
            /// <param name="name">The invokedynamic method name.</param>
            /// <param name="desc">The method descriptor.</param>
            /// <param name="bsm">The bootstrap method handle.</param>
            /// <param name="bsmArgs">Bootstrap method arguments.</param>
            public override void visitInvokeDynamicInsn(
                string? name,
                string? desc,
                Handle? bsm,
                params object[]? bsmArgs)
            {
                _logger.LogTrace($"InvokeDynamic: {name}: {desc}");
                // Note: bsmArgs can be null; ASM expects an array (possibly empty).
                base.visitInvokeDynamicInsn(name, desc, bsm, bsmArgs ?? Array.Empty<object>());
            }
        }
    }
}
