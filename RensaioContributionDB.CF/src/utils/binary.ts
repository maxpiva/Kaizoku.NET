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
 * Convert binary data read from a BLOB column into a base64 string.
 * D1 may return the value as an ArrayBuffer or a Uint8Array.
 */
export function blobToBase64(blob: ArrayBuffer | Uint8Array | null): string | null {
  if (blob === null || blob === undefined) {
    return null;
  }

  let bytes: Uint8Array;
  if (blob instanceof Uint8Array) {
    bytes = blob;
  } else if (blob instanceof ArrayBuffer) {
    bytes = new Uint8Array(blob);
  } else {
    return null;
  }

  let binary = '';
  const chunkSize = 0x8000; // 32KB chunks to avoid call-stack limits
  for (let i = 0; i < bytes.length; i += chunkSize) {
    binary += String.fromCharCode(...bytes.subarray(i, i + chunkSize));
  }
  return btoa(binary);
}
