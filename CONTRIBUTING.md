# Contributing to PassReset

## Commit Convention

This project uses [Conventional Commits](https://www.conventionalcommits.org/).

```
type(scope): subject
```

**Types**

| Type | When to use |
|------|-------------|
| `feat` | New feature |
| `fix` | Bug fix |
| `refactor` | Code change with no behavior change |
| `docs` | Documentation only |
| `chore` | Build, tooling, deps |
| `test` | Tests |
| `ci` | CI/CD pipeline changes |
| `perf` | Performance improvement |
| `style` | Formatting, whitespace |

**Scopes** (optional): `web`, `provider`, `common`, `deploy`, `docs`, `ci`, `deps`

Examples:
```
feat(provider): add LDAPS support via LdapUseSsl option
fix(web): gate recaptcha token on Enabled flag
docs(deploy): update IIS setup for Server 2019/2022/2025
```

A `commit-msg` hook enforces this format. To activate it:

```sh
git config core.hooksPath .githooks
```

## Branch Naming

```
feature/<short-description>
fix/<short-description>
chore/<short-description>
```

## Release Workflow

1. Merge all changes to `master`.
2. Tag the commit: `git tag v1.2.3 && git push origin v1.2.3`
3. The `release.yml` workflow publishes the zip and creates a GitHub Release automatically.

To build locally:

```powershell
.\deploy\Publish-PassReset.ps1 -Version v1.2.3
```

The zip lands in `deploy/PassReset-v1.2.3.zip` (excluded from git via `.gitignore`).
