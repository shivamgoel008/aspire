// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Aspire.Hosting.Kubernetes;

namespace Aspire.Hosting.Azure.Kubernetes;

/// <summary>
/// Knobs for
/// <see cref="AzureKubernetesClusterDefaultsExtensions.WithClusterDefaults"/>. All
/// properties have defaults that produce the "pit of success" AKS configuration —
/// every knob exists only so callers can deviate from that default when their
/// environment requires it, never because they have to set it to make the basic
/// case work.
/// </summary>
/// <remarks>
/// <para>
/// The defaults match the verbose recipe in <c>playground/CertManagerDemo</c>
/// (which this options bag is designed to replace): a <c>10.100.0.0/16</c> VNet
/// chosen to avoid the AKS default service CIDR <c>10.0.0.0/16</c>, a
/// <c>/22</c> AKS-node subnet sized for ≈1000 pods, and a <c>/24</c> AGC
/// frontend subnet (the smallest AGC allows).
/// </para>
/// <para>
/// Naming defaults (<see cref="LoadBalancerName"/>, <see cref="GatewayName"/>,
/// etc.) are short and generic on purpose so they read naturally as resource
/// names in the dashboard ("public", "public-gw", "cert-manager", "letsencrypt").
/// Override them when stacking multiple <c>WithClusterDefaults</c>-style
/// recipes in one AppHost.
/// </para>
/// </remarks>
[AspireExport(ExposeProperties = true)]
public sealed class ClusterDefaultsOptions
{
    /// <summary>
    /// CIDR block for the auto-provisioned VNet. Defaults to <c>10.100.0.0/16</c>,
    /// chosen to avoid the AKS default service CIDR (<c>10.0.0.0/16</c>) so the
    /// pod and service networks don't collide.
    /// </summary>
    public string AddressSpace { get; set; } = "10.100.0.0/16";

    /// <summary>
    /// CIDR block for the AKS node-pool subnet. Defaults to <c>10.100.0.0/22</c>
    /// (1,024 IPs), which is enough headroom for the system node pool plus a
    /// modest workload node pool without bumping into the per-pod IP exhaustion
    /// failure mode.
    /// </summary>
    public string AksSubnetCidr { get; set; } = "10.100.0.0/22";

    /// <summary>
    /// CIDR block for the AGC public frontend subnet. Defaults to
    /// <c>10.100.4.0/24</c>. AGC requires a <c>/24</c> minimum and the subnet
    /// must be delegated to <c>Microsoft.ServiceNetworking/trafficControllers</c>
    /// (the underlying <see cref="AzureKubernetesEnvironmentExtensions.AddLoadBalancer"/>
    /// applies that delegation idempotently).
    /// </summary>
    public string LoadBalancerSubnetCidr { get; set; } = "10.100.4.0/24";

    /// <summary>
    /// VM size used for the AKS system node pool. Defaults to
    /// <c>Standard_D2as_v5</c> — the smallest size AKS will accept for the system
    /// pool that still leaves room for cert-manager, AGC's ALB controller, kube-system,
    /// and CoreDNS without scheduling pressure.
    /// </summary>
    public string SystemNodePoolVmSize { get; set; } = "Standard_D2as_v5";

    /// <summary>
    /// Minimum node count for the system node pool autoscaler. Defaults to 1.
    /// </summary>
    public int SystemNodePoolMinCount { get; set; } = 1;

    /// <summary>
    /// Maximum node count for the system node pool autoscaler. Defaults to 3.
    /// </summary>
    public int SystemNodePoolMaxCount { get; set; } = 3;

    /// <summary>
    /// Name of the auto-created <see cref="AzureKubernetesLoadBalancerResource"/>.
    /// Defaults to <c>"public"</c>.
    /// </summary>
    public string LoadBalancerName { get; set; } = "public";

    /// <summary>
    /// Name of the auto-created <see cref="KubernetesGatewayResource"/>. Defaults
    /// to <c>"public-gw"</c>.
    /// </summary>
    public string GatewayName { get; set; } = "public-gw";

    /// <summary>
    /// Name of the auto-created <see cref="CertManagerResource"/>. Defaults to
    /// <c>"cert-manager"</c>.
    /// </summary>
    public string CertManagerName { get; set; } = "cert-manager";

    /// <summary>
    /// Name of the auto-created <see cref="CertManagerIssuerResource"/>. Defaults
    /// to <c>"letsencrypt"</c>.
    /// </summary>
    public string IssuerName { get; set; } = "letsencrypt";

    /// <summary>
    /// Which Let's Encrypt environment the auto-provisioned issuer points at.
    /// Defaults to <see cref="LetsEncryptEnvironment.Production"/>.
    /// Switch to <see cref="LetsEncryptEnvironment.Staging"/> for
    /// development loops where you'd otherwise burn the production rate limit.
    /// </summary>
    public LetsEncryptEnvironment AcmeEnvironment { get; set; } = LetsEncryptEnvironment.Production;

    /// <summary>
    /// When <see langword="true"/> (the default), the auto-created gateway is
    /// configured for TLS via <see cref="KubernetesGatewayExtensions.WithTls(global::Aspire.Hosting.ApplicationModel.IResourceBuilder{KubernetesGatewayResource}, System.Action{TlsOptions}?)"/>
    /// (and the cert-manager + issuer resources are provisioned). Set to
    /// <see langword="false"/> for the rare case where the cluster needs a plain
    /// HTTP gateway — typically development against a non-public hostname where
    /// Let's Encrypt HTTP-01 validation cannot succeed.
    /// </summary>
    public bool EnableTls { get; set; } = true;

    /// <summary>
    /// Optional callback for tuning the TLS posture (HTTP→HTTPS redirect, HSTS
    /// directives). Forwarded directly to <c>gateway.WithTls(issuer, configure)</c>;
    /// see <see cref="TlsOptions"/> for the available knobs.
    /// </summary>
    public Action<TlsOptions>? ConfigureTls { get; set; }

    /// <summary>
    /// When <see langword="true"/> (the default), each resource in the
    /// application model that exposes one or more <c>IsExternal == true</c>
    /// endpoints is automatically attached to the auto-gateway via
    /// <c>WithRoute</c>.
    /// Resources that the user has already wired up by hand are skipped (the
    /// user always wins).
    /// </summary>
    public bool AutoRouteExternalEndpoints { get; set; } = true;

    /// <summary>
    /// Path template applied to each auto-routed resource. <c>{name}</c> is
    /// replaced with the resource name. Defaults to <c>"/{name}"</c>, which
    /// produces routes like <c>/api</c>, <c>/web</c>, etc.
    /// </summary>
    /// <remarks>
    /// When a single resource exposes more than one external endpoint, the
    /// template is suffixed with <c>-{endpointName}</c> so each endpoint
    /// receives a distinct path (for example, <c>/api-grpc</c>).
    /// </remarks>
    public string RoutePathTemplate { get; set; } = "/{name}";
}
