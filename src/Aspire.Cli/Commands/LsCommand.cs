// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using System.Text.Json;
using Aspire.Cli.Configuration;
using Aspire.Cli.Interaction;
using Aspire.Cli.Projects;
using Aspire.Cli.Resources;
using Aspire.Cli.Telemetry;
using Aspire.Cli.Utils;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Aspire.Cli.Commands;

internal sealed class LsCommand : BaseCommand
{
    internal override HelpGroup HelpGroup => HelpGroup.AppCommands;

    private readonly IInteractionService _interactionService;
    private readonly IProjectLocator _projectLocator;
    private readonly CliExecutionContext _executionContext;
    private readonly ICliHostEnvironment _hostEnvironment;
    private readonly ConsoleEnvironment _consoleEnvironment;
    private readonly ProfilingTelemetry _profilingTelemetry;

    // `--format json --stream` emits newline-delimited JSON (NDJSON), one object per line:
    //   {"type":"started"}
    //   {"type":"candidate","candidate":{"path":"C:\\repo\\AppHost.csproj","language":"C#","status":"buildable"}}
    //   {"type":"complete","appHostCount":1}
    //   {"type":"canceled"}
    // These constants are the stable wire-format values for the `type` property.
    // See https://github.com/ndjson/ndjson-spec for the line-delimited JSON convention.
    // Keep docs/specs/cli-output-formats.md in sync when changing this shape.
    private const string JsonStreamStartedEventType = "started";
    private const string JsonStreamCandidateEventType = "candidate";
    private const string JsonStreamCompleteEventType = "complete";
    private const string JsonStreamCanceledEventType = "canceled";

    private static readonly Option<OutputFormat> s_formatOption = new("--format")
    {
        Description = SharedCommandStrings.LsFormatOptionDescription
    };

    private static readonly Option<bool> s_allOption = new("--all")
    {
        Description = SharedCommandStrings.LsAllOptionDescription
    };

    private static readonly Option<bool> s_streamOption = new("--stream")
    {
        Description = SharedCommandStrings.LsStreamOptionDescription
    };

    public LsCommand(
        IInteractionService interactionService,
        IProjectLocator projectLocator,
        IFeatures features,
        ICliUpdateNotifier updateNotifier,
        CliExecutionContext executionContext,
        ICliHostEnvironment hostEnvironment,
        ConsoleEnvironment consoleEnvironment,
        AspireCliTelemetry telemetry,
        ProfilingTelemetry profilingTelemetry)
        : base("ls", SharedCommandStrings.LsCommandDescription, features, updateNotifier, executionContext, interactionService, telemetry)
    {
        _interactionService = interactionService;
        _projectLocator = projectLocator;
        _executionContext = executionContext;
        _hostEnvironment = hostEnvironment;
        _consoleEnvironment = consoleEnvironment;
        _profilingTelemetry = profilingTelemetry;

        Options.Add(s_formatOption);
        Options.Add(s_allOption);
        Options.Add(s_streamOption);

        Validators.Add(result =>
        {
            if (result.GetValue(s_streamOption) && result.GetValue(s_formatOption) != OutputFormat.Json)
            {
                result.AddError(SharedCommandStrings.LsStreamRequiresJson);
            }
        });
    }

    protected override async Task<CommandResult> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartDiagnosticActivity(Name);

        var format = parseResult.GetValue(s_formatOption);
        var includeAll = parseResult.GetValue(s_allOption);
        var stream = parseResult.GetValue(s_streamOption);
        using var profilingActivity = _profilingTelemetry.StartLsCommand(format.ToString().ToLowerInvariant(), includeAll);

        // `aspire ls` is ambient discovery from the working directory by default, so
        // it should respect git/default filters. `--all` is the explicit escape hatch
        // for users who intentionally want ignored or generated paths included.
        var scope = includeAll
            ? AppHostDiscoveryScope.AllFiles
            : AppHostDiscoveryScope.DefaultFiltered;

        try
        {
            var useJsonStream = format == OutputFormat.Json && stream;

            // Live rendering is only for human interactive table output. JSON without --stream is consumed by
            // tools/logs as one stable payload, and JSON with --stream writes machine-readable event lines.
            // Non-interactive hosts may not support terminal cursor rewrites, so they also wait for the final table.
            var useLiveOutput = format == OutputFormat.Table
                && _hostEnvironment.SupportsInteractiveOutput
                && !_executionContext.DebugMode;

            List<AppHostProjectCandidate> appHosts;
            using (var findAppHostsActivity = _profilingTelemetry.StartLsFindAppHosts(scope.ToString()))
            {
                appHosts = (useLiveOutput, useJsonStream) switch
                {
                    (true, _) => await FindAppHostsWithLiveUpdatesAsync(scope, cancellationToken).ConfigureAwait(false),
                    (_, true) => await FindAppHostsWithJsonStreamAsync(scope, cancellationToken).ConfigureAwait(false),
                    _ => await _projectLocator.FindAppHostProjectsAsync(_executionContext.WorkingDirectory, scope, cancellationToken).ConfigureAwait(false)
                };
                findAppHostsActivity.SetAppHostCandidateCount(appHosts.Count);
            }
            profilingActivity.SetAppHostCandidateCount(appHosts.Count);

            var appHostInfos = CreateDisplayInfos(appHosts);

            if (format == OutputFormat.Json && !useJsonStream)
            {
                var json = JsonSerializer.Serialize(appHostInfos, JsonSourceGenerationContext.RelaxedEscaping.ListCandidateAppHostDisplayInfo);
                _interactionService.DisplayRawText(json, ConsoleOutput.Standard);
            }
            else if (!useLiveOutput && !useJsonStream)
            {
                if (appHostInfos.Count == 0)
                {
                    _interactionService.DisplayMessage(KnownEmojis.Information, SharedCommandStrings.LsNoCandidateAppHostsFound);
                }
                else
                {
                    DisplayTable(appHostInfos);
                }
            }

            return CommandResult.Success();
        }
        catch (OperationCanceledException ex) when (ex.CancellationToken == cancellationToken || cancellationToken.IsCancellationRequested)
        {
            if (format == OutputFormat.Json && stream)
            {
                WriteJsonStreamEvent(new LsJsonStreamEvent { Type = JsonStreamCanceledEventType }, new object());
            }
            else
            {
                _interactionService.DisplayCancellationMessage();
            }

            return CommandResult.Success();
        }
    }

    private async Task<List<AppHostProjectCandidate>> FindAppHostsWithJsonStreamAsync(AppHostDiscoveryScope scope, CancellationToken cancellationToken)
    {
        var streamLock = new object();
        var appHosts = new List<AppHostProjectCandidate>();
        WriteJsonStreamEvent(new LsJsonStreamEvent { Type = JsonStreamStartedEventType }, streamLock);

        await foreach (var candidate in _projectLocator.FindAppHostProjectsStreamAsync(_executionContext.WorkingDirectory, scope, cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            appHosts.Add(candidate);
            WriteJsonStreamEvent(new LsJsonStreamEvent
            {
                Type = JsonStreamCandidateEventType,
                Candidate = CreateDisplayInfo(candidate)
            }, streamLock);
        }

        appHosts.Sort((x, y) => x.AppHostFile.FullName.CompareTo(y.AppHostFile.FullName));

        WriteJsonStreamEvent(new LsJsonStreamEvent
        {
            Type = JsonStreamCompleteEventType,
            AppHostCount = appHosts.Count
        }, streamLock);

        return appHosts;
    }

    private void WriteJsonStreamEvent(LsJsonStreamEvent streamEvent, object streamLock)
    {
        var json = JsonSerializer.Serialize(streamEvent, JsonSourceGenerationContext.Streaming.LsJsonStreamEvent);

        // Flush immediately so tools can render each discovery event without waiting for the
        // final array or the process exit.
        lock (streamLock)
        {
            var writer = _consoleEnvironment.Out.Profile.Out.Writer;
            writer.WriteLine(json);
            writer.Flush();
        }
    }

    private async Task<List<AppHostProjectCandidate>> FindAppHostsWithLiveUpdatesAsync(AppHostDiscoveryScope scope, CancellationToken cancellationToken)
    {
        var displayLock = new object();
        var liveAppHostInfos = new List<CandidateAppHostDisplayInfo>();
        var appHosts = new List<AppHostProjectCandidate>();

        await _interactionService.DisplayLiveAsync(BuildLiveSearchRenderable(liveAppHostInfos, isSearching: true), async updateTarget =>
        {
            await foreach (var candidate in _projectLocator.FindAppHostProjectsStreamAsync(_executionContext.WorkingDirectory, scope, cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                appHosts.Add(candidate);

                // Candidate validation runs in parallel. Keep the live render state locked while
                // updating it so Spectre.Console never renders a list that another worker is mutating.
                lock (displayLock)
                {
                    liveAppHostInfos.Add(CreateDisplayInfo(candidate));
                    liveAppHostInfos.Sort(CompareByPath);
                    updateTarget(BuildLiveSearchRenderable(liveAppHostInfos, isSearching: true));
                }
            }

            appHosts.Sort((x, y) => x.AppHostFile.FullName.CompareTo(y.AppHostFile.FullName));

            // The final frame should be sorted by path, not by the order stream items arrived
            // from parallel validation workers.
            lock (displayLock)
            {
                liveAppHostInfos.Clear();
                liveAppHostInfos.AddRange(CreateDisplayInfos(appHosts));
                updateTarget(BuildLiveSearchRenderable(liveAppHostInfos, isSearching: false));
            }
        }).ConfigureAwait(false);

        return appHosts;
    }

    private List<CandidateAppHostDisplayInfo> CreateDisplayInfos(IEnumerable<AppHostProjectCandidate> appHosts)
    {
        return appHosts.Select(CreateDisplayInfo).ToList();
    }

    private CandidateAppHostDisplayInfo CreateDisplayInfo(AppHostProjectCandidate appHost)
    {
        return new CandidateAppHostDisplayInfo
        {
            Path = appHost.AppHostFile.FullName,
            Language = appHost.Language,
            Status = GetDisplayStatus(appHost.Status)
        };
    }

    private static int CompareByPath(CandidateAppHostDisplayInfo x, CandidateAppHostDisplayInfo y)
    {
        return x.Path.CompareTo(y.Path);
    }

    private static IRenderable BuildLiveSearchRenderable(List<CandidateAppHostDisplayInfo> appHosts, bool isSearching)
    {
        if (appHosts.Count == 0)
        {
            return isSearching
                ? new Markup($"[grey]{InteractionServiceStrings.FindingAppHosts.EscapeMarkup()}[/]")
                : new Markup(SharedCommandStrings.LsNoCandidateAppHostsFound.EscapeMarkup());
        }

        var table = BuildTable(appHosts);

        return isSearching
            ? new Rows(new Markup($"[grey]{InteractionServiceStrings.FindingAppHosts.EscapeMarkup()}[/]"), table)
            : table;
    }

    private void DisplayTable(List<CandidateAppHostDisplayInfo> appHosts)
    {
        _interactionService.DisplayRenderable(BuildTable(appHosts));
    }

    private static Table BuildTable(List<CandidateAppHostDisplayInfo> appHosts)
    {
        var table = new Table();
        table.AddBoldColumn(SharedCommandStrings.HeaderPath);
        table.AddBoldColumn(SharedCommandStrings.HeaderLanguage);
        table.AddBoldColumn(SharedCommandStrings.HeaderStatus);

        foreach (var appHost in appHosts)
        {
            table.AddRow(
                Markup.Escape(appHost.Path),
                Markup.Escape(appHost.Language),
                GetStatusMarkup(appHost.Status));
        }

        return table;
    }

    private static string GetDisplayStatus(AppHostProjectCandidateStatus status)
    {
        return status switch
        {
            AppHostProjectCandidateStatus.Buildable => "buildable",
            AppHostProjectCandidateStatus.PossiblyUnbuildable => "possibly-unbuildable",
            _ => status.ToString().ToLowerInvariant()
        };
    }

    private static string GetStatusMarkup(string status)
    {
        return status switch
        {
            "buildable" => "[green]buildable[/]",
            "possibly-unbuildable" => "[yellow]possibly-unbuildable[/]",
            _ => Markup.Escape(status)
        };
    }
}

// `aspire ls --format json` uses this shape; keep docs/specs/cli-output-formats.md in sync when changing it.
internal sealed class CandidateAppHostDisplayInfo
{
    public required string Path { get; init; }

    public required string Language { get; init; }

    public required string Status { get; init; }
}

// `aspire ls --format json --stream` uses this shape; keep docs/specs/cli-output-formats.md in sync when changing it.
internal sealed class LsJsonStreamEvent
{
    public required string Type { get; init; }

    public CandidateAppHostDisplayInfo? Candidate { get; init; }

    public int? AppHostCount { get; init; }
}
