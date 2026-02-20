using System;
using System.Collections.Generic;
using System.Text;

namespace Mihon.ExtensionsBridge.Models.Extensions
{
    public class ImageResponse
    {
        public int Status { get; set; } = 200;
        public string ContentType { get; set; }

        public byte[] Image { get; set; }
    }
}
