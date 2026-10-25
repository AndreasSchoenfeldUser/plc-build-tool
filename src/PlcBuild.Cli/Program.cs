using PlcBuild.Cli;
using PlcBuild.Core;

Console.OutputEncoding = Files.Utf8NoBom;
return CliApp.Run(args, RunContext.FromProcess());
