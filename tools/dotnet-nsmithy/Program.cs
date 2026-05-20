using System.CommandLine;
using DotnetNsmithy.Commands;

var rootCommand = new RootCommand(
    "NSmithy CLI — publish Maven JARs produced by 'dotnet pack' to a Maven registry."
);

rootCommand.AddCommand(PushCommand.Create());

return await rootCommand.InvokeAsync(args);
