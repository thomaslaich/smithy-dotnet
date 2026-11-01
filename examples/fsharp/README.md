# F# example

A Bookshelf service served and called from F#, over `aws.protocols#restJson1`.
The model shows how Smithy shapes look in F#: structures become records, an
optional member becomes an `option`, the `Status` union and the `Genre` enum
become discriminated unions, and the `NoSuchBook` and `AlreadyLent` errors
become exceptions that the client catches with `:?`.

## Projects

- `contracts`: the Smithy model, packaged as a contracts project.
- `server`: an ASP.NET Core app that implements `IBookshelfServiceHandler` with
  an in-memory shelf.
- `client`: a console app that adds and lends books, and shows modeled errors
  and input validation.

The server and client are F# projects, so NSmithy generates F# for them. They
reference the contracts project directly and need no `smithy-build.json` of
their own.

## Prerequisites

- .NET 10 SDK
- `just`, or the repository toolchain through `devenv shell`

## Build

Run all commands in this README from `examples/fsharp`. First build the local
packages and examples:

```bash
just build
just pack
just refresh-examples
```

## Run

Start the server:

```bash
dotnet run --project server --urls http://localhost:5000
```

In another shell, run the client:

```bash
dotnet run --project client -- http://localhost:5000
```

Or call the server directly:

```bash
curl -s -X POST http://localhost:5000/books \
  -H 'content-type: application/json' \
  -d '{"title":"Dune","author":"Frank Herbert","genre":"fiction"}'
curl -s 'http://localhost:5000/books?genre=fiction'
```
