using Mihon.ExtensionsBridge.Models.Abstractions;
using System.IO;
using Microsoft.Extensions.Options;
using Mihon.ExtensionsBridge.Models.Configuration;


namespace Mihon.ExtensionsBridge.Core.Services
{
    /// <summary>
    /// Provides a strongly-typed structure of folders used by the Extension Bridge at runtime.
    /// Ensures required directories exist under the specified working folder and exposes their paths.
    /// </summary>
    public class WorkingFolderStructure : IWorkingFolderStructure
    {
       
        /// <summary>
        /// Backing field for <see cref="TempFolder"/> storing the resolved temporary directory path.
        /// </summary>
        private string _tempFolder = string.Empty;

        /// <summary>
        /// Gets the root working folder path used by the Extension Bridge.
        /// </summary>
        public string WorkingFolder { get; private set; } = string.Empty;

        /// <summary>
        /// Gets the path to the extensions directory under the <see cref="WorkingFolder"/>.
        /// </summary>
        public string ExtensionsFolder => System.IO.Path.Combine(WorkingFolder, "extensions");

        /// <summary>
        /// Gets the path to the IKVM directory under the <see cref="WorkingFolder"/>.
        /// </summary>
        public string IKVMFolder => System.IO.Path.Combine(WorkingFolder, "ikvm");

        /// <summary>
        /// Gets the path to the IKVM tools directory under the <see cref="IKVMFolder"/>.
        /// </summary>
        public string IKVMToolsFolder => System.IO.Path.Combine(IKVMFolder, "tools");

        /// <summary>
        /// Gets the path to the IKVM JRE directory under the <see cref="IKVMFolder"/>.
        /// </summary>
        public string IKVMJREFolder => System.IO.Path.Combine(IKVMFolder, "jre");

        public string AndroidFolder => System.IO.Path.Combine(WorkingFolder, "android");

        /// <summary>
        /// Gets the path to the temporary directory used by the Extension Bridge for transient files.
        /// </summary>
        public string TempFolder => _tempFolder;


        public WorkingFolderStructure(IOptions<Paths> paths)
        {
            if (paths==null)
                throw new ArgumentNullException(nameof(paths));
            Paths p = paths.Value;
            if (string.IsNullOrWhiteSpace(p.BridgeFolder))
            {
                throw new ArgumentException("Bridge Folder path cannot be null or whitespace.", nameof(p.BridgeFolder));
            }
            if (!Directory.Exists(p.BridgeFolder))
                Directory.CreateDirectory(p.BridgeFolder);

            if (string.IsNullOrWhiteSpace(p.TempFolder))
            {
                _tempFolder = Path.Combine(Path.GetTempPath(), "extensionbridge");
            }
            else
            {
                _tempFolder = p.TempFolder;
            }

            WorkingFolder = p.BridgeFolder;

            if (!Directory.Exists(ExtensionsFolder))
            {
                Directory.CreateDirectory(ExtensionsFolder);
            }

            if (!Directory.Exists(IKVMFolder))
            {
                Directory.CreateDirectory(IKVMFolder);
            }

            if (!Directory.Exists(IKVMToolsFolder))
            {
                Directory.CreateDirectory(IKVMToolsFolder);
            }

            if (!Directory.Exists(IKVMJREFolder))
            {
                Directory.CreateDirectory(IKVMJREFolder);
            }
            if (!Directory.Exists(AndroidFolder))
            {
                Directory.CreateDirectory(AndroidFolder);
            }
            if (!Directory.Exists(_tempFolder))
            {
                Directory.CreateDirectory(_tempFolder);
            }
        }

    }
}
