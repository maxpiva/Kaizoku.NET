namespace Mihon.ExtensionsBridge.IKVMCompiler.Abstractions
{
    public interface ICompilerWorkingFolderStructure
    {
        string IKVMFolder { get; }
        string IKVMJREFolder { get; }
        string IKVMToolsFolder { get; }
        string TempFolder { get; }
        string WorkingFolder { get; }

    }
}
