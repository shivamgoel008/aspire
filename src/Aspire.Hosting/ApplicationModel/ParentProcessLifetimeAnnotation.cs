// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Aspire.Hosting.ApplicationModel;

using SystemProcess = System.Diagnostics.Process;

/// <summary>
/// Configures a persistent resource to be monitored by a parent process identity.
/// </summary>
internal sealed class ParentProcessLifetimeAnnotation(SystemProcess parentProcess) : IResourceAnnotation
{
    /// <summary>
    /// Gets the parent process to monitor.
    /// </summary>
    public SystemProcess ParentProcess { get; } = parentProcess ?? throw new ArgumentNullException(nameof(parentProcess));
}
