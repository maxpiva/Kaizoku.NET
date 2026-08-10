using System.Security.Cryptography;

namespace RensaioBackend.Services.Contributions.Snapshot;

/// <summary>
/// AES-CBC/PKCS7 decryption for the snapshot export's <c>data</c> blobs. The key and IV come
/// from the contribution worker's public <c>/key</c> endpoint.
/// </summary>
public static class ContributionSnapshotCrypto
{
    public static byte[] Decrypt(byte[] key, byte[] iv, byte[] cipher)
    {
        using Aes aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;
        aes.IV = iv;
        using ICryptoTransform transform = aes.CreateDecryptor();
        return transform.TransformFinalBlock(cipher, 0, cipher.Length);
    }
}
