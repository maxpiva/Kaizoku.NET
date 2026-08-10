import { Hono } from 'hono';
import type { Env } from '../types';

/**
 * Key route.
 * Base path: /key (set in index.ts)
 *
 * Returns the base64 concatenation of the AES-256 key + IV used to
 * obfuscate source data in the daily export. No authorization required —
 * the key is for obfuscation only, not security.
 */
const keyRoutes = new Hono<{ Bindings: Env }>();

// GET /key
keyRoutes.get('/', (c) => {
  const secret = c.env.AESKEY256IV;
  if (!secret) {
    return c.text('AESKEY256IV secret is not configured', 500);
  }
  return c.text(secret, 200, { 'Content-Type': 'text/plain; charset=utf-8' });
});

export default keyRoutes;
