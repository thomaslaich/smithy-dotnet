set shell := ["bash", "-eu", "-o", "pipefail", "-c"]

default:
    just --list

restore:
    dotnet restore Smithy.NET.slnx

fmt:
    treefmt

check-format:
    treefmt --ci

build:
    dotnet build Smithy.NET.slnx --configuration Release --no-restore --disable-build-servers

test:
    dotnet test Smithy.NET.slnx --configuration Release --no-build --disable-build-servers --verbosity minimal

pack:
    dotnet pack Smithy.NET.slnx --configuration Release --no-build --output artifacts/packages --verbosity minimal

ci: restore check-format build test pack
