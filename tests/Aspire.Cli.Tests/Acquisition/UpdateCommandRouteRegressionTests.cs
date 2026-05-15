// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Aspire.Cli.Acquisition;
using Aspire.Cli.Commands;
using Aspire.Cli.Tests.TestServices;
using Aspire.Cli.Tests.Utils;
using Microsoft.AspNetCore.InternalTesting;
using Microsoft.Extensions.DependencyInjection;

namespace Aspire.Cli.Tests.Acquisition;

/// <summary>
/// End-to-end regression guard for the silent-PR-demotion and
/// package-manager binary-clobber bugs on <c>aspire update --self</c>.
/// Pre-fix: an in-process binary swap ran unconditionally for every
/// non-dotnet-tool route, overwriting WinGet / Homebrew / PR-pinned
/// binaries with the latest stable archive. Post-fix: each non-script
/// route gets refused with the installer-appropriate command and the
/// binary is left untouched.
/// </summary>
public class UpdateCommandRouteRegressionTests(ITestOutputHelper outputHelper)
{
    // Each row encodes (sidecar source, identityChannel for PR substitution,
    // expected refusal command). Script and Unknown stay in-process by design,
    // so they're excluded from this regression net.
    [Theory]
    [InlineData("pr", "pr-16817", "get-aspire-cli-pr.sh 16817    # or: get-aspire-cli-pr.ps1 -PRNumber 16817")]
    [InlineData("winget", "stable", "winget upgrade Microsoft.Aspire")]
    [InlineData("brew", "stable", "brew upgrade --cask aspire")]
    [InlineData("localhive", "local", "./localhive.sh   # re-run from your Aspire checkout")]
    public async Task SelfUpdate_OnGatedRoute_RefusesWithRouteAppropriateCommand(
        string sidecarSource,
        string identityChannel,
        string expectedCommand)
    {
        using var workspace = TemporaryWorkspace.Create(outputHelper);

        var selfInfo = new InstallationInfo
        {
            Path = "/test/aspire",
            CanonicalPath = "/test/aspire",
            Route = sidecarSource,
            Channel = identityChannel,
            Status = InstallationInfoStatus.Ok,
        };

        TestInteractionService? interactionService = null;
        var services = CliTestHelper.CreateServiceCollection(workspace, outputHelper, options =>
        {
            options.InteractionServiceFactory = _ =>
            {
                interactionService = new TestInteractionService();
                return interactionService;
            };

            // Force the running CLI's identity channel so the PR-route
            // substitution exercises the parsed PR number path.
            options.CliExecutionContextFactory = _ =>
            {
                var root = workspace.WorkspaceRoot;
                var hivesDirectory = new DirectoryInfo(Path.Combine(root.FullName, ".aspire", "hives"));
                var cacheDirectory = new DirectoryInfo(Path.Combine(root.FullName, ".aspire", "cache"));
                var logsDirectory = new DirectoryInfo(Path.Combine(root.FullName, ".aspire", "logs"));
                var logFilePath = Path.Combine(logsDirectory.FullName, "test.log");
                return new CliExecutionContext(
                    root,
                    hivesDirectory,
                    cacheDirectory,
                    new DirectoryInfo(Path.Combine(Path.GetTempPath(), "aspire-test-sdks")),
                    logsDirectory,
                    logFilePath,
                    identityChannel: identityChannel);
            };
        });

        // Replace the real InstallationDiscovery with a fake surfacing the
        // route under test. Last registration wins.
        services.AddSingleton<IInstallationDiscovery>(_ => new FakeInstallationDiscovery(selfInfo));

        using var provider = services.BuildServiceProvider();
        var command = provider.GetRequiredService<RootCommand>();
        var parsed = command.Parse("update --self");
        var exitCode = await parsed.InvokeAsync().DefaultTimeout();

        Assert.NotNull(interactionService);
        // Exit 0 by design — the CLI succeeded in telling the user what to
        // do (matches the existing dotnet-tool refusal contract).
        Assert.Equal(0, exitCode);

        // The expected command must appear verbatim in the displayed plain
        // text — this is the signal a user / CI script would actually
        // observe in stdout.
        Assert.Contains(
            interactionService!.DisplayedPlainText,
            line => line.Contains(expectedCommand, StringComparison.Ordinal));
    }
}
