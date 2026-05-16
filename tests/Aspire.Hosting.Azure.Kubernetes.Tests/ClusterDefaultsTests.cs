// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#pragma warning disable ASPIREAZURE003 // AzureSubnetResource evaluation-only.
#pragma warning disable ASPIRECOMPUTE003

using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure.Kubernetes;
using Aspire.Hosting.Kubernetes;
using Aspire.Hosting.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace Aspire.Hosting.Azure.Tests;

public class ClusterDefaultsTests
{
    [Fact]
    public void WithClusterDefaults_BareCall_RegistersExpectedResources()
    {
        using var builder = TestDistributedApplicationBuilder.Create(
            DistributedApplicationOperation.Publish);

        var acmeEmail = builder.AddParameter("acme-email", "ops@contoso.com");
        builder.AddAzureKubernetesEnvironment("aks").WithClusterDefaults(acmeEmail);

        using var app = builder.Build();
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();

        // VNet, both subnets, gateway, AGC LB, cert-manager and issuer should all exist.
        Assert.Single(model.Resources.OfType<KubernetesGatewayResource>(), g => g.Name == "public-gw");
        Assert.Single(model.Resources.OfType<AzureKubernetesLoadBalancerResource>(), lb => lb.Name == "public");
        Assert.Single(model.Resources.OfType<CertManagerResource>(), c => c.Name == "cert-manager");
        Assert.Single(model.Resources.OfType<CertManagerIssuerResource>(), i => i.Name == "letsencrypt");
    }

    [Fact]
    public void WithClusterDefaults_OverridesAddressSpace_PropagatesToVnetAndSubnets()
    {
        using var builder = TestDistributedApplicationBuilder.Create(
            DistributedApplicationOperation.Publish);

        var acmeEmail = builder.AddParameter("acme-email", "ops@contoso.com");
        builder.AddAzureKubernetesEnvironment("aks").WithClusterDefaults(acmeEmail, o =>
        {
            o.AddressSpace = "172.16.0.0/16";
            o.AksSubnetCidr = "172.16.0.0/22";
            o.LoadBalancerSubnetCidr = "172.16.4.0/24";
        });

        using var app = builder.Build();
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();

        var vnet = Assert.Single(model.Resources.OfType<AzureVirtualNetworkResource>());
        Assert.Equal("aks-vnet", vnet.Name);
        // Subnets are registered as model resources too.
        Assert.Contains(model.Resources.OfType<AzureSubnetResource>(), s => s.Name == "aks-nodes");
        Assert.Contains(model.Resources.OfType<AzureSubnetResource>(), s => s.Name == "alb-public");
    }

    [Fact]
    public async Task WithClusterDefaults_AutoRoutesExternalHttpEndpoints()
    {
        using var builder = TestDistributedApplicationBuilder.Create(
            DistributedApplicationOperation.Publish);

        var acmeEmail = builder.AddParameter("acme-email", "ops@contoso.com");
        builder.AddAzureKubernetesEnvironment("aks").WithClusterDefaults(acmeEmail);

        builder.AddContainer("api", "myimage")
               .WithHttpEndpoint(targetPort: 8080, name: "http")
               .WithExternalHttpEndpoints();

        using var app = builder.Build();
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        var gateway = model.Resources.OfType<KubernetesGatewayResource>().Single();
        await app.RunAsync();

        // BeforePublishEvent should have populated a route for the api container under /api.
        Assert.Contains(gateway.Routes, r => r.Path == "/api" && r.Endpoint.Resource.Name == "api");
    }

    [Fact]
    public async Task WithClusterDefaults_RespectsUserAuthoredRoutes()
    {
        using var builder = TestDistributedApplicationBuilder.Create(
            DistributedApplicationOperation.Publish);

        var acmeEmail = builder.AddParameter("acme-email", "ops@contoso.com");
        builder.AddAzureKubernetesEnvironment("aks").WithClusterDefaults(acmeEmail);

        var api = builder.AddContainer("api", "myimage")
                         .WithHttpEndpoint(targetPort: 8080, name: "http")
                         .WithExternalHttpEndpoints();

        // User wires the gateway by hand to a different path — the auto-router must skip "api".
        var gatewayResource = builder.Resources.OfType<KubernetesGatewayResource>().Single();
        var gatewayBuilder = builder.CreateResourceBuilder(gatewayResource);
        gatewayBuilder.WithRoute("/custom-api", api.GetEndpoint("http"));

        using var app = builder.Build();
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        var gateway = model.Resources.OfType<KubernetesGatewayResource>().Single();
        await app.RunAsync();

        var apiRoutes = gateway.Routes.Where(r => r.Endpoint.Resource.Name == "api").ToList();
        Assert.Single(apiRoutes);
        Assert.Equal("/custom-api", apiRoutes[0].Path);
    }

    [Fact]
    public async Task WithClusterDefaults_DisableAutoRoute_LeavesNoUserRoutes()
    {
        using var builder = TestDistributedApplicationBuilder.Create(
            DistributedApplicationOperation.Publish);

        var acmeEmail = builder.AddParameter("acme-email", "ops@contoso.com");
        builder.AddAzureKubernetesEnvironment("aks").WithClusterDefaults(acmeEmail, o =>
        {
            o.AutoRouteExternalEndpoints = false;
        });

        builder.AddContainer("api", "myimage")
               .WithHttpEndpoint(targetPort: 8080, name: "http")
               .WithExternalHttpEndpoints();

        using var app = builder.Build();
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        var gateway = model.Resources.OfType<KubernetesGatewayResource>().Single();
        await app.RunAsync();

        Assert.Empty(gateway.Routes);
    }

    [Fact]
    public void WithClusterDefaults_DisableTls_DoesNotProvisionCertManager()
    {
        using var builder = TestDistributedApplicationBuilder.Create(
            DistributedApplicationOperation.Publish);

        var acmeEmail = builder.AddParameter("acme-email", "ops@contoso.com");
        builder.AddAzureKubernetesEnvironment("aks").WithClusterDefaults(acmeEmail, o =>
        {
            o.EnableTls = false;
        });

        using var app = builder.Build();
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();

        Assert.Empty(model.Resources.OfType<CertManagerResource>());
        Assert.Empty(model.Resources.OfType<CertManagerIssuerResource>());
        Assert.Single(model.Resources.OfType<KubernetesGatewayResource>());
    }

    [Fact]
    public void WithClusterDefaults_ThrowsOnNullBuilder()
    {
        using var b = TestDistributedApplicationBuilder.Create();
        var acmeEmail = b.AddParameter("acme-email", "ops@contoso.com");

        IResourceBuilder<AzureKubernetesEnvironmentResource> nullBuilder = null!;

        Assert.Throws<ArgumentNullException>(() => nullBuilder.WithClusterDefaults(acmeEmail));
    }

    [Fact]
    public void WithClusterDefaults_ThrowsOnNullAcmeEmail()
    {
        using var builder = TestDistributedApplicationBuilder.Create();
        var aks = builder.AddAzureKubernetesEnvironment("aks");

        Assert.Throws<ArgumentNullException>(() => aks.WithClusterDefaults(null!));
    }

    [Fact]
    public async Task WithClusterDefaults_MultiExternalEndpoints_AppendEndpointNameToPath()
    {
        using var builder = TestDistributedApplicationBuilder.Create(
            DistributedApplicationOperation.Publish);

        var acmeEmail = builder.AddParameter("acme-email", "ops@contoso.com");
        builder.AddAzureKubernetesEnvironment("aks").WithClusterDefaults(acmeEmail);

        builder.AddContainer("api", "myimage")
               .WithHttpEndpoint(targetPort: 8080, name: "http")
               .WithHttpEndpoint(targetPort: 9090, name: "grpc")
               .WithExternalHttpEndpoints();

        using var app = builder.Build();
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        var gateway = model.Resources.OfType<KubernetesGatewayResource>().Single();
        await app.RunAsync();

        Assert.Contains(gateway.Routes, r => r.Path == "/api-http");
        Assert.Contains(gateway.Routes, r => r.Path == "/api-grpc");
    }
}
