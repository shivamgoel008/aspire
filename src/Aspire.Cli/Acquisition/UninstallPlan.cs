// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Aspire.Cli.Acquisition;

/// <summary>
/// The actions <c>aspire uninstall</c> will (or, in dry-run mode, would)
/// perform against a particular install prefix.
/// </summary>
/// <param name="Prefix">
/// The install prefix the plan operates on. For PR-route this is
/// <c>~/.aspire/dogfood/pr-&lt;N&gt;/</c>; for script-route this is e.g.
/// <c>~/.aspire/</c>.
/// </param>
/// <param name="Route">
/// Install route detected from the sidecar at <c>&lt;Prefix&gt;/bin/.aspire-install.json</c>.
/// </param>
/// <param name="Outcome">
/// What the planner decided about this prefix.
/// </param>
/// <param name="Removals">
/// Absolute filesystem paths (files and directories) that
/// <see cref="UninstallOutcome.Proceed"/> plans will delete in order. Empty
/// for refusal outcomes. Directories are deleted recursively.
/// </param>
/// <param name="ManualSteps">
/// Human-readable instructions to print after execution (or in dry-run
/// output) describing cleanup that <c>aspire uninstall</c> intentionally
/// does not perform — typically shell-profile PATH entries and packager-
/// managed installs that the caller must remove themselves.
/// </param>
/// <param name="RefusalReason">
/// Populated when <see cref="Outcome"/> is a refusal variant; explains why.
/// Null for proceed plans.
/// </param>
internal sealed record UninstallPlan(
    string Prefix,
    InstallSource Route,
    UninstallOutcome Outcome,
    IReadOnlyList<string> Removals,
    IReadOnlyList<string> ManualSteps,
    string? RefusalReason);

/// <summary>
/// Outcome of planning an uninstall against a given prefix.
/// </summary>
internal enum UninstallOutcome
{
    /// <summary>Planner produced a non-empty set of removals; safe to execute.</summary>
    Proceed,

    /// <summary>Target prefix does not exist or has no install metadata.</summary>
    NothingToDo,

    /// <summary>
    /// Target prefix contains the running CLI's binary. We refuse to delete
    /// ourselves; print manual <c>rm -rf</c> commands instead.
    /// </summary>
    RefusedSelf,

    /// <summary>
    /// The install was placed by a packager (winget / brew / dotnet-tool).
    /// <c>aspire uninstall</c> does not invoke the packager; the user must
    /// use the packager's own uninstall command.
    /// </summary>
    RefusedPackagerOwned,

    /// <summary>
    /// Could not parse the sidecar, or the sidecar identifies a route this
    /// build does not understand. Refuses to delete by default so a future
    /// route doesn't get accidentally trampled.
    /// </summary>
    RefusedUnknownRoute,
}
