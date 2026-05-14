// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Aspire.Cli.Acquisition;

/// <summary>
/// Plans and (optionally) executes <c>aspire uninstall</c> against a given
/// install prefix.
/// </summary>
/// <remarks>
/// Planning and execution are separated so the command can preview actions
/// (<c>--dry-run</c>) and prompt the user before any filesystem mutation
/// occurs. The same plan instance feeds both the printed preview and the
/// executor; there is no second filesystem walk between them.
/// </remarks>
internal interface IInstallationUninstaller
{
    /// <summary>
    /// Inspects <paramref name="prefix"/> and returns a plan describing
    /// what would be removed.
    /// </summary>
    /// <param name="prefix">Absolute path of the install prefix.</param>
    /// <returns>A non-null <see cref="UninstallPlan"/>. The <c>Outcome</c>
    /// field distinguishes proceed plans from refusal / no-op plans.</returns>
    UninstallPlan Plan(string prefix);

    /// <summary>
    /// Executes a <see cref="UninstallOutcome.Proceed"/> plan, deleting
    /// every entry in <see cref="UninstallPlan.Removals"/> in order.
    /// Calling this on a non-proceed plan throws.
    /// </summary>
    Task ExecuteAsync(UninstallPlan plan, CancellationToken cancellationToken);
}
