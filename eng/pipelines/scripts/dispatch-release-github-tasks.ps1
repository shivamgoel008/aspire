# Dispatches the release-github-tasks.yml workflow on microsoft/aspire as the
# aspire-repo-bot GitHub App, then polls the resulting run until it completes.
# Exits 0 only if the dispatched run concludes with 'success'.
#
# This script is invoked from the AzDO release-publish-nuget pipeline as the
# final stage of a release. It centralizes the GitHub App JWT mint, installation
# access token exchange, workflow dispatch, run-id resolution, and run polling
# so the pipeline YAML stays declarative.
#
# Authentication flow (all per GitHub App API docs):
#   https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app
#   1. Sign a JWT (RS256) with the App's private key. iss=<AppId>, iat=now-60s, exp=now+540s.
#   2. POST /app/installations/{installationId}/access_tokens with the JWT -> installation token (~1h).
#   3. POST /repos/{owner}/{repo}/actions/workflows/{file}/dispatches with the installation token.
#   4. Poll GET /repos/.../actions/runs filtered by workflow + branch to find the run we just queued
#      (workflow_dispatch does not return a run id directly — this is the documented workaround).
#   5. Poll the run until status=completed; succeed only if conclusion=success.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$AppId,
    [Parameter(Mandatory = $true)][string]$PrivateKeyPem,
    [Parameter(Mandatory = $true)][string]$Owner,
    [Parameter(Mandatory = $true)][string]$Repo,
    [Parameter(Mandatory = $true)][string]$WorkflowFile,
    [Parameter(Mandatory = $true)][string]$Ref,
    [Parameter(Mandatory = $true)][hashtable]$Inputs,
    [Parameter()][int]$PollIntervalSeconds = 30,
    [Parameter()][int]$PollTimeoutMinutes = 60
)

$ErrorActionPreference = 'Stop'

function ConvertTo-Base64Url {
    param([byte[]]$Bytes)
    # Base64url per RFC 7515 §2: standard Base64 with '+'->'-', '/'->'_', no '=' padding.
    return [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function New-GitHubAppJwt {
    param([string]$AppId, [string]$PrivateKeyPem)

    # GitHub requires RS256 JWTs. iat may be backdated up to 60s to tolerate clock skew;
    # exp must be <=10 minutes from iat. We use 9 minutes to stay safely under the cap.
    $now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    $header = @{ alg = 'RS256'; typ = 'JWT' } | ConvertTo-Json -Compress
    $payload = @{ iat = $now - 60; exp = $now + 540; iss = $AppId } | ConvertTo-Json -Compress

    $headerB64 = ConvertTo-Base64Url -Bytes ([Text.Encoding]::UTF8.GetBytes($header))
    $payloadB64 = ConvertTo-Base64Url -Bytes ([Text.Encoding]::UTF8.GetBytes($payload))
    $signingInput = "$headerB64.$payloadB64"

    $rsa = [System.Security.Cryptography.RSA]::Create()
    try {
        # ImportFromPem handles both PKCS#1 ("BEGIN RSA PRIVATE KEY") and PKCS#8
        # ("BEGIN PRIVATE KEY") PEMs. GitHub Apps emit PKCS#1 by default.
        $rsa.ImportFromPem($PrivateKeyPem.ToCharArray())
        $sigBytes = $rsa.SignData(
            [Text.Encoding]::UTF8.GetBytes($signingInput),
            [Security.Cryptography.HashAlgorithmName]::SHA256,
            [Security.Cryptography.RSASignaturePadding]::Pkcs1)
    }
    finally {
        $rsa.Dispose()
    }

    $sigB64 = ConvertTo-Base64Url -Bytes $sigBytes
    return "$signingInput.$sigB64"
}

function Invoke-GitHubApi {
    param(
        [string]$Method,
        [string]$Uri,
        [string]$Token,
        [object]$Body
    )

    $headers = @{
        Authorization          = "Bearer $Token"
        Accept                 = 'application/vnd.github+json'
        'X-GitHub-Api-Version' = '2022-11-28'
        'User-Agent'           = 'aspire-release-pipeline'
    }

    $params = @{
        Method  = $Method
        Uri     = $Uri
        Headers = $headers
    }

    if ($null -ne $Body) {
        $params['Body'] = ($Body | ConvertTo-Json -Depth 8 -Compress)
        $params['ContentType'] = 'application/json'
    }

    return Invoke-RestMethod @params
}

Write-Host "=== Dispatch Release GitHub Tasks ==="
Write-Host "Target: $Owner/$Repo workflow=$WorkflowFile ref=$Ref"
Write-Host "Inputs:"
foreach ($key in $Inputs.Keys) {
    Write-Host "  $key = $($Inputs[$key])"
}

# 1. Mint App JWT.
Write-Host "Minting GitHub App JWT..."
$jwt = New-GitHubAppJwt -AppId $AppId -PrivateKeyPem $PrivateKeyPem

# 2. Look up the installation id for the target repo. We avoid hardcoding the
# installation id so the script stays reusable; the JWT can read it via the
# /repos/{owner}/{repo}/installation endpoint.
Write-Host "Looking up installation id for $Owner/$Repo..."
$installation = Invoke-GitHubApi -Method GET -Uri "https://api.github.com/repos/$Owner/$Repo/installation" -Token $jwt
$installationId = $installation.id
Write-Host "Installation id: $installationId"

# 3. Exchange JWT for an installation access token (~1h lifetime).
Write-Host "Exchanging JWT for installation access token..."
$tokenResp = Invoke-GitHubApi -Method POST -Uri "https://api.github.com/app/installations/$installationId/access_tokens" -Token $jwt
$installationToken = $tokenResp.token
Write-Host "Installation token acquired (expires $($tokenResp.expires_at))."

# Record the time *before* dispatching so we can find the resulting run reliably.
# GitHub's workflow_dispatch endpoint returns 204 with no body — there is no run id
# in the response. The standard workaround is to filter actions/runs by event,
# workflow, branch, and a created>=<dispatch time> timestamp.
$dispatchedAt = [DateTimeOffset]::UtcNow

# 4. Dispatch the workflow.
Write-Host "Dispatching workflow $WorkflowFile on ref=$Ref..."
$dispatchBody = @{
    ref    = $Ref
    inputs = $Inputs
}
Invoke-GitHubApi -Method POST `
    -Uri "https://api.github.com/repos/$Owner/$Repo/actions/workflows/$WorkflowFile/dispatches" `
    -Token $installationToken `
    -Body $dispatchBody | Out-Null
Write-Host "✓ Workflow dispatch accepted."

# 5. Resolve the run id. The dispatched run is not always queryable instantly,
# so retry for up to 2 minutes. Filter by created>=dispatchedAt-30s to allow for
# clock skew between this runner and GitHub.
$createdFilter = $dispatchedAt.AddSeconds(-30).ToString('yyyy-MM-ddTHH:mm:ssZ')
$runId = $null
$runHtmlUrl = $null
$resolveDeadline = [DateTime]::UtcNow.AddMinutes(2)

while ([DateTime]::UtcNow -lt $resolveDeadline -and -not $runId) {
    Start-Sleep -Seconds 5
    $runsUri = "https://api.github.com/repos/$Owner/$Repo/actions/workflows/$WorkflowFile/runs?event=workflow_dispatch&branch=$([Uri]::EscapeDataString($Ref))&created=%3E%3D$createdFilter&per_page=10"
    try {
        $runs = Invoke-GitHubApi -Method GET -Uri $runsUri -Token $installationToken
    }
    catch {
        Write-Host "  (transient) Could not list runs yet: $($_.Exception.Message)"
        continue
    }

    if ($runs.workflow_runs -and $runs.workflow_runs.Count -gt 0) {
        # The list endpoint returns runs newest first. Pick the oldest one created
        # after our dispatch timestamp — that's the run we just queued.
        $candidate = $runs.workflow_runs | Sort-Object -Property created_at | Select-Object -First 1
        $runId = $candidate.id
        $runHtmlUrl = $candidate.html_url
        Write-Host "✓ Resolved dispatched run: $runHtmlUrl (id=$runId)"
        break
    }

    Write-Host "  Waiting for dispatched run to appear..."
}

if (-not $runId) {
    Write-Error "Could not resolve the dispatched workflow run within 2 minutes. Check the workflow run history manually."
    exit 1
}

# Surface the run URL in the AzDO job summary regardless of outcome.
Write-Host "##vso[task.setvariable variable=DispatchedRunUrl]$runHtmlUrl"
Write-Host "##[section]Dispatched run: $runHtmlUrl"

# 6. Poll the run until it reaches a terminal state.
$pollDeadline = [DateTime]::UtcNow.AddMinutes($PollTimeoutMinutes)
$status = $null
$conclusion = $null

while ([DateTime]::UtcNow -lt $pollDeadline) {
    Start-Sleep -Seconds $PollIntervalSeconds
    try {
        $run = Invoke-GitHubApi -Method GET -Uri "https://api.github.com/repos/$Owner/$Repo/actions/runs/$runId" -Token $installationToken
    }
    catch {
        Write-Host "  (transient) Poll failed: $($_.Exception.Message). Retrying."
        continue
    }

    $status = $run.status
    $conclusion = $run.conclusion
    Write-Host "  status=$status conclusion=$conclusion"

    if ($status -eq 'completed') {
        break
    }
}

if ($status -ne 'completed') {
    Write-Error "Dispatched workflow did not complete within $PollTimeoutMinutes minutes. Last status: $status. See $runHtmlUrl"
    exit 1
}

if ($conclusion -ne 'success') {
    Write-Error "Dispatched workflow finished with conclusion '$conclusion'. See $runHtmlUrl"
    exit 1
}

Write-Host "✓ Dispatched workflow completed successfully: $runHtmlUrl"
exit 0
