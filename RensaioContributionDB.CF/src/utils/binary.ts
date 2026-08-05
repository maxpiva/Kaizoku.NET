/**
 * Convert a base64 string into binary for storage in a BLOB column.
 * Throws when the value is present but not a base64 string.
 */
export function base64ToBlob(value: unknown): ArrayBuffer | null {
  if (value === undefined || value === null) {
    return null;
  }
  if (typeof value !== 'string') {
    throw new Error('Expected a base64-encoded string');
  }
  const binary = atob(value);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i += 1) {
    bytes[i] = binary.charCodeAt(i);
  }
  return bytes.buffer;
}

/**
 * What a BLOB column may come back as from D1 depending on engine/runtime:
 *   - local Miniflare:        ArrayBuffer
 *   - Workers runtime:        Uint8Array (or other typed array views)
 *   - HTTP/CLI JSON path:     plain array of byte numbers
 *   - some engines:           base64 string
 */
export type BlobValue = ArrayBuffer | Uint8Array | number[] | string | null;

/**
 * Normalize any of the D1 BLOB representations into a Uint8Array.
 * Returns null for NULL/undefined.
 */
export function toUint8Array(blob: BlobValue): Uint8Array | null {
  if (blob === null || blob === undefined) {
    return null;
  }

  // Live D1 returns BLOBs as base64 strings in some engines.
  if (typeof blob === 'string') {
    const binary = atob(blob);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i += 1) {
      bytes[i] = binary.charCodeAt(i);
    }
    return bytes;
  }

  if (blob instanceof Uint8Array) {
    return blob;
  }

  if (blob instanceof ArrayBuffer) {
    return new Uint8Array(blob);
  }

  // ArrayBufferView (other typed arrays, DataView) — defensive.
  if (ArrayBuffer.isView(blob)) {
    return new Uint8Array(blob.buffer, blob.byteOffset, blob.byteLength);
  }

  // Plain array of byte numbers (HTTP/CLI JSON serialization of a BLOB).
  if (Array.isArray(blob)) {
    return Uint8Array.from(blob);
  }

  throw new Error(
    `Unsupported BLOB type returned by D1: ${blob === null ? 'null' : typeof blob}`
  );
}

/**
 * Convert binary data read from a BLOB column into a base64 string.
 * Accepts every representation D1 may return (see BlobValue).
 */
export function blobToBase64(blob: BlobValue): string | null {
  const bytes = toUint8Array(blob);
  if (bytes === null) {
    return null;
  }

  let binary = '';
  const chunkSize = 0x8000; // 32KB chunks to avoid call-stack limits
  for (let i = 0; i < bytes.length; i += chunkSize) {
    binary += String.fromCharCode(...bytes.subarray(i, i + chunkSize));
  }
  return btoa(binary);
}
