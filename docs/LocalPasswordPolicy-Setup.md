# Local Password Policy Setup

PassReset supports two operator-managed offline password-policy layers, both
optional and configured under `PasswordChangeOptions.LocalPolicy`:

1. **Banned-words list** — a plaintext file of corp-specific terms. Passwords
   containing any term (case-insensitive substring match) are rejected.
2. **Local HIBP corpus** — an on-disk mirror of the HaveIBeenPwned SHA-1 breach
   corpus, used instead of the remote HIBP API. Intended for air-gapped networks.

Both features are independent. Either, both, or neither may be configured.

## When to use

- **Air-gapped networks:** no outbound internet access for the HIBP API call
- **Compliance drivers:** policies requiring custom banned-term enforcement
  (internal product codenames, executive surnames, brand identifiers)
- **Higher-throughput deployments:** avoids HIBP rate limits / latency

## Configuration

In `appsettings.Production.json`:

```jsonc
{
  "PasswordChangeOptions": {
    "LocalPolicy": {
      "BannedWordsPath": "C:\\PassReset\\config\\banned-words.txt",
      "LocalPwnedPasswordsPath": "C:\\PassReset\\pwned-hashes\\",
      "MinBannedTermLength": 4
    }
  }
}
```

**Any missing path disables the corresponding feature** without blocking service startup.

### `BannedWordsPath`

Absolute path to a UTF-8 plaintext file. Service startup fails if the path is set
but the file does not exist.

### `LocalPwnedPasswordsPath`

Absolute path to a directory containing per-prefix HIBP range files. Startup
fails if the path is set but the directory is missing or empty of valid prefix
files. **When set, remote HIBP calls are disabled automatically.**

### `MinBannedTermLength`

Minimum length (characters) for a banned term to be considered. Terms shorter
than this are skipped at load time. Protects against DoS-style single-character
entries dominating the check. Default: 4. Must be >= 1.

## Banned-words file format

- One term per line
- UTF-8 encoded
- Lines starting with `#` are comments (ignored)
- Blank lines are ignored
- Each term is trimmed of surrounding whitespace
- Terms are case-folded to lowercase; matching is case-insensitive substring

Example:

```
# Corp banned terms — updated 2026-04-21
# Product codenames
bluewidget
orangewave
# Executive surnames
smithjones
```

## HIBP SHA-1 corpus — obtaining and layout

Use the official [HaveIBeenPwned PwnedPasswordsDownloader](https://github.com/HaveIBeenPwned/PwnedPasswordsDownloader):

```bash
# Install
dotnet tool install --global haveibeenpwned-downloader

# Download in per-prefix layout, SHA-1 mode
haveibeenpwned-downloader -s false -d C:\PassReset\pwned-hashes
```

Expected result: ~1 million files named `00000.txt` through `FFFFF.txt`, total
~35 GB on disk. Each file contains lines formatted
`<35-hex-suffix>:<occurrence-count>`.

PassReset does NOT require the count column; only the suffix is used for
membership checking. Existing HIBP-standard files work as-is.

## File permissions

Both the banned-words file and the HIBP corpus directory should be readable
only by the IIS app-pool identity running PassReset:

```powershell
icacls "C:\PassReset\config\banned-words.txt" /inheritance:r `
  /grant "IIS AppPool\PassReset:(R)" `
  /grant "BUILTIN\Administrators:(F)"

icacls "C:\PassReset\pwned-hashes" /inheritance:r `
  /grant "IIS AppPool\PassReset:(RX)" `
  /grant "BUILTIN\Administrators:(F)" /T
```

## Refresh cadence

Both the banned-words file and the HIBP corpus are loaded lazily (banned-words
at service start; HIBP per-prefix on first lookup). To pick up changes:

```powershell
Restart-WebAppPool -Name "PassReset"
```

HIBP data is typically refreshed quarterly by HIBP; re-download the corpus
and restart the service on your chosen cadence.

## Troubleshooting

**"BannedWordsPath configured but file not found"** at startup: check the path
in config, verify the app-pool identity can see the file.

**"LocalPwnedPasswordsPath contains no HIBP prefix files"**: the directory
exists but has no `<5-hex>.txt` files. Did the downloader run to completion?

**No rejections happening with banned-words file set**: check the startup log
for `BannedWordsChecker loaded N terms from ...`. If `N = 0`, all terms were
filtered by `MinBannedTermLength`.

**How do I confirm remote HIBP is disabled?** Startup log line
`LocalPwnedPasswordsChecker enabled (root=...)` indicates the local path is
active; the `PwnedPasswordChecker` logs will stop showing HTTP attempts.

## Error codes

When local policy rejects a password, the API returns:

- `BannedWord` (20) — banned-words match
- `LocallyKnownPwned` (21) — local HIBP match

The user-facing message for both is identical
("This password is not allowed by local policy.") — operators can distinguish
via SIEM events and logs.
