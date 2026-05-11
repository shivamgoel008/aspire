// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using System.Globalization;
using Aspire.Cli.Configuration;
using Aspire.Cli.Interaction;
using Aspire.Cli.Resources;
using Aspire.Cli.Telemetry;
using Aspire.Cli.Utils;

namespace Aspire.Cli.Commands.Template;

/// <summary>
/// Stub implementation of <c>aspire template refresh</c>. Real implementation will arrive in a
/// later phase that wires up the aspire.dev template service and local cache.
/// </summary>
internal sealed class GitTemplateRefreshCommand : BaseCommand
{
    internal override HelpGroup HelpGroup => HelpGroup.ToolsAndConfiguration;

    public GitTemplateRefreshCommand(
        IInteractionService interactionService,
        IFeatures features,
        ICliUpdateNotifier updateNotifier,
        CliExecutionContext executionContext,
        AspireCliTelemetry telemetry)
        : base("refresh", TemplateCommandStrings.RefreshCommand_Description, features, updateNotifier, executionContext, interactionService, telemetry)
    {
    }

    protected override bool UpdateNotificationsEnabled => false;

    protected override Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        InteractionService.DisplayMessage(KnownEmojis.Information, string.Format(CultureInfo.CurrentCulture, TemplateCommandStrings.NotYetImplemented, "refresh"));
        return Task.FromResult(ExitCodeConstants.Success);
    }
}
