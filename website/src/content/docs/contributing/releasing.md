---
title: Releasing
description: How to publish a new NSmithy release.
---

NSmithy releases are published by creating a GitHub release.

## Version

The published NuGet package and codegen JAR versions are derived from the GitHub
release tag. The release workflow strips the leading `v` and passes the result to the
build (`-p:Version` / `-Pversion`), overriding the `0.0.0-SNAPSHOT` dev placeholders
that local builds use (`VersionPrefix` in `Directory.Build.props`, the default in
`codegen/build.gradle.kts`).

The repo-root `VERSION` file is separate: it holds the real, user-facing version and
feeds the two surfaces the tag does **not** reach — the docs site (deployed from `main`,
not from tags) and the `NSmithy.Templates` package (whose scaffolded projects must
reference published versions). **Bump `VERSION` to the release version and commit it
before tagging.** If the tag and `VERSION` disagree, the templates pack fails with a
guard error.

## Tag Format

GitHub release tags should match the package version with a `v` prefix.

Example:

- release tag: `v0.6.0`
- package version: `0.6.0`

## GitHub Release Flow

1. Prepare the release commit on `main` (e.g. `chore: prepare 0.6.0 release`):
   - Bump the repo-root `VERSION` file to the release version (e.g. `0.6.0`). This
     updates the docs site and the templates package.
   - Update `CHANGELOG.md`: rename the `[Unreleased]` section to the new version and
     start a fresh, empty `[Unreleased]` above it.
2. In GitHub, create a new release.
3. Create a new tag using the `v<package-version>` format (e.g. `v0.6.0`), matching
   `VERSION`.
4. Write the release notes — reuse the new `CHANGELOG.md` section for that version so
   the GitHub release and the changelog stay in sync.
5. Publish the release.

Publishing the GitHub release triggers the workflow in `.github/workflows/release.yml`,
which builds, tests, packs, and pushes the NuGet packages using the version from the tag.
