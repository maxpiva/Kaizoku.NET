namespace Mihon.ExtensionsBridge.Core.Runtime
{
    /// <summary>
    /// Thrown when an extension fails to load due to reflection errors,
    /// version mismatches, or missing class definitions in the JAR.
    /// </summary>
    public class ExtensionLoadException : Exception
    {
        public ExtensionLoadException(string message) : base(message) { }
        public ExtensionLoadException(string message, Exception innerException) : base(message, innerException) { }
    }
}
