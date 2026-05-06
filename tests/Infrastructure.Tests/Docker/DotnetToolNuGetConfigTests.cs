// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Xml.Linq;
using Xunit;

namespace Infrastructure.Tests;

public sealed class DotnetToolNuGetConfigTests
{
    private static readonly string[] s_dotnetToolPackageSourceKeys =
    [
        "dotnet-public",
        "dotnet9",
        "dotnet-libraries"
    ];

    private static readonly Dictionary<string, string[]> s_expectedPackageSourceMappings = new()
    {
        ["dotnet9"] = ["Aspire.*"],
        ["dotnet-libraries"] = ["Microsoft.DeveloperControlPlane*"],
        ["dotnet-public"] = ["*"]
    };

    [Fact]
    public void DockerDotnetToolPackageSourcesMatchRootNuGetConfig()
    {
        var rootConfig = XDocument.Load(Path.Combine(FindRepoRoot(), "NuGet.config"));
        var dockerConfig = XDocument.Load(Path.Combine(FindRepoRoot(), "tests", "Shared", "Docker", "NuGet.DotnetTool.config"));

        var rootPackageSources = GetPackageSources(rootConfig);
        var expectedPackageSources = s_dotnetToolPackageSourceKeys.ToDictionary(key => key, key => rootPackageSources[key]);
        var dockerPackageSources = GetPackageSources(dockerConfig);

        Assert.Equal(expectedPackageSources, dockerPackageSources);
    }

    [Fact]
    public void DockerDotnetToolPackageSourceMappingsStayNarrow()
    {
        var dockerConfig = XDocument.Load(Path.Combine(FindRepoRoot(), "tests", "Shared", "Docker", "NuGet.DotnetTool.config"));
        var actualPackageSourceMappings = GetPackageSourceMappings(dockerConfig);

        Assert.Equal(s_expectedPackageSourceMappings.Keys, actualPackageSourceMappings.Keys);
        foreach (var (key, expectedPatterns) in s_expectedPackageSourceMappings)
        {
            Assert.Equal(expectedPatterns, actualPackageSourceMappings[key]);
        }
    }

    private static Dictionary<string, string> GetPackageSources(XDocument config)
    {
        return config.Root!
            .Element("packageSources")!
            .Elements("add")
            .ToDictionary(
                source => (string)source.Attribute("key")!,
                source => (string)source.Attribute("value")!);
    }

    private static Dictionary<string, string[]> GetPackageSourceMappings(XDocument config)
    {
        return config.Root!
            .Element("packageSourceMapping")!
            .Elements("packageSource")
            .ToDictionary(
                source => (string)source.Attribute("key")!,
                source => source.Elements("package")
                    .Select(package => (string)package.Attribute("pattern")!)
                    .ToArray());
    }

    private static string FindRepoRoot()
    {
        var currentDirectory = AppContext.BaseDirectory;
        while (currentDirectory is not null)
        {
            if (File.Exists(Path.Combine(currentDirectory, "NuGet.config")) &&
                Directory.Exists(Path.Combine(currentDirectory, "tests", "Shared", "Docker")))
            {
                return currentDirectory;
            }

            currentDirectory = Directory.GetParent(currentDirectory)?.FullName;
        }

        throw new InvalidOperationException("Could not find the repository root.");
    }
}
