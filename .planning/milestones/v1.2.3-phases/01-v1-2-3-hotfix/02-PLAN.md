---
phase: 01-v1-2-3-hotfix
plan: 02
type: execute
wave: 1
depends_on: []
files_modified:
  - src/PassReset.Common/ApiErrorCode.cs
  - src/PassReset.PasswordProvider/PasswordChangeProvider.cs
  - src/PassReset.Web/ClientApp/src/types/settings.ts
  - src/PassReset.Web/appsettings.json
  - src/PassReset.Web/appsettings.Production.template.json
  - docs/appsettings-Production.md
  - CHANGELOG.md
autonomous: true
requirements:
  - BUG-002

must_haves:
  truths:
    - "When AD rejects ChangePassword with HRESULT 0x80070005 (E_ACCESSDENIED), the API returns ApiErrorCode.PasswordTooRecentlyChanged (19), not Generic (0)"
    - "When AD rejects with HRESULT 0x8007202F (ERROR_DS_CONSTRAINT_VIOLATION), the API also returns PasswordTooRecentlyChanged"
    - "The frontend displays the localized 'password changed too recently' message from ClientSettings.Alerts.errorPasswordTooRecentlyChanged"
    - "HResult + COMException.Message are preserved in a warning log so operators can diagnose actual ACL issues masquerading as min-age"
    - "Existing pre-check PasswordTooYoung (code 13) behaviour is unchanged — new code 19 only fires on the AD-side rejection path"
  artifacts:
    - path: "src/PassReset.Common/ApiErrorCode.cs"
      provides: "PasswordTooRecentlyChanged = 19"
      contains: "PasswordTooRecentlyChanged"
    - path: "src/PassReset.PasswordProvider/PasswordChangeProvider.cs"
      provides: "HResult classification inside ChangePasswordInternal catch (COMException)"
      contains: "PasswordTooRecentlyChanged"
    - path: "src/PassReset.Web/ClientApp/src/types/settings.ts"
      provides: "ApiErrorCode.PasswordTooRecentlyChanged: 19 + Alerts.errorPasswordTooRecentlyChanged?: string"
      contains: "PasswordTooRecentlyChanged"
    - path: "src/PassReset.Web/appsettings.json"
      provides: "Default ClientSettings.Alerts.errorPasswordTooRecentlyChanged string"
      contains: "errorPasswordTooRecentlyChanged"
  key_links:
    - from: "src/PassReset.PasswordProvider/PasswordChangeProvider.cs"
      to: "src/PassReset.Common/ApiErrorCode.cs"
      via: "ApiErrorCode.PasswordTooRecentlyChanged enum reference"
      pattern: "ApiErrorCode\\.PasswordTooRecentlyChanged"
    - from: "src/PassReset.Web/ClientApp/src/types/settings.ts"
      to: "src/PassReset.Common/ApiErrorCode.cs"
      via: "Mirrored numeric value 19"
      pattern: "PasswordTooRecentlyChanged:\\s*19"
---

<objective>
Fix BUG-002: Map AD's `E_ACCESSDENIED (0x80070005)` rejection during `UserPrincipal.ChangePassword` to a dedicated `ApiErrorCode.PasswordTooRecentlyChanged` with a localized user-facing message, instead of bubbling up as generic "Unexpected Error".

Purpose: Close the diagnostic gap where users hit AD's `minPwdAge` just after a recent change (clock skew / replication lag / DC policy variance past the pre-check) and see a useless generic error.

Output: New enum value in `PassReset.Common`, HResult classification in the provider, mirrored TypeScript const + Alerts string, default i18n copy in appsettings, docs + CHANGELOG entries.
</objective>

<execution_context>
@C:/Users/Phibu/Claude-Projekte/AD-Passreset-Portal/.claude/get-shit-done/workflows/execute-plan.md
@C:/Users/Phibu/Claude-Projekte/AD-Passreset-Portal/.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/PROJECT.md
@.planning/ROADMAP.md
@.planning/REQUIREMENTS.md
@.planning/phases/01-v1-2-3-hotfix/RESEARCH.md

@src/PassReset.Common/ApiErrorCode.cs
@src/PassReset.PasswordProvider/PasswordChangeProvider.cs
@src/PassReset.Web/ClientApp/src/types/settings.ts
@src/PassReset.Web/appsettings.json

<interfaces>
<!-- Known shape from RESEARCH.md reading -->

PassReset.Common.ApiErrorCode (current last entry): ApproachingLockout = 18
New: PasswordTooRecentlyChanged = 19

ApiErrorException shape (existing):
  new ApiErrorException(new ApiErrorItem(ApiErrorCode code, string friendlyMessage))

ChangePasswordInternal existing catch structure (PasswordChangeProvider.cs ~line 361):
  try { userPrincipal.ChangePassword(...); }
  catch (COMException comEx) { /* existing AllowSetPasswordFallback branch */ throw; }

Top-level catch at ~line 118 already routes ApiErrorException → ApiErrorItem via apiError.ToApiErrorItem().

Frontend types/settings.ts:
  - ApiErrorCode const (numeric mirror) at ~line 111
  - Alerts interface at ~line 33 with errorPasswordTooYoung? already present
</interfaces>
</context>

<tasks>

<task type="auto" tdd="true">
  <name>Task 1: Add ApiErrorCode.PasswordTooRecentlyChanged and wire HResult classification</name>
  <files>src/PassReset.Common/ApiErrorCode.cs, src/PassReset.PasswordProvider/PasswordChangeProvider.cs</files>
  <behavior>
    - COMException with HResult == unchecked((int)0x80070005) inside ChangePasswordInternal → ApiErrorException(ApiErrorCode.PasswordTooRecentlyChanged, friendly message)
    - COMException with HResult == unchecked((int)0x8007202F) → same mapping
    - Any other HResult → existing behaviour preserved (AllowSetPasswordFallback branch, then rethrow)
    - HResult classification runs BEFORE the AllowSetPasswordFallback branch per RESEARCH.md pitfall — min-age rejection must NOT trigger SetPassword fallback (which would bypass history)
    - Warning log emitted with: user SamAccountName, HResult formatted as 0x{X8}, and the COMException.Message — to let operators distinguish actual ACL issues from min-age
  </behavior>
  <action>
    1. In `src/PassReset.Common/ApiErrorCode.cs`, append after `ApproachingLockout = 18`:
       ```csharp
       /// <summary>
       /// Active Directory rejected the password change because the domain's minimum password age
       /// (<c>minPwdAge</c>) has not yet elapsed since the previous change. Distinct from
       /// <see cref="PasswordTooYoung"/> which is the portal-side pre-check.
       /// </summary>
       PasswordTooRecentlyChanged = 19,
       ```
       Preserve numeric stability — do NOT renumber existing values. Per RESEARCH.md A4, additive enum values are backward-compatible.

    2. In `src/PassReset.PasswordProvider/PasswordChangeProvider.cs` inside `ChangePasswordInternal`'s `catch (COMException comEx)` block (around line 361), insert HResult classification **at the top of the catch, before the AllowSetPasswordFallback branch**:
       ```csharp
       // BUG-002: classify well-known HResults BEFORE any SetPassword fallback.
       // Min-age rejection must never be routed through SetPassword (which bypasses history).
       const int E_ACCESSDENIED = unchecked((int)0x80070005);
       const int ERROR_DS_CONSTRAINT_VIOLATION = unchecked((int)0x8007202F);

       if (comEx.HResult == E_ACCESSDENIED || comEx.HResult == ERROR_DS_CONSTRAINT_VIOLATION)
       {
           _logger.LogWarning(comEx,
               "AD rejected ChangePassword for {User} with HRESULT=0x{Hex:X8}; message={Message}. " +
               "Treating as minimum-password-age violation. If this user IS allowed to change password, " +
               "verify the service account has the 'Change Password' extended right.",
               userPrincipal.SamAccountName,
               comEx.HResult,
               comEx.Message);

           throw new ApiErrorException(new ApiErrorItem(
               ApiErrorCode.PasswordTooRecentlyChanged,
               "Your password was changed too recently. Please wait before trying again."));
       }

       // Existing AllowSetPasswordFallback branch follows unchanged...
       ```

       Verify no other `catch (COMException ...)` in the file also needs updating — scope the change narrowly to `ChangePasswordInternal`.

    3. Confirm (via grep) that the top-level `catch (Exception ex)` at ~line 118 already handles `ApiErrorException` by returning `apiError.ToApiErrorItem()`. No change needed there.

    Per RESEARCH.md pitfall: the warning log preserves HResult + message so operators can diagnose a misconfigured service account (access-denied due to missing "Change Password" right) vs. a genuine min-age rejection.
  </action>
  <verify>
    <automated>dotnet build src/PassReset.sln --configuration Release</automated>
  </verify>
  <done>
    `ApiErrorCode.PasswordTooRecentlyChanged = 19` exists. `PasswordChangeProvider.cs` classifies E_ACCESSDENIED and ERROR_DS_CONSTRAINT_VIOLATION inside the COMException catch, **before** the AllowSetPasswordFallback branch, via ApiErrorException. Release build succeeds. Existing PasswordTooYoung (13) pre-check is untouched.
  </done>
</task>

<task type="auto">
  <name>Task 2: Mirror enum + Alerts in frontend types and add default i18n copy</name>
  <files>src/PassReset.Web/ClientApp/src/types/settings.ts, src/PassReset.Web/appsettings.json, src/PassReset.Web/appsettings.Production.template.json</files>
  <action>
    1. `src/PassReset.Web/ClientApp/src/types/settings.ts`:
       - Add `PasswordTooRecentlyChanged: 19` to the `ApiErrorCode` const (at ~line 111) — preserve numeric ordering, match existing formatting style (trailing `as const` block).
       - Add `errorPasswordTooRecentlyChanged?: string;` to the `Alerts` interface (~line 33), grouped near the existing `errorPasswordTooYoung?: string;` for consistency.

    2. `src/PassReset.Web/appsettings.json` (default development settings):
       - Under `ClientSettings.Alerts`, add:
         ```json
         "errorPasswordTooRecentlyChanged": "Your password was changed too recently. Please wait and try again."
         ```
       - Match tone of existing `errorPasswordTooYoung` entry per RESEARCH.md recommendation.

    3. `src/PassReset.Web/appsettings.Production.template.json`:
       - Add the same key under `ClientSettings.Alerts` so operator template stays in sync with defaults.

    Note: per RESEARCH.md, the frontend already has a pattern where missing `Alerts.*` optional strings fall back to a default (verify during implementation — if it doesn't, no change required here because we're providing the default copy in appsettings.json). No changes to `PasswordForm.tsx` error-display logic expected; the numeric code → alert string mapping should already be generic.
  </action>
  <verify>
    <automated>cd src/PassReset.Web/ClientApp && npm run build</automated>
  </verify>
  <done>
    `npm run build` (tsc + vite) passes with no new type errors. `ApiErrorCode.PasswordTooRecentlyChanged === 19` in the TS const. `Alerts.errorPasswordTooRecentlyChanged` is a defined optional field. `appsettings.json` and template both carry the default string.
  </done>
</task>

<task type="auto">
  <name>Task 3: Update docs and CHANGELOG</name>
  <files>docs/appsettings-Production.md, CHANGELOG.md</files>
  <action>
    1. `docs/appsettings-Production.md` — add a row in the `ClientSettings.Alerts` reference table:
       - Key: `ClientSettings.Alerts.errorPasswordTooRecentlyChanged`
       - Default: `"Your password was changed too recently. Please wait and try again."`
       - Description: "Shown when Active Directory rejects the change due to the domain's `minPwdAge` (distinct from the portal-side pre-check message `errorPasswordTooYoung`). Operators may localize or rephrase."

    2. `CHANGELOG.md` — under `[Unreleased]` → `### Fixed`, add:
       ```
       - **Provider**: AD `minPwdAge` rejections (COMException HRESULT `0x80070005` and
         `0x8007202F` during `UserPrincipal.ChangePassword`) now surface as
         `ApiErrorCode.PasswordTooRecentlyChanged` with a localized message instead of
         "Unexpected Error". Original HResult and message remain in warning logs for
         operator diagnosis. (BUG-002)
       ```

    Verify via grep that no README.md section references the old "Unexpected Error" text for this flow; if it does, update it. Otherwise leave README untouched (no user-visible UI change beyond the message string).
  </action>
  <verify>
    <automated>dotnet build src/PassReset.sln --configuration Release</automated>
  </verify>
  <done>
    Docs row added under Alerts. CHANGELOG `[Unreleased]` lists the fix. Build still passes.
  </done>
</task>

</tasks>

<verification>
- `dotnet build src/PassReset.sln --configuration Release` — zero new warnings.
- `cd src/PassReset.Web/ClientApp && npm run build` — tsc passes, vite bundles.
- `git grep -n "PasswordTooRecentlyChanged"` hits: ApiErrorCode.cs (`= 19`), PasswordChangeProvider.cs (HResult mapping + ApiErrorException), settings.ts (`: 19`), appsettings.json (Alerts string), appsettings.Production.template.json, docs/appsettings-Production.md, CHANGELOG.md.
- `git grep -n "E_ACCESSDENIED\|0x80070005" -- src/PassReset.PasswordProvider/` shows the constant is scoped to the new handler in `ChangePasswordInternal`.
- Manual (staging): change a user's password twice within `minPwdAge`. Expected: UI shows `errorPasswordTooRecentlyChanged` copy. Server log emits one WARNING with the HResult and sam-account-name.
</verification>

<acceptance_criteria>
From REQUIREMENTS.md BUG-002:
> When AD rejects a password change because the domain's `minPwdAge` has not elapsed, the portal surfaces a dedicated `ApiErrorCode.PasswordTooRecentlyChanged` with a localized user-facing message (no generic "Unexpected Error"). SIEM event logged appropriately.

From ROADMAP.md Phase 1 success criterion #2:
> A user who retries a password change within the domain `minPwdAge` window sees a dedicated "password changed too recently" message (not "Unexpected Error"), and a matching SIEM event is emitted.

SIEM note: per RESEARCH.md, no new SIEM event type is required — existing `ValidationFailed` (or equivalent path that currently handles `PasswordTooYoung`) covers this. During implementation, grep PasswordController for the PasswordTooYoung SIEM branch and route PasswordTooRecentlyChanged through the same event type for consistency. If no such branch exists, leave SIEM untouched (the warning log from Task 1 already captures the event).
</acceptance_criteria>

<pitfalls>
From RESEARCH.md BUG-002:
- **Over-eager mapping:** Mapping *every* access-denied to `PasswordTooRecentlyChanged` can mask a misconfigured service account as a user problem. Mitigation: the warning log preserves HResult + `comEx.Message` + guidance about the "Change Password" extended right.
- **Interaction with `AllowSetPasswordFallback`:** HResult classification MUST run before the fallback branch. Min-age rejection must NOT route into SetPassword (which bypasses password history).
- **SetPassword bypasses history:** unchanged — do not route min-age rejection into it.
- **Pre-check coexists:** `PasswordTooYoung = 13` (portal pre-check) remains untouched; `PasswordTooRecentlyChanged = 19` is the new AD-side rejection code. Both error codes exist with distinct copy.
- **Enum additive only:** do NOT renumber existing values (A4 in Assumptions Log — additive is backward-compatible).
</pitfalls>

<success_criteria>
- BUG-002 ready for release verification.
- No breaking changes: existing `appsettings.Production.json` without the new Alerts key continues to work (the key is optional; default copy is shipped in `appsettings.json`).
- Enum additive, TS mirror numeric, matches C# value.
- Release build + frontend build both pass.
</success_criteria>

<commits>
Expected: two commits (split by concern for clean review).
- `fix(provider): map E_ACCESSDENIED from AD to PasswordTooRecentlyChanged (BUG-002)` — ApiErrorCode.cs + PasswordChangeProvider.cs.
- `fix(web): add PasswordTooRecentlyChanged frontend mirror + default alert copy (BUG-002)` — settings.ts + appsettings.json + template + docs/appsettings-Production.md + CHANGELOG.md.

Single-commit alternative also acceptable: `fix(provider): map E_ACCESSDENIED to PasswordTooRecentlyChanged (BUG-002)` covering all files.
</commits>

<output>
After completion, create `.planning/phases/01-v1-2-3-hotfix/02-SUMMARY.md` per template.

**After-phase step (not in this plan):** Tag `v1.2.3` happens only after plans 01, 02, 03 all pass verification; see CLAUDE.md release process.
</output>
