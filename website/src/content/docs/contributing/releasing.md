---
title: Releasing
description: How to publish a new NSmithy release.
---

NSmithy releases are published by creating a GitHub release.

## Version

The published NuGet package version is derived from the GitHub release tag. The
release workflow strips the leading `v` and passes the result to the build,
overriding the `VersionPrefix`/`VersionSuffix` placeholders in
`Directory.Build.props` (those are only used for local SNAPSHOT builds).

## Tag Format

GitHub release tags should match the package version with a `v` prefix.

Example:

- release tag: `v0.4.0`
- package version: `0.4.0`

## GitHub Release Flow

1. In GitHub, create a new release.
2. Create a new tag using the `v<package-version>` format (e.g. `v0.4.0`).
3. Publish the release.

Publishing the GitHub release triggers the workflow in `.github/workflows/release.yml`,
which builds, tests, packs, and pushes the NuGet packages using the version from the tag.
