// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Aspire.Hosting.Utils;
using Microsoft.Extensions.Logging;

namespace Aspire.Hosting;

internal static class LoggingHelpers
{
    public static void WriteDashboardUrl(ILogger logger, string? dashboardUrls, string? token, bool isContainer)
    {
        if (!StringUtils.TryGetUriFromDelimitedString(dashboardUrls, ";", out var firstDashboardUrl))
        {
            return;
        }

        if (!string.IsNullOrEmpty(token))
        {
            var message = !isContainer
                ? "Login to the dashboard at {DashboardLoginUrl}"
                : "Login to the dashboard at {DashboardLoginUrl} . The URL may need changes depending on how network access to the container is configured.";

            var dashboardUrl = $"{firstDashboardUrl.GetLeftPart(UriPartial.Authority)}/login?t={token}";
            logger.LogInformation(message, dashboardUrl);
        }
        else
        {
            var message = !isContainer
                ? "The dashboard is available at {DashboardUrl}"
                : "The dashboard is available at {DashboardUrl} . The URL may need changes depending on how network access to the container is configured.";

            var dashboardUrl = firstDashboardUrl.GetLeftPart(UriPartial.Authority);
            logger.LogInformation(message, dashboardUrl);
        }
    }
}
