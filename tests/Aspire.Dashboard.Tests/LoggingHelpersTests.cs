// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Aspire.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Xunit;

namespace Aspire.Dashboard.Tests;

public class LoggingHelpersTests
{
    [Fact]
    public void WriteDashboardUrl_WithToken_LogsLoginUrl()
    {
        var sink = new TestSink();
        var logger = new TestLogger("TestLogger", sink, enabled: true);

        LoggingHelpers.WriteDashboardUrl(logger, "http://localhost:18888", "abc123", isContainer: false);

        var write = Assert.Single(sink.Writes);
        Assert.Equal(LogLevel.Information, write.LogLevel);
        Assert.Contains("/login?t=abc123", write.Message);
        Assert.Contains("Login to the dashboard at", write.Message);
    }

    [Fact]
    public void WriteDashboardUrl_WithToken_IsContainer_LogsContainerMessage()
    {
        var sink = new TestSink();
        var logger = new TestLogger("TestLogger", sink, enabled: true);

        LoggingHelpers.WriteDashboardUrl(logger, "http://localhost:18888", "abc123", isContainer: true);

        var write = Assert.Single(sink.Writes);
        Assert.Equal(LogLevel.Information, write.LogLevel);
        Assert.Contains("/login?t=abc123", write.Message);
        Assert.Contains("URL may need changes depending on how network access to the container is configured", write.Message);
    }

    [Fact]
    public void WriteDashboardUrl_WithoutToken_LogsDashboardUrl()
    {
        var sink = new TestSink();
        var logger = new TestLogger("TestLogger", sink, enabled: true);

        LoggingHelpers.WriteDashboardUrl(logger, "http://localhost:18888", token: null, isContainer: false);

        var write = Assert.Single(sink.Writes);
        Assert.Equal(LogLevel.Information, write.LogLevel);
        Assert.Contains("The dashboard is available at", write.Message);
        Assert.Contains("http://localhost:18888", write.Message);
        Assert.DoesNotContain("/login?t=", write.Message);
    }

    [Fact]
    public void WriteDashboardUrl_WithoutToken_IsContainer_LogsContainerMessage()
    {
        var sink = new TestSink();
        var logger = new TestLogger("TestLogger", sink, enabled: true);

        LoggingHelpers.WriteDashboardUrl(logger, "http://localhost:18888", token: null, isContainer: true);

        var write = Assert.Single(sink.Writes);
        Assert.Equal(LogLevel.Information, write.LogLevel);
        Assert.Contains("The dashboard is available at", write.Message);
        Assert.Contains("URL may need changes depending on how network access to the container is configured", write.Message);
    }

    [Fact]
    public void WriteDashboardUrl_EmptyToken_LogsDashboardUrlWithoutLogin()
    {
        var sink = new TestSink();
        var logger = new TestLogger("TestLogger", sink, enabled: true);

        LoggingHelpers.WriteDashboardUrl(logger, "http://localhost:18888", token: "", isContainer: false);

        var write = Assert.Single(sink.Writes);
        Assert.Equal(LogLevel.Information, write.LogLevel);
        Assert.Contains("The dashboard is available at", write.Message);
        Assert.DoesNotContain("/login?t=", write.Message);
    }

    [Fact]
    public void WriteDashboardUrl_InvalidUrl_DoesNotLog()
    {
        var sink = new TestSink();
        var logger = new TestLogger("TestLogger", sink, enabled: true);

        LoggingHelpers.WriteDashboardUrl(logger, "not-a-url", token: "abc123", isContainer: false);

        Assert.Empty(sink.Writes);
    }

    [Fact]
    public void WriteDashboardUrl_NullUrl_DoesNotLog()
    {
        var sink = new TestSink();
        var logger = new TestLogger("TestLogger", sink, enabled: true);

        LoggingHelpers.WriteDashboardUrl(logger, dashboardUrls: null, token: "abc123", isContainer: false);

        Assert.Empty(sink.Writes);
    }

    [Fact]
    public void WriteDashboardUrl_SemicolonDelimitedUrls_UsesFirstUrl()
    {
        var sink = new TestSink();
        var logger = new TestLogger("TestLogger", sink, enabled: true);

        LoggingHelpers.WriteDashboardUrl(logger, "http://localhost:18888;http://localhost:19999", "mytoken", isContainer: false);

        var write = Assert.Single(sink.Writes);
        Assert.Contains("http://localhost:18888/login?t=mytoken", write.Message);
        Assert.DoesNotContain("19999", write.Message);
    }
}
