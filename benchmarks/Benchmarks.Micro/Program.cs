using BenchmarkDotNet.Running;

// Entry point for the codec, client and server suites.
//
//   dotnet run -c Release --project benchmarks/Benchmarks.Micro -- --filter '*'
//
// Run the parity gate first. These numbers only mean something if every stack
// under comparison is known to serve the same bytes.
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

internal sealed partial class Program;
