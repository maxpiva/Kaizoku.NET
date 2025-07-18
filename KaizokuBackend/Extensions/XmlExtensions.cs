using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
using KaizokuBackend.Services.Import.KavitaParser;

namespace KaizokuBackend.Extensions
{
    /// <summary>
    /// Extension methods for XML and ComicInfo operations
    /// </summary>
    public static class XmlExtensions
    {
        /// <summary>
        /// Converts a Stream to a ComicInfo object
        /// </summary>
        /// <param name="stream">XML stream to parse</param>
        /// <returns>ComicInfo object or null if parsing fails</returns>
        public static ComicInfo? ToComicInfo(this Stream stream)
        {
            try
            {
                var comicInfoXml = XDocument.Load(stream);
                comicInfoXml.Descendants()
                    .Where(e => e.IsEmpty || string.IsNullOrWhiteSpace(e.Value))
                    .Remove();

                var serializer = new XmlSerializer(typeof(ComicInfo));
                using var reader = comicInfoXml.Root?.CreateReader();
                if (reader == null) return null;

                return (ComicInfo?)serializer.Deserialize(reader);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Converts a ComicInfo object to a Stream
        /// </summary>
        /// <param name="info">ComicInfo object to serialize</param>
        /// <returns>XML stream</returns>
        public static Stream ToStream(this ComicInfo info)
        {
            var xmlSerializer = new XmlSerializer(typeof(ComicInfo));
            using var stringWriter = new StringWriter();
            using (var xmlWriter = XmlWriter.Create(stringWriter, new XmlWriterSettings 
            { 
                Indent = true, 
                Encoding = new UTF8Encoding(false), 
                OmitXmlDeclaration = false 
            }))
            {
                xmlSerializer.Serialize(xmlWriter, info);
            }

            string xmlContent = stringWriter.ToString()
                .Replace("""<?xml version="1.0" encoding="utf-16"?>""", @"<?xml version='1.0' encoding='utf-8'?>");
            
            return new MemoryStream(Encoding.UTF8.GetBytes(xmlContent));
        }
    }
}