using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Serialization;
using Mihon.ExtensionsBridge.IKVMCompiler.Abstractions;

namespace Mihon.ExtensionsBridge.IKVMCompiler.Services
{
    /// <summary>
    /// Represents the resolved IKVM runtime and toolchain environment information for the current platform.
    /// Provides versioning details, operating system and processor identifiers, and required compatibility paths.
    /// </summary>
    public class IKVMVersion : IIKVMVersion
    {
        /// <summary>
        /// Gets the IKVM distribution version string.
        /// </summary>
        public string Version { get; }

        /// <summary>
        /// Gets the normalized operating system identifier used by IKVM toolchain downloads
        /// (e.g., <c>win</c>, <c>osx</c>, <c>linux</c>, <c>linux-musl</c>, <c>android</c>).
        /// </summary>
        public string OS { get; }

        /// <summary>
        /// Gets the normalized processor architecture identifier used by IKVM toolchain downloads
        /// (e.g., <c>x64</c>, <c>x86</c>, <c>arm</c>, <c>arm64</c>).
        /// </summary>
        public string Processor { get; }

        /// <summary>
        /// Gets the absolute path to the Android compatibility assembly required for IKVM.
        /// </summary>
        [JsonIgnore]
        public string AndroidCompatPath { get; }

        /// <summary>
        /// Gets the .NET target version string for IKVM tools components.
        /// </summary>
        public string ToolsNetVersion { get; }

        /// <summary>
        /// Gets the .NET target version string for IKVM JRE components.
        /// </summary>
        public string JRENetVersion { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="IKVMVersion"/> class for serializers.
        /// </summary>
        internal IKVMVersion()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="IKVMVersion"/> class, resolving platform metadata and required paths.
        /// </summary>
        /// <param name="version">The IKVM distribution version.</param>
        /// <param name="toolsNetVersion">The .NET version string for IKVM tools components.</param>
        /// <param name="jRENetVersion">The .NET version string for IKVM JRE components.</param>
        /// <exception cref="ArgumentException">
        /// Thrown when any provided version string is null or whitespace, or when <c>Android.Compat.dll</c> is not found.
        /// </exception>
        public IKVMVersion(string version, string toolsNetVersion, string jRENetVersion)
        {
            if (string.IsNullOrWhiteSpace(version))
                throw new ArgumentException("IKVM version cannot be null or whitespace.", nameof(version));
            if (string.IsNullOrWhiteSpace(toolsNetVersion))
                throw new ArgumentException("IKVM tools .NET version cannot be null or whitespace.", nameof(toolsNetVersion));
            if (string.IsNullOrWhiteSpace(jRENetVersion))
                throw new ArgumentException("IKVM JRE .NET version cannot be null or whitespace.", nameof(jRENetVersion));

            Version = version;
            ToolsNetVersion = toolsNetVersion;
            JRENetVersion = jRENetVersion;

            OS = ResolveOS();
            Processor = ResolveProcessor();

            AndroidCompatPath = Path.Combine(AppContext.BaseDirectory, "Android.Compat.dll");
            if (!File.Exists(AndroidCompatPath))
                throw new ArgumentException("Android.Compat.dll not found in application directory.");
        }

        /// <summary>
        /// Resolves the current operating system to an IKVM-compatible identifier.
        /// </summary>
        /// <returns>An OS identifier string recognized by IKVM toolchain downloads.</returns>
        /// <exception cref="PlatformNotSupportedException">
        /// Thrown when the current operating system cannot be mapped to an IKVM identifier.
        /// </exception>
        private static string ResolveOS()
        {
            if (OperatingSystem.IsWindows())
                return "win";

            if (OperatingSystem.IsMacOS())
                return "osx";

            if (OperatingSystem.IsLinux())
            {
                var musl = IsMuslLibc();
                return musl ? "linux-musl" : "linux";
            }

            if (OperatingSystem.IsAndroid())
            {
                return "android";
            }

            throw new PlatformNotSupportedException("Current operating system is not supported for IKVM toolchain downloads.");
        }

        /// <summary>
        /// Resolves the current process architecture to an IKVM-compatible identifier.
        /// </summary>
        /// <returns>A processor architecture identifier string recognized by IKVM toolchain downloads.</returns>
        /// <exception cref="PlatformNotSupportedException">
        /// Thrown when the current process architecture cannot be mapped to an IKVM identifier.
        /// </exception>
        private static string ResolveProcessor()
        {
            if (RuntimeInformation.ProcessArchitecture == Architecture.Arm)
                return "arm";
            if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
                return "arm64";
            if (RuntimeInformation.ProcessArchitecture == Architecture.X86)
                return "x86";
            if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
                return "x64";

            throw new PlatformNotSupportedException("Current operating system is not supported for IKVM toolchain downloads.");
        }

        /// <summary>
        /// Detects whether the current Linux system uses musl libc.
        /// </summary>
        /// <remarks>
        /// Attempts detection via <c>ldd --version</c>. If that fails, falls back to checking for files matching
        /// <c>/lib/ld-musl-*</c>. Returns <c>false</c> on non-Linux platforms or when detection cannot confirm musl.
        /// </remarks>
        /// <returns><c>true</c> if musl libc is detected; otherwise, <c>false</c>.</returns>
        private static bool IsMuslLibc()
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "ldd",
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process is null)
                {
                    return false;
                }

                var outputBuilder = new StringBuilder();
                outputBuilder.Append(process.StandardOutput.ReadToEnd());
                outputBuilder.Append(process.StandardError.ReadToEnd());
                process.WaitForExit();

                return outputBuilder.ToString().Contains("musl", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                try
                {
                    if (Directory.Exists("/lib"))
                    {
                        foreach (var file in Directory.EnumerateFiles("/lib", "ld-musl-*"))
                        {
                            if (!string.IsNullOrEmpty(file))
                            {
                                return true;
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore secondary detection failures.
                }

                return false;
            }
        }
    }
}
