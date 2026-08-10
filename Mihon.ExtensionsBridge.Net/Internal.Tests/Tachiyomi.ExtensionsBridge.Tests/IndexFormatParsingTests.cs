using System.Collections.Generic;
using Mihon.ExtensionsBridge.Core.Services;
using Xunit;

namespace Tachiyomi.ExtensionsBridge.Tests
{
    public class IndexFormatParsingTests
    {
        private const string NewFormatIndex = """
        {
          "name": "Keiyoushi",
          "badgeLabel": "K",
          "signingKey": "abc123",
          "contact": { "website": "https://keiyoushi.github.io", "discord": "https://discord.gg/example" },
          "extensionList": {
            "extensions": [
              {
                "name": "Tachiyomi: MangaDex",
                "packageName": "eu.kanade.tachiyomi.extension.all.mangadex",
                "resources": {
                  "apkUrl": "https://cdn.example.com/apk/tachiyomi-all.mangadex-v1.4.16.apk",
                  "iconUrl": "https://cdn.example.com/icon/eu.kanade.tachiyomi.extension.all.mangadex.png",
                  "jarUrl": "https://cdn.example.com/jar/tachiyomi-all.mangadex-v1.4.16.jar"
                },
                "extensionLib": "1.4",
                "versionCode": "42",
                "versionName": "1.4.16",
                "contentWarning": "CONTENT_WARNING_NSFW",
                "sources": [
                  {
                    "id": "2499283573021220255",
                    "name": "MangaDex",
                    "language": "en",
                    "homeUrl": "https://mangadex.org"
                  },
                  {
                    "id": "1145824452519314725",
                    "name": "MangaDex (es)",
                    "language": "es",
                    "homeUrl": "https://mangadex.org",
                    "mirrorUrls": ["https://mirror.mangadex.org"]
                  }
                ]
              }
            ]
          }
        }
        """;

        private const string LegacyIndex = """
        [
          {
            "name": "Tachiyomi: Comick",
            "pkg": "eu.kanade.tachiyomi.extension.all.comick",
            "apk": "tachiyomi-all.comick-v1.5.30.apk",
            "lang": "all",
            "code": 55,
            "version": "1.5.30",
            "nsfw": 1,
            "sources": [
              {
                "name": "Comick",
                "lang": "en",
                "id": "1234567890",
                "baseUrl": "https://comick.io",
                "versionId": 1
              }
            ]
          }
        ]
        """;

        [Fact]
        public void ParseIndexBody_NewFormat_MapsFields()
        {
            var extensions = RepositoryDownloader.ParseIndexBody(NewFormatIndex);

            Assert.NotNull(extensions);
            var ext = Assert.Single(extensions!);
            Assert.Equal("eu.kanade.tachiyomi.extension.all.mangadex", ext.Package);
            // New-format resources keep their absolute URLs in the artifact fields;
            // GetApkFilename/GetApkUrl derive the local filename and download URL from them.
            Assert.Equal("https://cdn.example.com/apk/tachiyomi-all.mangadex-v1.4.16.apk", ext.Apk);
            Assert.Equal("https://cdn.example.com/jar/tachiyomi-all.mangadex-v1.4.16.jar", ext.Jar);
            Assert.Equal("https://cdn.example.com/icon/eu.kanade.tachiyomi.extension.all.mangadex.png", ext.Icon);
            Assert.Null(ext.IconUrl);
            Assert.Equal("1.4.16", ext.Version);
            Assert.Equal(42, ext.VersionCode);
            Assert.Equal(1, ext.Nsfw);
            Assert.Equal(0, ext.Mixed);
            // Two sources with different languages -> "all"
            Assert.Equal("all", ext.Language);
            Assert.Equal(2, ext.Sources.Count);
            Assert.Equal("2499283573021220255", ext.Sources[0].Id);
            Assert.Equal("en", ext.Sources[0].Language);
            Assert.Equal("https://mangadex.org", ext.Sources[0].BaseUrl);
            Assert.Equal(new List<string> { "https://mirror.mangadex.org" }, ext.Sources[1].MirrorUrls);
        }

        [Fact]
        public void ParseIndexBody_NewFormat_MixedContentWarning_SetsMixed()
        {
            var body = """
            {
              "extensionList": {
                "extensions": [
                  {
                    "name": "Tachiyomi: Example",
                    "packageName": "eu.kanade.tachiyomi.extension.en.example",
                    "resources": { "apkUrl": "https://cdn.example.com/apk/example.apk" },
                    "versionCode": "7",
                    "versionName": "1.0.7",
                    "contentWarning": "CONTENT_WARNING_MIXED",
                    "sources": [
                      { "id": "1", "name": "Example", "language": "en", "homeUrl": "https://example.com" },
                      { "id": "2", "name": "Example Two", "language": "en", "homeUrl": "https://two.example.com" }
                    ]
                  }
                ]
              }
            }
            """;

            var extensions = RepositoryDownloader.ParseIndexBody(body);

            Assert.NotNull(extensions);
            var ext = Assert.Single(extensions!);
            Assert.Equal(0, ext.Nsfw);
            Assert.Equal(1, ext.Mixed);
            // Two sources sharing one language keep it instead of collapsing to "all".
            Assert.Equal("en", ext.Language);
        }

        [Fact]
        public void ParseIndexBody_LegacyFormat_ParsesAsToday()
        {
            var extensions = RepositoryDownloader.ParseIndexBody(LegacyIndex);

            Assert.NotNull(extensions);
            var ext = Assert.Single(extensions!);
            Assert.Equal("eu.kanade.tachiyomi.extension.all.comick", ext.Package);
            Assert.Equal("tachiyomi-all.comick-v1.5.30.apk", ext.Apk);
            Assert.Equal("all", ext.Language);
            Assert.Equal(55, ext.VersionCode);
            Assert.Equal(1, ext.Nsfw);
            Assert.Null(ext.IconUrl);
            var source = Assert.Single(ext.Sources);
            Assert.Equal("https://comick.io", source.BaseUrl);
        }

        [Fact]
        public void ParseIndexBody_ObjectWithoutInlineList_ReturnsNull()
        {
            var extensions = RepositoryDownloader.ParseIndexBody("""{ "name": "Repo", "extensionListUrl": "https://example.com/list.json" }""");
            Assert.Null(extensions);
        }
    }
}
