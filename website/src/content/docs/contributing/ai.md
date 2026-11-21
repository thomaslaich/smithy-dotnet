---
title: AI use
description: How AI coding agents are used to build NSmithy, and what that means for contributions.
---

## How NSmithy is built

Most of NSmithy's code is written with AI coding agents working under the
maintainer's direction. The early code was written mostly with OpenAI Codex,
later code with both Codex and Claude Code. The maintainer decides the design,
reviews every change in a pull request, and merges it.

The tests check the output, whoever wrote it:

- Smithy's protocol conformance suites run against the clients and servers, and
  [Protocol status](/smithy-dotnet/protocols/status/) publishes the results.
- Golden tests check in the generated C# and F# for small models, so every
  generator change shows up as a reviewable diff.
- The [benchmark suite](https://github.com/thomaslaich/smithy-dotnet/tree/main/benchmarks)
  checks that every server and client exchanges the same requests and responses
  as a reference stack before it compares their performance.
- CI also builds the examples, runs a native AOT smoke test, and creates
  projects from the templates.

[`AGENTS.md`](https://github.com/thomaslaich/smithy-dotnet/blob/main/AGENTS.md)
at the repository root describes the repository for coding agents.

## Contributions

AI-assisted contributions are welcome. Understand what your change does: you do
not need to know every line, but you should be able to explain the approach and
answer questions about it in review. You do not need to say whether you used AI.
