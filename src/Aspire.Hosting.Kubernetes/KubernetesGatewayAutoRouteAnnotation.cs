// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.Kubernetes;

/// <summary>
/// Marks a <see cref="KubernetesGatewayResource"/> as eligible for auto-routing every external
/// HTTP endpoint in the app model. Evaluated by the gateway-emission pipeline step so that
/// auto-routing happens deterministically inside the publish pipeline rather than being gated
/// on the <c>BeforePublishEvent</c> (which only fires for the legacy
/// <c>dotnet run -- --publisher kubernetes</c> path; <c>aspire deploy</c> drives pipeline
/// steps directly over JSON-RPC and never raises that event).
/// </summary>
/// <param name="PathTemplate">Path template applied to each routed endpoint. Supports a
/// <c>{name}</c> placeholder that expands to the resource name (e.g. <c>"/{name}"</c> →
/// <c>"/api"</c>). For multi-endpoint resources the endpoint name is appended.</param>
/// <param name="InfrastructureResourceNames">Names of resources to exclude from auto-routing
/// — typically the infra resources the recipe created itself (vnet, subnets, gateway, load
/// balancer, cert-manager, issuer). Existing routes set by the user via <c>WithRoute</c> are
/// always preserved (user-wins).</param>
internal sealed record KubernetesGatewayAutoRouteAnnotation(
    string PathTemplate,
    IReadOnlySet<string> InfrastructureResourceNames) : IResourceAnnotation;
