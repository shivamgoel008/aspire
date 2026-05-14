// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;

namespace Aspire.Cli.Acquisition;

/// <summary>
/// Default <see cref="IInstallSidecarReader"/> backed by the on-disk
/// <c>.aspire-install.json</c> file. Read with <see cref="JsonDocument"/>
/// (AOT-safe) and returns <see cref="InstallSource.Unknown"/> for any
/// unrecognized or malformed value rather than throwing — callers treat
/// unknown sources as legacy / pre-sidecar installs and fall back to the
/// pre-sidecar layout heuristic.
/// </summary>
internal sealed class InstallSidecarReader : IInstallSidecarReader
{
    /// <summary>
    /// Well-known file name of the sidecar that each install route writes
    /// next to the CLI binary. Matches the contract in
    /// <c>docs/specs/install-routes.md</c>.
    /// </summary>
    public const string SidecarFileName = ".aspire-install.json";

    /// <inheritdoc />
    public InstallSidecarInfo? TryRead(string binaryDir)
    {
        if (string.IsNullOrEmpty(binaryDir))
        {
            return null;
        }

        var sidecarPath = Path.Combine(binaryDir, SidecarFileName);
        if (!File.Exists(sidecarPath))
        {
            return null;
        }

        var rawSource = ReadSourceField(sidecarPath);
        var parsed = InstallSourceExtensions.ParseInstallSource(rawSource);
        return new InstallSidecarInfo(sidecarPath, parsed, rawSource);
    }

    /// <summary>
    /// Reads the <c>source</c> field from a sidecar file at a known path.
    /// Static helper used by <c>BundleService.ComputeDefaultExtractDir</c>,
    /// which runs before DI is wired and cannot take a service dependency.
    /// Returns <see langword="null"/> when the file is missing, unreadable,
    /// or contains malformed / unexpected JSON.
    /// </summary>
    internal static string? ReadSourceField(string sidecarPath)
    {
        if (!File.Exists(sidecarPath))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(sidecarPath);
            using var doc = JsonDocument.Parse(stream);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("source", out var sourceElement) &&
                sourceElement.ValueKind == JsonValueKind.String)
            {
                return sourceElement.GetString();
            }
        }
        catch (IOException)
        {
            // File disappeared between File.Exists and File.OpenRead, or read failed.
        }
        catch (JsonException)
        {
            // Sidecar exists but contains invalid JSON.
        }

        return null;
    }
}
