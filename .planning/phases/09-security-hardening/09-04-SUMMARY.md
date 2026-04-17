---
phase: 09-security-hardening
plan: 04
requirements: [STAB-016]
status: complete
commits:
  - b2ad631 fix(web): STAB-016 resolve WebSettings via IOptions at request time
---

# Plan 09-04 Summary — STAB-016

## Outcome

HSTS header emission is now driven by `IOptions<WebSettings>` resolved from
`HttpContext.RequestServices` at request time. The previous implementation
captured `webSettings` by closure during startup, so integration tests that
overrode `EnableHttpsRedirect` via `ConfigureAppConfiguration` could not flip
HSTS emission. Resolving options per-request picks up the current bound value
and makes the middleware fully test-observable.

## Files Modified

- `src/PassReset.Web/Program.cs` — security-headers middleware resolves
  `runtimeWeb = context.RequestServices.GetRequiredService<IOptions<WebSettings>>().Value`
  instead of using the captured `webSettings` local.
- `src/PassReset.Tests/Web/Startup/HttpsRedirectionTests.cs` — 3 integration tests
  with two factory fixtures (`HstsEnabledFactory`, `HstsDisabledFactory`).

## Verification

- `dotnet test --filter "FullyQualifiedName~HttpsRedirectionTests"` → 3/3 green.
- HSTS present when `EnableHttpsRedirect=true` with `max-age=31536000; includeSubDomains` ✓
- HSTS never contains `preload` directive (D-12) ✓
- HSTS absent when `EnableHttpsRedirect=false` ✓

## Notes

The production change (capture → request-time resolution) is tiny but
load-bearing for test observability. It also makes `WebSettings` reloadable
in principle (e.g. for future config hot-reload) without requiring an app
restart to refresh middleware state.
