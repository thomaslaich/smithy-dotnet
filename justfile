set shell := ["bash", "-eu", "-o", "pipefail", "-c"]

default:
    just --list

restore:
    dotnet restore SmithyNet.slnx

fmt:
    treefmt

check-format:
    treefmt --ci

build:
    dotnet build SmithyNet.slnx --configuration Release --no-restore --disable-build-servers

test:
    dotnet test SmithyNet.slnx --configuration Release --no-build --disable-build-servers

pack:
    dotnet pack SmithyNet.slnx --configuration Release --no-build --output artifacts/packages

refresh-examples:
    dotnet clean examples/simple-rest-json/server/SmithyNet.Examples.SimpleRestJson.Server.csproj --verbosity minimal
    dotnet clean examples/simple-rest-json/client/SmithyNet.Examples.SimpleRestJson.Client.csproj --verbosity minimal
    dotnet clean examples/aws/client/SmithyNet.Examples.Aws.Client.csproj --verbosity minimal
    dotnet clean examples/grpc/server/SmithyNet.Examples.Grpc.Server.csproj --verbosity minimal
    dotnet clean examples/grpc/client-rest/SmithyNet.Examples.Grpc.ClientRest.csproj --verbosity minimal
    dotnet clean examples/grpc/client-grpc/SmithyNet.Examples.Grpc.ClientGrpc.csproj --verbosity minimal
    dotnet clean examples/polyglot/dotnet/SmithyNet.Polyglot.DotNet.Client.csproj --verbosity minimal
    rm -rf examples/simple-rest-json/server/obj
    rm -rf examples/simple-rest-json/client/obj
    rm -rf examples/aws/client/obj
    rm -rf examples/grpc/server/obj
    rm -rf examples/grpc/client-rest/obj
    rm -rf examples/grpc/client-grpc/obj
    rm -rf examples/polyglot/dotnet/obj
    dotnet restore examples/simple-rest-json/server/SmithyNet.Examples.SimpleRestJson.Server.csproj --no-cache --force
    dotnet restore examples/simple-rest-json/client/SmithyNet.Examples.SimpleRestJson.Client.csproj --no-cache --force
    dotnet restore examples/aws/client/SmithyNet.Examples.Aws.Client.csproj --no-cache --force
    dotnet restore examples/grpc/server/SmithyNet.Examples.Grpc.Server.csproj --no-cache --force
    dotnet restore examples/grpc/client-rest/SmithyNet.Examples.Grpc.ClientRest.csproj --no-cache --force
    dotnet restore examples/grpc/client-grpc/SmithyNet.Examples.Grpc.ClientGrpc.csproj --no-cache --force
    dotnet restore examples/polyglot/dotnet/SmithyNet.Polyglot.DotNet.Client.csproj --no-cache --force

ci: restore check-format build test pack
