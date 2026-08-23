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

# Scaffold and build every `dotnet new` template combination against the packed packages.
smoke-templates:
    # Part of `ci`, so the templates cannot rot unnoticed: their hand-written Program.cs is the
    # only code in the repo that compiles against generated APIs without being built here, so a
    # codegen rename breaks `dotnet new` while everything else stays green.
    bash templates/smoke-test.sh

ci: check-format build test pack refresh-examples smoke-templates

# The examples pin the fixed `0.0.0-SNAPSHOT` dev version permanently, while a release build packs
# release-versioned packages, so NuGet resolves a version other than the pinned one and NU1603
# (warning-as-error) fails the restore. That failure says nothing about the examples: they are a
# dev-loop check, and `ci` gates them on every pull request, where VERSION is unset and the pins
# match what `just pack` produced.

# What a release build runs: everything in `ci` except the examples refresh.
release-ci: check-format build test pack

bench-build:
    dotnet build benchmarks/Benchmarks.slnx --configuration Release

# Verify every stack serves byte-identical responses. Run before trusting numbers.
bench-parity:
    dotnet test benchmarks/Benchmarks.Parity/Bench.Parity.csproj --configuration Release

# Re-record the golden wire captures from the reference stack. Review the diff.
bench-capture:
    dotnet run --project benchmarks/Benchmarks.Capture --configuration Release

# Level A: pure codecs, no ASP.NET. Fast, deterministic, best regression signal.
bench-codec-json:
    dotnet run --project benchmarks/Benchmarks.Micro --configuration Release -- \
        --filter '*Bench.Micro.SerializationBenchmarks*' '*Bench.Micro.SerializationExecutionBenchmarks*' '*Bench.Micro.DeserializationBenchmarks*' '*ErrorBenchmarks*' \
        --inProcess \
        --artifacts benchmarks/results/codec/json

bench-codec-cbor:
    dotnet run --project benchmarks/Benchmarks.Micro --configuration Release -- \
        --filter '*CborSerializationBenchmarks*' \
        --inProcess \
        --artifacts benchmarks/results/codec/cbor

bench-codec-xml:
    dotnet run --project benchmarks/Benchmarks.Micro --configuration Release -- \
        --filter '*XmlSerializationBenchmarks*' \
        --inProcess \
        --artifacts benchmarks/results/codec/xml

bench-codec-proto:
    dotnet run --project benchmarks/Benchmarks.Micro --configuration Release -- \
        --filter '*ProtoSerializationBenchmarks*' '*GrpcSerializationBenchmarks*' '*GrpcDeserializationBenchmarks*' \
        --inProcess \
        --artifacts benchmarks/results/codec/proto

bench-codec: bench-codec-json bench-codec-cbor bench-codec-xml bench-codec-proto

# Level B, server side: full in-memory HTTP pipeline per stack per scenario.
bench-server-rest-json:
    dotnet run --project benchmarks/Benchmarks.Micro --configuration Release -- \
        --filter '*Bench.Micro.ServerBenchmarks*' \
        --inProcess \
        --artifacts benchmarks/results/server/rest-json

bench-server-grpc:
    dotnet run --project benchmarks/Benchmarks.Micro --configuration Release -- \
        --filter '*GrpcServerBenchmarks*' \
        --inProcess \
        --artifacts benchmarks/results/server/grpc

bench-server: bench-server-rest-json bench-server-grpc

# Level A, client side: request building and response parsing, no server.
bench-client-rest-json:
    dotnet run --project benchmarks/Benchmarks.Micro --configuration Release -- \
        --filter '*Bench.Micro.ClientBenchmarks*' '*ClientCeremonyBenchmarks*' \
        --inProcess \
        --artifacts benchmarks/results/client/rest-json

bench-client-grpc:
    dotnet run --project benchmarks/Benchmarks.Micro --configuration Release -- \
        --filter '*GrpcClientBenchmarks*' \
        --inProcess \
        --artifacts benchmarks/results/client/grpc

bench-client: bench-client-rest-json bench-client-grpc

# Unary gRPC client, server and Proto codec attribution against Grpc.Net.
bench-grpc: bench-codec-proto bench-client-grpc bench-server-grpc

# The whole micro suite. Slow.
bench: bench-parity bench-codec bench-client bench-server

docs:
    cd website && npm install && npm run dev
