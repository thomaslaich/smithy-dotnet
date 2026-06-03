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
    dotnet clean examples/simple-rest-json/server/NSmithy.Examples.SimpleRestJson.Server.csproj --verbosity minimal
    dotnet clean examples/simple-rest-json/client/NSmithy.Examples.SimpleRestJson.Client.csproj --verbosity minimal
    dotnet clean examples/rest-json1/server/NSmithy.Examples.RestJson1.Server.csproj --verbosity minimal
    dotnet clean examples/rest-json1/client/NSmithy.Examples.RestJson1.Client.csproj --verbosity minimal
    dotnet clean examples/aws/client/NSmithy.Examples.Aws.Client.csproj --verbosity minimal
    dotnet clean examples/grpc/server/NSmithy.Examples.Grpc.Server.csproj --verbosity minimal
    dotnet clean examples/grpc/client/NSmithy.Examples.Grpc.Client.csproj --verbosity minimal
    dotnet clean examples/streaming/server/NSmithy.Examples.Streaming.Server.csproj --verbosity minimal
    dotnet clean examples/streaming/client/NSmithy.Examples.Streaming.Client.csproj --verbosity minimal
    dotnet clean examples/polyglot/dotnet/NSmithy.Polyglot.DotNet.Client.csproj --verbosity minimal
    rm -rf examples/simple-rest-json/contracts/obj
    rm -rf examples/simple-rest-json/server/obj
    rm -rf examples/simple-rest-json/client/obj
    rm -rf examples/rest-json1/contracts/obj
    rm -rf examples/rest-json1/server/obj
    rm -rf examples/rest-json1/client/obj
    rm -rf examples/aws/client/obj
    rm -rf examples/grpc/contracts/obj
    rm -rf examples/grpc/server/obj
    rm -rf examples/grpc/client/obj
    rm -rf examples/streaming/contracts/obj
    rm -rf examples/streaming/server/obj
    rm -rf examples/streaming/client/obj
    rm -rf examples/polyglot/dotnet/obj
    dotnet restore examples/simple-rest-json/contracts/NSmithy.Examples.SimpleRestJson.Contracts.csproj --no-cache --force
    dotnet restore examples/simple-rest-json/server/NSmithy.Examples.SimpleRestJson.Server.csproj --no-cache --force
    dotnet restore examples/simple-rest-json/client/NSmithy.Examples.SimpleRestJson.Client.csproj --no-cache --force
    dotnet restore examples/rest-json1/contracts/NSmithy.Examples.RestJson1.Contracts.csproj --no-cache --force
    dotnet restore examples/rest-json1/server/NSmithy.Examples.RestJson1.Server.csproj --no-cache --force
    dotnet restore examples/rest-json1/client/NSmithy.Examples.RestJson1.Client.csproj --no-cache --force
    dotnet restore examples/aws/client/NSmithy.Examples.Aws.Client.csproj --no-cache --force
    dotnet restore examples/grpc/contracts/Library.Contracts.csproj --no-cache --force
    dotnet restore examples/grpc/server/NSmithy.Examples.Grpc.Server.csproj --no-cache --force
    dotnet restore examples/grpc/client/NSmithy.Examples.Grpc.Client.csproj --no-cache --force
    dotnet restore examples/streaming/contracts/Metrics.Contracts.csproj --no-cache --force
    dotnet restore examples/streaming/server/NSmithy.Examples.Streaming.Server.csproj --no-cache --force
    dotnet restore examples/streaming/client/NSmithy.Examples.Streaming.Client.csproj --no-cache --force
    dotnet restore examples/polyglot/dotnet/NSmithy.Polyglot.DotNet.Client.csproj --no-cache --force
    # gRPC examples need two build passes: the first generates the .proto file via the
    # smithy build, the second picks it up via the static <Protobuf> glob and compiles
    # it with Grpc.Tools. (MSBuild evaluates Protobuf_Compile's item condition at
    # graph-build time, before dynamic items added inside target bodies are visible.)
    dotnet build examples/grpc/server/NSmithy.Examples.Grpc.Server.csproj --verbosity minimal 2>/dev/null || true
    dotnet build examples/grpc/client/NSmithy.Examples.Grpc.Client.csproj --verbosity minimal 2>/dev/null || true
    dotnet build examples/streaming/server/NSmithy.Examples.Streaming.Server.csproj --verbosity minimal 2>/dev/null || true
    dotnet build examples/streaming/client/NSmithy.Examples.Streaming.Client.csproj --verbosity minimal 2>/dev/null || true

ci: check-format build test pack

docs:
    cd website && npm install && npm run dev
