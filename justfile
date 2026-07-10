set shell := ["bash", "-eu", "-o", "pipefail", "-c"]

default:
    just --list

restore:
    dotnet restore NSmithy.slnx

fmt:
    treefmt

check-format:
    treefmt --ci

# Build & publish the Smithy → C# codegen JAR to the local Maven cache so that
# `smithy build` (invoked from each .csproj via NSmithy.MSBuild) can resolve

# `io.github.thomaslaich.nsmithy:smithy-csharp-codegen:0.1.0-SNAPSHOT` from ~/.m2.
codegen:
    cd codegen && gradle :smithy-csharp-codegen:clean :smithy-csharp-codegen:publishToMavenLocal :smithy-proto-codegen:clean :smithy-proto-codegen:publishToMavenLocal

# Publish the codegen JARs to Maven Central via the Sonatype Central Portal.
# Used by the release workflow; expects MAVEN_CENTRAL_USERNAME / MAVEN_CENTRAL_PASSWORD
# and ORG_GRADLE_PROJECT_signingInMemoryKey / ORG_GRADLE_PROJECT_signingInMemoryKeyPassword

# to be set in the environment.
publish-codegen VERSION:
    cd codegen && gradle -Pversion={{ VERSION }} :smithy-csharp-codegen:publishAndReleaseToMavenCentral :smithy-proto-codegen:publishAndReleaseToMavenCentral

build: codegen restore
    dotnet build NSmithy.slnx --configuration Release --no-restore --disable-build-servers ${VERSION:+-p:Version=$VERSION}

test:
    cd codegen && gradle test
    dotnet test NSmithy.slnx --configuration Release --no-build --disable-build-servers

pack:
    bash packages/NSmithy.MSBuild/tools/download-smithy-cli.sh
    dotnet pack NSmithy.slnx --configuration Release --no-build --output artifacts/packages ${VERSION:+-p:Version=$VERSION}

refresh-examples:
    find examples -type d -name obj -prune -exec rm -rf {} +
    dotnet restore examples/examples.slnx --no-cache --force
    # gRPC examples need two build passes: the first generates the .proto file via the
    # smithy build, the second picks it up via the static <Protobuf> glob and compiles
    # it with Grpc.Tools. (MSBuild evaluates Protobuf_Compile's item condition at
    # graph-build time, before dynamic items added inside target bodies are visible.)
    dotnet build examples/examples.slnx --verbosity minimal >/dev/null 2>&1 || true
    dotnet build examples/examples.slnx --verbosity minimal

ci: check-format build test pack

docs:
    cd website && npm install && npm run dev
