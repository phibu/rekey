/**
 * FEAT-004: Computes a SHA-1 hash of the input string using WebCrypto and returns
 * it as a lowercase hex string. Used to derive the 5-char prefix sent to the
 * HIBP k-anonymity endpoint — plaintext never leaves the browser.
 */
export async function sha1Hex(input: string): Promise<string> {
  const bytes = new TextEncoder().encode(input);
  const hash = await crypto.subtle.digest('SHA-1', bytes);
  return Array.from(new Uint8Array(hash))
    .map((b) => b.toString(16).padStart(2, '0'))
    .join('');
}
