/**
 * Derive AES-256-CBC key and IV from the concatenated base64 secret.
 *
 * The `aeskey256iv` secret is: base64(32-byte-key + 16-byte-iv)
 * = 64 base64 characters.
 *
 * In Workers runtime, Web Crypto is available via `crypto.subtle`.
 */
import { toUint8Array, type BlobValue } from './binary';

const KEY_LENGTH = 32; // AES-256
const IV_LENGTH = 16;  // CBC IV

/**
 * Parse the concatenated key+IV from the environment secret.
 * Returns the raw key bytes and IV bytes.
 */
export function parseAesKeyIv(encoded: string): { keyBytes: ArrayBuffer; iv: Uint8Array } {
  const raw = atob(encoded);
  if (raw.length !== KEY_LENGTH + IV_LENGTH) {
    throw new Error(
      `aeskey256iv must decode to ${KEY_LENGTH + IV_LENGTH} bytes, got ${raw.length}`
    );
  }

  const keyBytes = new ArrayBuffer(KEY_LENGTH);
  const keyView = new Uint8Array(keyBytes);
  const iv = new Uint8Array(IV_LENGTH);

  for (let i = 0; i < KEY_LENGTH; i += 1) {
    keyView[i] = raw.charCodeAt(i);
  }
  for (let i = 0; i < IV_LENGTH; i += 1) {
    iv[i] = raw.charCodeAt(KEY_LENGTH + i);
  }

  return { keyBytes, iv };
}

/**
 * Import the AES key for Web Crypto operations.
 */
async function importAesKey(keyBytes: ArrayBuffer): Promise<CryptoKey> {
  return crypto.subtle.importKey(
    'raw',
    keyBytes,
    { name: 'AES-CBC' },
    false,           // not extractable
    ['encrypt']      // only encryption needed
  );
}

/**
 * Transform raw binary data for export:
 *   BLOB → AES-256-CBC encrypt → base64
 *
 * Returns the final base64 string ready for sources.json.
 */
export async function encryptSourceData(
  rawData: BlobValue,
  aeskey256iv: string
): Promise<string> {
  const { keyBytes, iv } = parseAesKeyIv(aeskey256iv);
  const key = await importAesKey(keyBytes);

  // D1 returns BLOBs differently across engines: ArrayBuffer (local
  // Miniflare), Uint8Array, or a base64 string (live D1 HTTP). Normalize
  // before passing to Web Crypto, which strictly requires a JsBufferSource.
  const bytes = toUint8Array(rawData);
  if (bytes === null) {
    throw new Error('Cannot encrypt empty source data');
  }

  // AES-256-CBC encrypt
  const encrypted = await crypto.subtle.encrypt(
    { name: 'AES-CBC', iv },
    key,
    bytes
  );

  // base64 encode
  const encryptedBytes = new Uint8Array(encrypted);
  let binary = '';
  const chunkSize = 0x8000;
  for (let i = 0; i < encryptedBytes.length; i += chunkSize) {
    binary += String.fromCharCode(...encryptedBytes.subarray(i, i + chunkSize));
  }
  return btoa(binary);
}
