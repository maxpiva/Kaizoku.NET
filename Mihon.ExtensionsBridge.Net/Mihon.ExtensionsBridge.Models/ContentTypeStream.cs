using System;
using System.IO;

namespace Mihon.ExtensionsBridge.Models
{
    public abstract class ContentTypeStream : MemoryStream
    {
        public abstract string ContentType { get; init; }

        protected ContentTypeStream()
        {
        }

        protected ContentTypeStream(byte[] buffer)
            : base(buffer)
        {
        }
    }
}
