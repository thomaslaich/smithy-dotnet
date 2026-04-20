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
    dotnet test SmithyNet.slnx --configuration Release --no-build --disable-build-servers --verbosity minimal

pack:
    dotnet pack SmithyNet.slnx --configuration Release --no-build --output artifacts/packages --verbosity minimal

refresh-examples:
    dotnet clean examples/simple-rest-json/dotnet/server/SmithyNet.Examples.SimpleRestJson.Server.csproj --verbosity minimal
    dotnet clean examples/simple-rest-json/dotnet/client/SmithyNet.Examples.SimpleRestJson.Client.csproj --verbosity minimal
    rm -rf examples/simple-rest-json/dotnet/server/obj/packages/smithynet.*
    rm -rf examples/simple-rest-json/dotnet/client/obj/packages/smithynet.*
    dotnet restore examples/simple-rest-json/dotnet/server/SmithyNet.Examples.SimpleRestJson.Server.csproj --no-cache --force
    dotnet restore examples/simple-rest-json/dotnet/client/SmithyNet.Examples.SimpleRestJson.Client.csproj --no-cache --force

ci: restore check-format build test pack
