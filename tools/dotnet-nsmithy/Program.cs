using System.CommandLine;
using DotnetNsmithy.Commands;

var rootCommand = new RootCommand(
    "NSmithy CLI — pack Smithy contracts into Maven JARs and publish them to Maven registries."
);

rootCommand.AddCommand(PackCommand.Create());
rootCommand.AddCommand(PushCommand.Create());

return await rootCommand.InvokeAsync(args);
