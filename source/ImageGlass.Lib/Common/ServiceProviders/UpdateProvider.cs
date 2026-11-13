/*
ImageGlass - A Fast, Seamless Photo Viewer
Copyright (C) 2010 - 2027 DUONG DIEU PHAP
Project homepage: https://imageglass.org

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/
using ImageGlass.Common.ServiceProviders.Update;
using ImageGlass.Common.Types;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ImageGlass.Common.ServiceProviders;

public partial class UpdateProvider : IUpdateProvider
{
    private static readonly HttpClient _httpClient = CreateHttpClient();
    private InterlockedBool _isChecking;


    /// <summary>
    /// Gets whether enough time has elapsed since the last check to warrant a new silent check.
    /// </summary>
    public static bool ShouldCheck
    {
        get
        {
            var lastCheckTime = ParseLastCheckTime();

            // a fresh offset each read, so same-cadence installs do not all report on the same day
            var jitterMinutes = UpdateConstants.CheckJitterMinutes <= 0
                ? 0
                : Random.Shared.Next(0, UpdateConstants.CheckJitterMinutes);
            var threshold = UpdateConstants.BackgroundCheckInterval + TimeSpan.FromMinutes(jitterMinutes);

            return (DateTime.UtcNow - lastCheckTime) >= threshold;
        }
    }


    /// <summary>
    /// Checks for an available update by fetching the update manifest.
    /// </summary>
    /// <param name="isScheduled">
    /// <c>true</c> for the periodic background check. Only a scheduled check carries the usage
    /// tokens and moves the bookmark; a manual check must leave both alone, or the user-triggered
    /// request would report despite an opt-out and would shorten the next reported gap.
    /// </param>
    public async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken ct, bool isScheduled = false)
    {
        if (_isChecking) return UpdateCheckResult.Failed("Check already in progress.");

        _isChecking.SetTrue();
        try
        {
            var result = await CheckForUpdateCoreAsync(isScheduled, ct).ConfigureAwait(false);

            if (isScheduled) await RememberScheduledCheckAsync().ConfigureAwait(false);

            return result;
        }
        catch (OperationCanceledException)
        {
            if (isScheduled) await RememberScheduledCheckAsync().ConfigureAwait(false);
            return UpdateCheckResult.Failed("Check was cancelled.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌❌❌ {nameof(CheckForUpdateAsync)}: {ex}");
            if (isScheduled) await RememberScheduledCheckAsync().ConfigureAwait(false);
            return UpdateCheckResult.Failed(ex.Message, ex);
        }
        finally
        {
            _isChecking.SetFalse();
        }
    }



    #region Private Methods

    /// <summary>
    /// Core logic: fetch metadata -> parse -> compare version.
    /// </summary>
    private static async Task<UpdateCheckResult> CheckForUpdateCoreAsync(bool isScheduled, CancellationToken ct)
    {
        var json = await FetchMetadataAsync(isScheduled, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(json))
        {
            return UpdateCheckResult.Failed("Empty response from GitHub.");
        }

        var arch = BHelper.ArchToken;
        var parsed = GitHubReleaseParser.Parse(json, arch);
        if (parsed.Release is null)
        {
            return UpdateCheckResult.Failed(parsed.Error ?? "Could not read the GitHub release.");
        }

        if (!string.IsNullOrEmpty(parsed.Sha256Url))
        {
            var sidecar = await FetchTextAsync(parsed.Sha256Url, ct).ConfigureAwait(false);
            parsed = parsed.WithSha256(GitHubReleaseParser.ParseSha256File(sidecar), GitHubReleaseParser.ArtifactKey(arch));
        }

        var release = parsed.Release;
        if (release is null || string.IsNullOrEmpty(release.Version))
        {
            return UpdateCheckResult.NoUpdate();
        }

        // already up to date; keep the release so a manual check can show its notes
        if (CompareVersions(Core.BuildInfo.Version, release.Version) <= 0)
        {
            return UpdateCheckResult.NoUpdate(release);
        }

        if (string.Equals(release.Version, Core.Config.UpdateSkippedVersion, StringComparison.OrdinalIgnoreCase))
        {
            return UpdateCheckResult.NoUpdate();
        }

        return UpdateCheckResult.Available(release);
    }


    /// <summary>
    /// Fetches the latest GitHub release JSON. No usage statistics are sent.
    /// </summary>
    private static async Task<string?> FetchMetadataAsync(bool isScheduled, CancellationToken ct)
    {
        _ = isScheduled;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(UpdateConstants.MetadataTimeout);

        using var request = new HttpRequestMessage(HttpMethod.Get, UpdateConstants.MetadataUrl);
        request.Headers.UserAgent.ParseAdd(ProductUserAgent());
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        // enforce size limit
        var contentLength = response.Content.Headers.ContentLength ?? 0;
        if (contentLength > UpdateConstants.MaxMetadataSize)
        {
            throw new InvalidOperationException($"IGE: Metadata too large: {contentLength} bytes.");
        }

        var json = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
        if (json.Length > UpdateConstants.MaxMetadataSize)
        {
            throw new InvalidOperationException($"IGE: Metadata body exceeded limit: {json.Length} chars.");
        }

        return json;
    }


    /// <summary>
    /// Parses the last check time from <see cref="Core.Config.AutoUpdate"/>.
    /// </summary>
    private static DateTime ParseLastCheckTime()
    {
        var value = Core.Config.AutoUpdate;
        if (string.Equals(value, "0", StringComparison.Ordinal)) return DateTime.MinValue;

        return BHelper.TryParseUtcRoundtrip(value, out var dt) ? dt : DateTime.MinValue;
    }


    /// <summary>
    /// Compares two version strings. Returns positive if remote is newer, 0 if equal, negative if older.
    /// </summary>
    internal static int CompareVersions(string currentVersion, string remoteVersion)
    {
        return GitHubReleaseParser.CompareVersions(currentVersion, remoteVersion);
    }


    private static async Task<string?> FetchTextAsync(string url, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(UpdateConstants.MetadataTimeout);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(ProductUserAgent());
        using var response = await _httpClient.SendAsync(request, timeoutCts.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
    }


    /// <summary>
    /// A failed or finished scheduled check still counts, so a down network is not retried on every launch.
    /// </summary>
    private static async Task RememberScheduledCheckAsync()
    {
        try
        {
            Core.Config.AutoUpdate = BHelper.FormatUtcRoundtrip(DateTime.UtcNow);
            _ = await Core.Config.SaveAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌❌❌ {nameof(RememberScheduledCheckAsync)}: {ex}");
        }
    }


    internal static string ProductUserAgent()
    {
        var version = Core.BuildInfo?.Version;
        if (string.IsNullOrWhiteSpace(version)) version = "0.0.0.0";
        return $"{BHelper.AppName}/{version}";
    }


    private static HttpClient CreateHttpClient()
    {
        return new HttpClient
        {
            Timeout = UpdateConstants.DownloadTimeout,
        };
    }

    #endregion // Private Methods

}
