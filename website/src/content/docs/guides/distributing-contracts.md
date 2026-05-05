---
title: Distributing Contracts
description: Package Smithy models as a versioned Maven artifact and share them across teams via GitHub Packages.
---

Keeping your Smithy model inside the consuming project works for a single
service, but breaks down when multiple teams or repos need to share the same
contract. The standard Smithy answer is to publish the model as a JAR to a
Maven registry. Any `smithy-build.json` that lists the JAR as a dependency will
have the model available on the Smithy CLI classpath at build time.

This guide shows both [Gradle](#gradle) and [Maven](#maven) approaches with
GitHub Packages. The same approach works with
[JFrog Artifactory](#jfrog-artifactory), [AWS CodeArtifact](#aws-codeartifact),
or any other Maven-compatible registry.

## How Smithy discovers models in JARs

The Smithy CLI scans its classpath for JARs containing a
`META-INF/smithy/manifest` file. The manifest is a newline-delimited list of
paths to `.smithy` files inside the JAR. Any shape defined in those files
becomes available during model assembly — exactly as if the file lived in
`sources` locally.

## Project layout

The model files and manifest are the same regardless of which build tool you
use:

```
my-contracts/
└── src/
    └── main/
        └── resources/
            └── META-INF/
                └── smithy/
                    ├── manifest
                    └── hello.smithy
```

## manifest file

List every `.smithy` file in the JAR, one per line, relative to
`META-INF/smithy/`:

```
hello.smithy
```

If you have multiple files or subdirectories:

```
common/shapes.smithy
hello/service.smithy
hello/errors.smithy
```

## Gradle

### build.gradle.kts

```kotlin
plugins {
    `java`
    `maven-publish`
}

group = "com.example"
version = "1.0.0"

dependencies {
    // needed for trait validation at build time, not included in the JAR
    compileOnly("software.amazon.smithy:smithy-model:1.52.0")
}

publishing {
    publications {
        create<MavenPublication>("contracts") {
            from(components["java"])
        }
    }
    repositories {
        maven {
            name = "GitHubPackages"
            url = uri("https://maven.pkg.github.com/YOUR_ORG/YOUR_REPO")
            credentials {
                username = System.getenv("GITHUB_ACTOR")
                password = System.getenv("GITHUB_TOKEN")
            }
        }
    }
}
```

### Publishing

```bash
export GITHUB_ACTOR=your-github-username
export GITHUB_TOKEN=ghp_...   # needs write:packages scope
./gradlew publish
```

## Maven

### pom.xml

```xml
<?xml version="1.0" encoding="UTF-8"?>
<project>
  <modelVersion>4.0.0</modelVersion>
  <groupId>com.example</groupId>
  <artifactId>my-contracts</artifactId>
  <version>1.0.0</version>

  <dependencies>
    <dependency>
      <groupId>software.amazon.smithy</groupId>
      <artifactId>smithy-model</artifactId>
      <version>1.52.0</version>
      <scope>provided</scope>
    </dependency>
  </dependencies>

  <distributionManagement>
    <repository>
      <id>github</id>
      <url>https://maven.pkg.github.com/YOUR_ORG/YOUR_REPO</url>
    </repository>
  </distributionManagement>
</project>
```

Credentials go in `~/.m2/settings.xml` — set once globally, not per project:

```xml
<settings>
  <servers>
    <server>
      <id>github</id>
      <username>${env.GITHUB_ACTOR}</username>
      <password>${env.GITHUB_TOKEN}</password>
    </server>
  </servers>
</settings>
```

The `<id>github</id>` must match the `<id>` in `distributionManagement`.

### Publishing

```bash
export GITHUB_ACTOR=your-github-username
export GITHUB_TOKEN=ghp_...   # needs write:packages scope
mvn deploy
```

The package appears under **Packages** on the GitHub repository page.

## Consuming in smithy-build.json

Add the private registry and the JAR dependency to the `maven` block:

```json
{
  "version": "1.0",
  "sources": ["model"],
  "maven": {
    "repositories": [
      {
        "url": "https://maven.pkg.github.com/YOUR_ORG/YOUR_REPO",
        "httpCredentials": "${GITHUB_ACTOR}:${GITHUB_TOKEN}"
      }
    ],
    "dependencies": [
      "com.example:my-contracts:1.0.0",
      "io.github.thomaslaich.nsmithy:smithy-csharp-codegen:0.1.0-preview.5"
    ]
  },
  "plugins": {
    "csharp-codegen": {
      "service": "example.hello#HelloService",
      "baseNamespace": ""
    }
  }
}
```

`httpCredentials` supports `${ENV_VAR}` interpolation. The token is read at
build time and never needs to appear in source.

### Local development

If you have the [GitHub CLI](https://cli.github.com/) installed — which most
developers already do — you can source a token from it directly. Add these two
lines to your shell profile (`.bashrc`, `.zshrc`, `config.fish`, etc.):

```bash
export GITHUB_TOKEN=$(gh auth token)
export GITHUB_ACTOR=$(gh api user --jq .login)
```

After that, `dotnet build` just works with no manual token management. The
token is scoped to whatever permissions your `gh auth login` session has, which
includes `read:packages` for packages in any org you belong to.

### CI (GitHub Actions)

`GITHUB_ACTOR` is injected automatically. You only need to expose
`GITHUB_TOKEN` — the default `secrets.GITHUB_TOKEN` has `read:packages` for
packages in the same org:

```yaml
- name: Build
  env:
    GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
  run: dotnet build
```

## Versioning

Treat the contract JAR like a library:

- bump the version in `build.gradle.kts` / `pom.xml` for each release
- pin a specific version in `smithy-build.json` so builds are reproducible
- use a pre-release suffix (e.g. `1.1.0-beta.1`) while a new contract is
  stabilising before committing to it

Adding shapes and optional members is backwards compatible. Removing or
renaming shapes, or making optional members required, is a breaking change and
warrants a major version bump.

## Other registries

The `maven.repositories` array accepts any Maven repository URL. Replace
the GitHub Packages URL and credential format with the appropriate values for
your registry:

### JFrog Artifactory

```json
{
  "url": "https://your-org.jfrog.io/artifactory/smithy-contracts",
  "httpCredentials": "${ARTIFACTORY_USER}:${ARTIFACTORY_TOKEN}"
}
```

### AWS CodeArtifact

CodeArtifact uses short-lived tokens fetched via the AWS CLI:

```bash
export CODEARTIFACT_TOKEN=$(aws codeartifact get-authorization-token \
  --domain your-domain --domain-owner 123456789012 \
  --query authorizationToken --output text)
```

```json
{
  "url": "https://your-domain-123456789012.d.codeartifact.us-east-1.amazonaws.com/maven/your-repo/",
  "httpCredentials": "aws:${CODEARTIFACT_TOKEN}"
}
```

### Self-hosted Nexus / Artifactory OSS

```json
{
  "url": "https://nexus.internal/repository/smithy-contracts/",
  "httpCredentials": "${NEXUS_USER}:${NEXUS_PASSWORD}"
}
```
