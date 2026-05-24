# Manual release process

GitHub Releases are **not** created automatically on push to `master`. Use the workflows below when you intentionally want to ship.

## Current published release

Latest GitHub release: **v2.0.0a1** (pre-release). Version files in the repo should stay aligned at `2.0.0a1` until the next intentional release.

## 3-step flow

### 1. Merge feature work to `master`

Normal PRs only. Nothing in CI publishes a release from a merge.

### 2. (Optional) Open a Release Please version-bump PR

**Actions → Release Please → Run workflow**

- Opens or updates a PR that bumps `CHANGELOG.md`, `.release-please-manifest.json`, `MainConfig.cs`, and `Info.plist` files.
- Does **not** create a GitHub Release or tag (`skip-github-release: true`).

Review and merge that PR when the version bump is correct.

### 3. Build artifacts (and optionally publish)

**Actions → Build and Release KOTORModSync → Run workflow**

| Input | Default | Purpose |
|--------|---------|---------|
| `version` | empty | Tag name (e.g. `v2.0.0a2`). Empty uses `MainConfig.CurrentVersion`. |
| `create_github_release` | `false` | Set **true** only when you want a public GitHub Release with uploaded zips. |
| `update_appcast` | `false` | Set **true** to commit `appcast.xml` and attach it (requires `create_github_release`). |

- With `create_github_release: false`: builds all platform zips as workflow artifacts only (good for smoke-testing).
- With `create_github_release: true`: creates the GitHub Release and uploads artifacts.

### 4. Verify

- Confirm the release on GitHub: `gh release list`
- Confirm in-app version matches manifest: `dotnet test --filter FullyQualifiedName~ReleaseVersionAlignment`

## What changed (why releases stopped auto-firing)

Previously, **Release Please** ran on every push to `master`, opened version PRs for any commit (including internal plan docs), and **created GitHub Releases** when those PRs merged. **Build and Release** also ran on `release` events and when Release Please finished, which amplified the noise.

Now:

- `release-please.yml` — `workflow_dispatch` only, never publishes GitHub Releases.
- `build-and-release.yml` — `workflow_dispatch` only; GitHub Release creation requires `create_github_release: true`.
