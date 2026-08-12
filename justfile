set shell := ["bash", "-eu", "-o", "pipefail", "-c"]

default:
    just --list

restore:
    dotnet restore NSmithy.slnx

fmt:
    treefmt

# Drop every build output, including the caches that outlive `dotnet clean`.
clean:
    # The Smithy CLI resolves the codegen plugin into a per-project cache under obj/, and the
    # in-repo version is a fixed 0.0.0-SNAPSHOT, so a rebuilt plugin looks unchanged to it and
    # generated code silently stays stale. Deleting obj/ and bin/ takes that cache with it.
    find . -type d \( -name obj -o -name bin \) -not -path './website/*' -prune -exec rm -rf {} +
    find codegen -type d -name build -prune -exec rm -rf {} +
    dotnet nuget locals temp --clear >/dev/null

check-format:
    treefmt --ci

# Stage the bundled Maven repo consumed by NSmithy.MSBuild during `dotnet build`.
codegen:
    # bundleMavenRepo builds the plugins and assembles the offline Maven bundle
    # (it depends on publishToMavenLocal). Staging it into tools/maven-repo means
    # in-repo builds (conformance/examples) resolve NSmithy codegen from the bundle
    # too, so no smithy-build.json needs a ~/.m2 repository entry.
    cd codegen && gradle bundleMavenRepo
    find packages/NSmithy.MSBuild/tools/maven-repo -mindepth 1 -not -name .gitignore -delete
    cp -R codegen/build/maven-bundle/. packages/NSmithy.MSBuild/tools/maven-repo/

# Publish the codegen JARs to Maven Central via the Sonatype Central Portal.
publish-codegen VERSION:
    # Used by the release workflow; expects MAVEN_CENTRAL_USERNAME / MAVEN_CENTRAL_PASSWORD and
    # ORG_GRADLE_PROJECT_signingInMemoryKey / ORG_GRADLE_PROJECT_signingInMemoryKeyPassword to be
    # set in the environment.
    cd codegen && gradle -Pversion={{ VERSION }} :smithy-csharp-codegen:publishAndReleaseToMavenCentral :smithy-proto-codegen:publishAndReleaseToMavenCentral

build: codegen restore
    dotnet build NSmithy.slnx --configuration Release --no-restore --disable-build-servers ${VERSION:+-p:Version=$VERSION}

test:
    cd codegen && gradle test
    dotnet test NSmithy.slnx --configuration Release --no-build --disable-build-servers

pack:
    cd codegen && gradle bundleMavenRepo ${VERSION:+-Pversion=$VERSION}
    find packages/NSmithy.MSBuild/tools/maven-repo -mindepth 1 -not -name .gitignore -delete
    cp -R codegen/build/maven-bundle/. packages/NSmithy.MSBuild/tools/maven-repo/
    bash packages/NSmithy.MSBuild/tools/download-smithy-cli.sh
    dotnet pack NSmithy.slnx --configuration Release --no-build --output artifacts/packages ${VERSION:+-p:Version=$VERSION}

# Build the examples against the freshly packed packages, the way a consumer does.
refresh-examples:
    # Part of `ci`, so an example cannot break unnoticed: the examples consume NSmithy through
    # NuGet and MSBuild rather than project references, which is a path nothing else covers.
    find examples -type d -name obj -prune -exec rm -rf {} +
    dotnet restore examples/examples.slnx --no-cache --force
    # gRPC examples need two build passes: the first generates the .proto file via the
    # smithy build, the second picks it up via the static <Protobuf> glob and compiles
    # it with Grpc.Tools. (MSBuild evaluates Protobuf_Compile's item condition at
    # graph-build time, before dynamic items added inside target bodies are visible.)
    dotnet build examples/examples.slnx --verbosity minimal >/dev/null 2>&1 || true
    dotnet build examples/examples.slnx --verbosity minimal

ci: check-format build test pack refresh-examples

docs:
    cd website && npm install && npm run dev
