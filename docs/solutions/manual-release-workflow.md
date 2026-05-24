# Manual release workflow

`[REPO]` GitHub Releases are **manual only** for KOTORModSync. Merging to `master` does not publish a release.

## Why this exists

CI builds and tests on every push, but shipping a version to users requires an intentional workflow dispatch. This avoids accidental public releases from routine merges.

## Authoritative runbook

See [docs/manual-release.md](../manual-release.md) for the full 3-step flow:

1. Merge feature work to `master`
2. (Optional) **Release Please** workflow — version-bump PR only (`skip-github-release: true`)
3. **Build and Release** workflow — set `create_github_release=true` when ready to publish

## Agent checklist

| Step | Command / action |
|------|------------------|
| Confirm current version | Read `MainConfig.CurrentVersion` or `.release-please-manifest.json` |
| Verify alignment test | `dotnet test src/KOTORModSync.Tests/KOTORModSync.Tests.csproj --filter "FullyQualifiedName~ReleaseVersionAlignment"` |
| List releases | `gh release list` |
| Do **not** | Tag or release from agent commits without explicit user request |

## Common mistakes

- Expecting a release artifact after merging a docs-only PR
- Bumping version locally without Release Please / manifest alignment
- Setting `update_appcast` without `create_github_release` (appcast job is skipped unless publishing)

## Related

- Plan: [docs/plans/2026-05-24-015-fix-manual-release-workflow-plan.md](../plans/2026-05-24-015-fix-manual-release-workflow-plan.md) (historical)
- Copilot routing: `.github/copilot-instructions.md` → `docs/manual-release.md`
