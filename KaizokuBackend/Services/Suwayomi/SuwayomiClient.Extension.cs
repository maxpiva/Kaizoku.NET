using System.Net.Http.Json;
using KaizokuBackend.Models;

namespace KaizokuBackend.Services
{
    public partial class SuwayomiClient
    {



        /// <summary>
        /// Gets a list of available extensions
        /// </summary>
        /// <returns>List of extensions</returns>
        public async Task<List<SuwayomiExtension>> GetExtensionsAsync(CancellationToken token = default)
        {
            var url = $"{_apiUrl}/extension/list";
            return (await _http.GetFromJsonAsync<List<SuwayomiExtension>>(url, token).ConfigureAwait(false)) ?? new();
        }

        /// <summary>
        /// Installs an extension by package name
        /// </summary>
        /// <param name="pkgName">Package name of the extension</param>
        /// <returns>True if installation was successful</returns>
        public async Task<bool> InstallExtensionAsync(string pkgName, CancellationToken token = default)
        {
            var url = $"{_apiUrl}/extension/install/{pkgName}";
            var response = await _http.GetAsync(url, token).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }

        /// <summary>
        /// Installs an extension from a file upload
        /// </summary>
        /// <param name="fileContent">The extension file content</param>
        /// <param name="fileName">Name of the extension file</param>
        /// <returns>True if installation was successful</returns>
        public async Task<bool> InstallExtensionFromFileAsync(byte[] fileContent, string fileName, CancellationToken token = default)
        {
            var url = $"{_apiUrl}/extension/install";
            
            using var content = new MultipartFormDataContent();
            using var fileStream = new ByteArrayContent(fileContent);
            content.Add(fileStream, "file", fileName);
            
            var response = await _http.PostAsync(url, content, token).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }

        /// <summary>
        /// Updates an extension by package name
        /// </summary>
        /// <param name="pkgName">Package name of the extension</param>
        /// <returns>True if update was successful</returns>
        public async Task<bool> UpdateExtensionAsync(string pkgName, CancellationToken token = default)
        {
            var url = $"{_apiUrl}/extension/update/{pkgName}";
            var response = await _http.GetAsync(url, token).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }

        /// <summary>
        /// Uninstalls an extension by package name
        /// </summary>
        /// <param name="pkgName">Package name of the extension</param>
        /// <returns>True if uninstallation was successful</returns>
        public async Task<bool> UninstallExtensionAsync(string pkgName, CancellationToken token = default)
        {
            var url = $"{_apiUrl}/extension/uninstall/{pkgName}";
            var response = await _http.GetAsync(url, token).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }

        /// <summary>
        /// Gets the icon for an extension
        /// </summary>
        /// <param name="apkName">APK name of the extension</param>
        /// <returns>Icon as a byte array</returns>
        public async Task<Stream> GetExtensionIconAsync(string apkName, CancellationToken token = default)
        {
            var url = $"{_apiUrl}/extension/icon/{apkName}";
            var response = await _http.GetAsync(url, token).ConfigureAwait(false);
            
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            }

            return new MemoryStream();
        }
    }
}
