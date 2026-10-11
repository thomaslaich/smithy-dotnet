using System.CommandLine;
using DotnetNsmithy.Commands;

var rootCommand = new RootCommand(
    "NSmithy CLI — run a project's Smithy CLI and publish Maven JARs produced by 'dotnet pack'."
);

rootCommand.Subcommands.Add(PushCommand.Create());
rootCommand.Subcommands.Add(SmithyCommand.Create());

return await rootCommand.Parse(args).InvokeAsync();
