// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Aspire.Cli.Commands;
using Aspire.Cli.Tests.Utils;
using Microsoft.AspNetCore.InternalTesting;
using Microsoft.Extensions.DependencyInjection;

namespace Aspire.Cli.Tests.Commands.Template;

public class GitTemplateCommandsTests(ITestOutputHelper outputHelper)
{
    [Fact]
    public async Task GitTemplateCommand_WhenFeatureFlagDisabled_NotRegisteredOnRootCommand()
    {
        using var workspace = TemporaryWorkspace.Create(outputHelper);
        var services = CliTestHelper.CreateServiceCollection(workspace, outputHelper);
        using var provider = services.BuildServiceProvider();

        var rootCommand = provider.GetRequiredService<RootCommand>();
        var templateCommand = rootCommand.Subcommands.FirstOrDefault(c => c.Name == "template");

        Assert.Null(templateCommand);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task GitTemplateCommand_WhenFeatureFlagEnabled_RegisteredOnRootCommand()
    {
        using var workspace = TemporaryWorkspace.Create(outputHelper);
        var services = CliTestHelper.CreateServiceCollection(workspace, outputHelper, options =>
        {
            options.EnabledFeatures = [KnownFeatures.GitTemplatesEnabled];
        });
        using var provider = services.BuildServiceProvider();

        var rootCommand = provider.GetRequiredService<RootCommand>();
        var templateCommand = rootCommand.Subcommands.FirstOrDefault(c => c.Name == "template");

        Assert.NotNull(templateCommand);
        Assert.Equal("template", templateCommand.Name);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task GitTemplateCommand_WhenFeatureFlagEnabled_HasExpectedSubcommands()
    {
        using var workspace = TemporaryWorkspace.Create(outputHelper);
        var services = CliTestHelper.CreateServiceCollection(workspace, outputHelper, options =>
        {
            options.EnabledFeatures = [KnownFeatures.GitTemplatesEnabled];
        });
        using var provider = services.BuildServiceProvider();

        var rootCommand = provider.GetRequiredService<RootCommand>();
        var templateCommand = rootCommand.Subcommands.FirstOrDefault(c => c.Name == "template");

        Assert.NotNull(templateCommand);
        var subNames = templateCommand.Subcommands.Select(c => c.Name).ToHashSet();
        Assert.Contains("list", subNames);
        Assert.Contains("search", subNames);
        Assert.Contains("refresh", subNames);
        Assert.Contains("new", subNames);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task GitTemplateCommand_WithoutSubcommand_ReturnsInvalidCommand()
    {
        using var workspace = TemporaryWorkspace.Create(outputHelper);
        var services = CliTestHelper.CreateServiceCollection(workspace, outputHelper, options =>
        {
            options.EnabledFeatures = [KnownFeatures.GitTemplatesEnabled];
        });
        using var provider = services.BuildServiceProvider();

        var command = provider.GetRequiredService<RootCommand>();
        var result = command.Parse("template");

        var exitCode = await result.InvokeAsync().DefaultTimeout();

        Assert.Equal(ExitCodeConstants.InvalidCommand, exitCode);
    }

    [Fact]
    public async Task GitTemplateCommand_WithHelpArgument_ReturnsZero()
    {
        using var workspace = TemporaryWorkspace.Create(outputHelper);
        var services = CliTestHelper.CreateServiceCollection(workspace, outputHelper, options =>
        {
            options.EnabledFeatures = [KnownFeatures.GitTemplatesEnabled];
        });
        using var provider = services.BuildServiceProvider();

        var command = provider.GetRequiredService<RootCommand>();
        var result = command.Parse("template --help");

        var exitCode = await result.InvokeAsync().DefaultTimeout();
        Assert.Equal(ExitCodeConstants.Success, exitCode);
    }

    [Theory]
    [InlineData("template list --help")]
    [InlineData("template search keyword --help")]
    [InlineData("template refresh --help")]
    [InlineData("template new --help")]
    [InlineData("template new ./path --help")]
    public async Task GitTemplateSubcommand_WithHelpArgument_ReturnsZero(string arguments)
    {
        using var workspace = TemporaryWorkspace.Create(outputHelper);
        var services = CliTestHelper.CreateServiceCollection(workspace, outputHelper, options =>
        {
            options.EnabledFeatures = [KnownFeatures.GitTemplatesEnabled];
        });
        using var provider = services.BuildServiceProvider();

        var command = provider.GetRequiredService<RootCommand>();
        var result = command.Parse(arguments);

        var exitCode = await result.InvokeAsync().DefaultTimeout();
        Assert.Equal(ExitCodeConstants.Success, exitCode);
    }

    [Theory]
    [InlineData("template list")]
    [InlineData("template search keyword")]
    [InlineData("template refresh")]
    [InlineData("template new")]
    public async Task GitTemplateSubcommand_StubReturnsSuccess(string arguments)
    {
        using var workspace = TemporaryWorkspace.Create(outputHelper);
        var services = CliTestHelper.CreateServiceCollection(workspace, outputHelper, options =>
        {
            options.EnabledFeatures = [KnownFeatures.GitTemplatesEnabled];
        });
        using var provider = services.BuildServiceProvider();

        var command = provider.GetRequiredService<RootCommand>();
        var result = command.Parse(arguments);

        var exitCode = await result.InvokeAsync().DefaultTimeout();
        Assert.Equal(ExitCodeConstants.Success, exitCode);
    }

    [Fact]
    public async Task GitTemplateSearchCommand_WithoutKeyword_ReturnsNonZero()
    {
        using var workspace = TemporaryWorkspace.Create(outputHelper);
        var services = CliTestHelper.CreateServiceCollection(workspace, outputHelper, options =>
        {
            options.EnabledFeatures = [KnownFeatures.GitTemplatesEnabled];
        });
        using var provider = services.BuildServiceProvider();

        var command = provider.GetRequiredService<RootCommand>();
        var result = command.Parse("template search");

        var exitCode = await result.InvokeAsync().DefaultTimeout();
        Assert.NotEqual(ExitCodeConstants.Success, exitCode);
    }
}
