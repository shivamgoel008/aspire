// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using System.CommandLine.Help;
using Aspire.Cli.Configuration;
using Aspire.Cli.Interaction;
using Aspire.Cli.Resources;
using Aspire.Cli.Telemetry;
using Aspire.Cli.Utils;

namespace Aspire.Cli.Commands.Template;

/// <summary>
/// Parent command for the <c>aspire template</c> command group.
/// </summary>
/// <remarks>
/// The class name is <c>GitTemplateCommand</c> rather than <c>TemplateCommand</c> to avoid a
/// type-name collision with <see cref="Aspire.Cli.Commands.TemplateCommand"/>, which is the
/// per-template subcommand wrapper used by <c>aspire new</c>. The user-facing CLI verb remains
/// <c>aspire template</c>.
/// </remarks>
internal sealed class GitTemplateCommand : BaseCommand
{
    internal override HelpGroup HelpGroup => HelpGroup.ToolsAndConfiguration;

    public GitTemplateCommand(
        IInteractionService interactionService,
        IFeatures features,
        ICliUpdateNotifier updateNotifier,
        CliExecutionContext executionContext,
        GitTemplateListCommand listCommand,
        GitTemplateSearchCommand searchCommand,
        GitTemplateRefreshCommand refreshCommand,
        GitTemplateNewCommand newCommand,
        AspireCliTelemetry telemetry)
        : base("template", TemplateCommandStrings.Description, features, updateNotifier, executionContext, interactionService, telemetry)
    {
        Subcommands.Add(listCommand);
        Subcommands.Add(searchCommand);
        Subcommands.Add(refreshCommand);
        Subcommands.Add(newCommand);
    }

    protected override bool UpdateNotificationsEnabled => false;

    protected override Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        new HelpAction().Invoke(parseResult);
        return Task.FromResult(ExitCodeConstants.InvalidCommand);
    }
}
