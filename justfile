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

ci: restore check-format build test pack
