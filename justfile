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

# `io.github.thomaslaich.smithy:csharp:0.1.0-SNAPSHOT` from ~/.m2.
codegen:
    cd codegen && gradle :csharp:publishToMavenLocal

build: codegen restore
    dotnet build NSmithy.slnx --configuration Release --no-restore --disable-build-servers

test:
    dotnet test NSmithy.slnx --configuration Release --no-build --disable-build-servers

pack:
    dotnet pack NSmithy.slnx --configuration Release --no-build --output artifacts/packages

refresh-examples:
    dotnet clean examples/simple-rest-json/server/NSmithy.Examples.SimpleRestJson.Server.csproj --verbosity minimal
    dotnet clean examples/simple-rest-json/client/NSmithy.Examples.SimpleRestJson.Client.csproj --verbosity minimal
    dotnet clean examples/aws/client/NSmithy.Examples.Aws.Client.csproj --verbosity minimal
    dotnet clean examples/grpc/server/NSmithy.Examples.Grpc.Server.csproj --verbosity minimal
    dotnet clean examples/grpc/client-rest/NSmithy.Examples.Grpc.ClientRest.csproj --verbosity minimal
    dotnet clean examples/grpc/client-grpc/NSmithy.Examples.Grpc.ClientGrpc.csproj --verbosity minimal
    dotnet clean examples/polyglot/dotnet/NSmithy.Polyglot.DotNet.Client.csproj --verbosity minimal
    rm -rf examples/simple-rest-json/server/obj
    rm -rf examples/simple-rest-json/client/obj
    rm -rf examples/aws/client/obj
    rm -rf examples/grpc/server/obj
    rm -rf examples/grpc/client-rest/obj
    rm -rf examples/grpc/client-grpc/obj
    rm -rf examples/polyglot/dotnet/obj
    dotnet restore examples/simple-rest-json/server/NSmithy.Examples.SimpleRestJson.Server.csproj --no-cache --force
    dotnet restore examples/simple-rest-json/client/NSmithy.Examples.SimpleRestJson.Client.csproj --no-cache --force
    dotnet restore examples/aws/client/NSmithy.Examples.Aws.Client.csproj --no-cache --force
    dotnet restore examples/grpc/server/NSmithy.Examples.Grpc.Server.csproj --no-cache --force
    dotnet restore examples/grpc/client-rest/NSmithy.Examples.Grpc.ClientRest.csproj --no-cache --force
    dotnet restore examples/grpc/client-grpc/NSmithy.Examples.Grpc.ClientGrpc.csproj --no-cache --force
    dotnet restore examples/polyglot/dotnet/NSmithy.Polyglot.DotNet.Client.csproj --no-cache --force

ci: check-format build test pack
