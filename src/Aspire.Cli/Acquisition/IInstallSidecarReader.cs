// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Aspire.Cli.Acquisition;

/// <summary>
/// Result of reading an install-route sidecar from a binary directory.
/// </summary>
/// <param name="SidecarPath">Absolute path of the sidecar file that was read.</param>
/// <param name="Source">
/// Parsed install route. <see cref="InstallSource.Unknown"/> when the sidecar
/// exists but its <c>source</c> field does not match a known route.
/// </param>
/// <param name="RawSource">
/// The literal <c>source</c> string from the sidecar (may be a value not yet
/// understood by this build). <see langword="null"/> when the sidecar file
/// exists but is malformed.
/// </param>
internal sealed record InstallSidecarInfo(string SidecarPath, InstallSource Source, string? RawSource);

/// <summary>
/// Reads the install-route sidecar (<c>.aspire-install.json</c>) that an
/// install route writes next to the CLI binary. The sidecar identifies the
/// installation route so callers (e.g. <c>BundleService</c>,
/// <c>aspire info</c>, <c>aspire uninstall</c>) can branch behavior without
/// path-shape heuristics.
/// </summary>
/// <remarks>
/// See <c>docs/specs/install-routes.md</c> for the file contract. The reader
/// is AOT-safe: parsing uses <c>JsonDocument</c> instead of reflection-based
/// deserialization.
/// </remarks>
internal interface IInstallSidecarReader
{
    /// <summary>
    /// Attempts to read the sidecar at
    /// <c>&lt;<paramref name="binaryDir"/>&gt;/.aspire-install.json</c>.
    /// </summary>
    /// <param name="binaryDir">Directory containing the CLI binary.</param>
    /// <returns>
    /// <see cref="InstallSidecarInfo"/> describing the sidecar contents, or
    /// <see langword="null"/> when the sidecar file does not exist or
    /// <paramref name="binaryDir"/> is empty.
    /// </returns>
    InstallSidecarInfo? TryRead(string binaryDir);
}
