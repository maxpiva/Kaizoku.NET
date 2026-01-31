namespace Mihon.ExtensionsBridge.Models.Abstractions
{
    public interface IWorkingFolderStructure
    {
        string ExtensionsFolder { get; }
        string TempFolder { get; }
        string AndroidFolder { get; }
        string WorkingFolder { get; }

    }
}
