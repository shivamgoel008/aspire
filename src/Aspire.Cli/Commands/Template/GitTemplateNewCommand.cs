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
/// Stub implementation of <c>aspire template new [path]</c>. Real implementation will arrive in
/// a later phase that scaffolds a working <c>aspire-template.json</c> manifest interactively.
/// </summary>
internal sealed class GitTemplateNewCommand : BaseCommand
{
    internal override HelpGroup HelpGroup => HelpGroup.ToolsAndConfiguration;

    private static readonly Argument<string?> s_pathArgument = new("path")
    {
        Description = TemplateCommandStrings.NewCommand_PathArgumentDescription,
        Arity = ArgumentArity.ZeroOrOne
    };

    public GitTemplateNewCommand(
        IInteractionService interactionService,
        IFeatures features,
        ICliUpdateNotifier updateNotifier,
        CliExecutionContext executionContext,
        AspireCliTelemetry telemetry)
        : base("new", TemplateCommandStrings.NewCommand_Description, features, updateNotifier, executionContext, interactionService, telemetry)
    {
        Arguments.Add(s_pathArgument);
    }

    protected override bool UpdateNotificationsEnabled => false;

    protected override Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        InteractionService.DisplayMessage(KnownEmojis.Information, string.Format(CultureInfo.CurrentCulture, TemplateCommandStrings.NotYetImplemented, "new"));
        return Task.FromResult(ExitCodeConstants.Success);
    }
}
