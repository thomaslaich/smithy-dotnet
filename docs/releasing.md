# Releasing

NSmithy releases are published by creating a GitHub release.

## Version

NuGet package versions come from `Directory.Build.props`.

Update `VersionPrefix` and `VersionSuffix` before creating a release. Package
versions are not derived from the Git tag.

## Tag Format

GitHub release tags should match the package version with a `v` prefix.

Example:

- package version: `0.1.0-preview.4`
- release tag: `v0.1.0-preview.4`

## GitHub Release Flow

1. Update the version in `Directory.Build.props`.
2. Merge or push the version change to the branch you want to release from.
3. In GitHub, create a new release.
4. Create a new tag using the `v<package-version>` format.
5. Publish the release.

Publishing the GitHub release triggers the workflow in `.github/workflows/release.yml`,
which builds, tests, packs, and pushes the NuGet packages.
