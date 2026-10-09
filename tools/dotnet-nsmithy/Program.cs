using System.CommandLine;
using DotnetNsmithy.Commands;

var rootCommand = new RootCommand(
    "NSmithy CLI — install build dependencies and publish Maven artifacts."
);

rootCommand.Subcommands.Add(PushCommand.Create());
rootCommand.Subcommands.Add(InstallCommand.Create());

return await rootCommand.Parse(args).InvokeAsync();
