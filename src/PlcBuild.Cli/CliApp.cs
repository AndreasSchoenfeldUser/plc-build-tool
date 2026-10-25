using System.CommandLine;
using PlcBuild.Core;
using PlcBuild.Protocol;

namespace PlcBuild.Cli;

/// <summary>Command line of CLAUDE.md section 4. Parsing only; the work happens in <see cref="Commands"/>.</summary>
public static class CliApp
{
    public static int Run(string[] args, RunContext ctx)
    {
        var root = BuildRootCommand(ctx);
        var parse = root.Parse(args);
        if (parse.Errors.Count > 0)
        {
            foreach (var error in parse.Errors)
            {
                ctx.Error.WriteLine($"[USAGE] {error.Message}");
            }
            ctx.Error.WriteLine("Hilfe: plc-build --help");
            return ExitCodes.Usage;
        }
        try
        {
            return parse.Invoke(new InvocationConfiguration { Output = ctx.Out, Error = ctx.Error, EnableDefaultExceptionHandler = false });
        }
        catch (Exception ex)
        {
            // A bug in plc-build is not a finding in the project.
            ctx.Error.WriteLine($"[INTERNAL_ERROR] Unerwarteter Fehler in plc-build: {ex}");
            return ExitCodes.Aborted;
        }
    }

    internal static RootCommand BuildRootCommand(RunContext ctx)
    {
        var config = new Option<string?>("--config") { Description = "Pfad zu plc.yml (Vorgabe: plc.yml im aktuellen Verzeichnis)" };
        var outDir = new Option<string>("--out") { Description = "Ausgabeverzeichnis (Vorgabe: out/)", DefaultValueFactory = _ => "out" };
        var timeout = new Option<double>("--timeout") { Description = "Zeitlimit je Adapter-Aufruf in Minuten (Vorgabe: 30)", DefaultValueFactory = _ => 30 };
        timeout.Validators.Add(r =>
        {
            if (r.GetValueOrDefault<double>() <= 0)
            {
                r.AddError("--timeout muss größer als 0 sein.");
            }
        });
        var verbose = new Option<bool>("--verbose") { Description = "Ausgabe des Adapters mitschreiben" };

        Option<string> ProjectOption(bool required) =>
            new("--project") { Description = "Projekt-ID aus plc.yml", Required = required };

        CommonOptions Common(ParseResult r, Option<string>? outOption = null) => new()
        {
            ConfigPath = r.GetValue(config),
            OutDir = r.GetValue(outOption ?? outDir)!,
            Timeout = TimeSpan.FromMinutes(r.GetValue(timeout)),
            Verbose = r.GetValue(verbose),
        };

        Command WithCommon(Command command, params Option[] extra)
        {
            foreach (var option in extra) command.Options.Add(option);
            command.Options.Add(config);
            command.Options.Add(outDir);
            command.Options.Add(timeout);
            command.Options.Add(verbose);
            return command;
        }

        var root = new RootCommand("plc-build: SPS-Projekte im CI übersetzen, prüfen und testen. Exit-Codes: 0 ok, 1 Befund, 2 Aufruf/Konfiguration, 3 Umgebung, 4 Abbruch.");

        // doctor
        {
            var project = ProjectOption(required: false);
            var cmd = WithCommon(new Command("doctor", "Umgebung prüfen, ohne Projekt zu öffnen"), project);
            cmd.SetAction(r => Commands.Doctor(ctx, Common(r), r.GetValue(project)));
            root.Subcommands.Add(cmd);
        }

        // single steps
        foreach (var (step, description) in new[]
        {
            (StepNames.Restore, "Basisprojekt aus dem Archiv in ein Arbeitsverzeichnis"),
            (StepNames.Import, "Textquellen aus Git ins Projekt"),
            (StepNames.Compile, "Übersetzen"),
            (StepNames.Analyze, "Statische Prüfung mit Regelwerk aus dem Repository"),
            (StepNames.Test, "Tests auf Simulation oder Usermode-Laufzeit"),
        })
        {
            var project = ProjectOption(required: true);
            var cmd = WithCommon(new Command(step, description), project);
            cmd.SetAction(r => Commands.Step(ctx, Common(r), step, r.GetValue(project)!));
            root.Subcommands.Add(cmd);
        }

        // archive: --out is the archive file here, the output directory moves to --out-dir (ADR 0004)
        {
            var project = ProjectOption(required: true);
            var file = new Option<string>("--out") { Description = "Archivdatei, die erzeugt wird", Required = true };
            var archiveOutDir = new Option<string>("--out-dir") { Description = "Ausgabeverzeichnis (Vorgabe: out/)", DefaultValueFactory = _ => "out" };
            var cmd = new Command("archive", "Projekt archivieren");
            foreach (var option in new Option[] { project, file, config, archiveOutDir, timeout, verbose })
            {
                cmd.Options.Add(option);
            }
            cmd.SetAction(r => Commands.Archive(ctx, Common(r, archiveOutDir), r.GetValue(project)!, r.GetValue(file)!));
            root.Subcommands.Add(cmd);
        }

        // report
        {
            var project = ProjectOption(required: true);
            var junit = new Option<string>("--junit") { Description = "Verzeichnis für JUnit-XML", Required = true };
            var sarif = new Option<string?>("--sarif") { Description = "Verzeichnis für SARIF" };
            var cmd = WithCommon(new Command("report", "JUnit, SARIF und Build-Manifest aus den Ergebnissen schreiben"), project, junit, sarif);
            cmd.SetAction(r => Commands.Report(ctx, Common(r), r.GetValue(project)!, r.GetValue(junit)!, r.GetValue(sarif)));
            root.Subcommands.Add(cmd);
        }

        // cleanup
        {
            var project = ProjectOption(required: false);
            var cmd = WithCommon(new Command("cleanup", "Nur selbst gestartete Prozesse beenden"), project);
            cmd.SetAction(r => Commands.Cleanup(ctx, Common(r), r.GetValue(project)));
            root.Subcommands.Add(cmd);
        }

        // run
        {
            var project = ProjectOption(required: true);
            var steps = new Option<string>("--steps")
            {
                Description = "Schritte in einer Adapter-Sitzung, z. B. restore,import,compile,analyze,test",
                Required = true,
            };
            var cmd = WithCommon(new Command("run", "Mehrere Schritte in einer Adapter-Sitzung"), project, steps);
            cmd.SetAction(r => Commands.Run(ctx, Common(r), r.GetValue(project)!,
                r.GetValue(steps)!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));
            root.Subcommands.Add(cmd);
        }

        return root;
    }
}
