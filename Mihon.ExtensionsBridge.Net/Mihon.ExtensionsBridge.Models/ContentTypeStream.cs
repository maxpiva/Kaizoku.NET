using System;
using System.IO;

namespace Mihon.ExtensionsBridge.Models
{
    public class ContentTypeStream : MemoryStream
    {
        public virtual string ContentType { get; init; }

        public ContentTypeStream()
        {
        }

        protected ContentTypeStream(byte[] buffer)
            : base(buffer)
        {
        }
    }
}
