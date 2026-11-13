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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace ImageGlass.Common.ServiceProviders.Update;


/// <summary>
/// Source-generated JSON context for the GitHub Releases API.
/// </summary>
[JsonSerializable(typeof(GitHubReleaseDto))]
[JsonSerializable(typeof(GitHubAssetDto))]
public partial class GitHubReleaseJsonContext : JsonSerializerContext;


/// <summary>
/// The fields LiteGlass reads from a GitHub release.
/// </summary>
public sealed class GitHubReleaseDto
{
    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }

    [JsonPropertyName("published_at")]
    public string? PublishedAt { get; set; }

    [JsonPropertyName("assets")]
    public List<GitHubAssetDto>? Assets { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}


/// <summary>
/// One file attached to a GitHub release.
/// </summary>
public sealed class GitHubAssetDto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("browser_download_url")]
    public string? BrowserDownloadUrl { get; set; }

    /// <summary>
    /// GitHub's asset digest, typically <c>sha256:&lt;hex&gt;</c>.
    /// </summary>
    [JsonPropertyName("digest")]
    public string? Digest { get; set; }
}


/// <summary>
/// A parsed LiteGlass release, ready to show and (when the hash is known) to install.
/// </summary>
public sealed class ParsedGitHubRelease
{
    public UpdateReleaseInfo? Release { get; init; }

    public string? Error { get; init; }

    /// <summary>
    /// URL of a sibling <c>.sha256</c> asset, when the zip itself has no digest.
    /// </summary>
    public string? Sha256Url { get; init; }

    public bool HasInstallableZip => Release?.Artifacts?.Values.Any(a => a.IsInstallable) == true;


    /// <summary>
    /// Returns a copy whose portable zip artifact carries <paramref name="sha256"/>.
    /// </summary>
    public ParsedGitHubRelease WithSha256(string? sha256, string artifactKey)
    {
        if (Release?.Artifacts is null || string.IsNullOrWhiteSpace(sha256)) return this;
        if (!Release.Artifacts.TryGetValue(artifactKey, out var artifact)) return this;

        var next = new Dictionary<string, UpdateArtifactInfo>(Release.Artifacts, StringComparer.Ordinal)
        {
            [artifactKey] = new UpdateArtifactInfo
            {
                Url = artifact.Url,
                Size = artifact.Size,
                Sha256 = sha256.Trim().ToLowerInvariant(),
            },
        };

        return new ParsedGitHubRelease
        {
            Release = CopyRelease(Release, next),
            Sha256Url = null,
        };
    }


    private static UpdateReleaseInfo CopyRelease(UpdateReleaseInfo source, Dictionary<string, UpdateArtifactInfo> artifacts)
    {
        return new UpdateReleaseInfo
        {
            Version = source.Version,
            Title = source.Title,
            Description = source.Description,
            ChangelogUrl = source.ChangelogUrl,
            UpdateUrl = source.UpdateUrl,
            PublishedDate = source.PublishedDate,
            Artifacts = artifacts,
        };
    }
}


/// <summary>
/// Turns a GitHub release payload into the app's update model. No network.
/// </summary>
public static class GitHubReleaseParser
{
    public const string RepoSlug = "ghostltx/LiteGlass";

    public const int MaxNotesLength = 8000;


    /// <summary>
    /// Stable asset name the release workflow publishes, for example <c>LiteGlass-win-x64-portable.zip</c>.
    /// </summary>
    public static string PortableZipName(string arch) => $"LiteGlass-win-{arch}-portable.zip";


    /// <summary>
    /// Key stored on <see cref="UpdateReleaseInfo.Artifacts"/>.
    /// </summary>
    public static string ArtifactKey(string arch) => $"win-{arch}-zip";


    /// <summary>
    /// Strips a leading <c>v</c> and any <c>-channel</c> suffix so <see cref="Version"/> can parse it.
    /// </summary>
    public static string NormalizeVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return string.Empty;

        var value = version.Trim();
        if (value.Length > 1 && (value[0] == 'v' || value[0] == 'V')) value = value[1..];

        var dash = value.IndexOf('-');
        if (dash > 0) value = value[..dash];

        return value.Trim();
    }


    /// <summary>
    /// Positive when <paramref name="remoteVersion"/> is newer.
    /// </summary>
    public static int CompareVersions(string currentVersion, string remoteVersion)
    {
        var currentText = NormalizeVersion(currentVersion);
        var remoteText = NormalizeVersion(remoteVersion);
        if (!Version.TryParse(currentText, out var current)) return 0;
        if (!Version.TryParse(remoteText, out var remote)) return 0;

        return remote.CompareTo(current);
    }


    /// <summary>
    /// First hex token of a <c>.sha256</c> sidecar file.
    /// </summary>
    public static string? ParseSha256File(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var token = text.Trim().Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (token is null) return null;
        if (token.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) token = token["sha256:".Length..];

        if (token.Length != 64 || !token.All(Uri.IsHexDigit)) return null;
        return token.ToLowerInvariant();
    }


    /// <summary>
    /// Parses the latest-release JSON for one CPU architecture.
    /// </summary>
    public static ParsedGitHubRelease Parse(string json, string arch)
    {
        GitHubReleaseDto? dto;
        try
        {
            dto = System.Text.Json.JsonSerializer.Deserialize(json, GitHubReleaseJsonContext.Default.GitHubReleaseDto);
        }
        catch (Exception ex)
        {
            return new ParsedGitHubRelease { Error = ex.Message };
        }

        if (dto is null) return new ParsedGitHubRelease { Error = "Empty GitHub release." };
        if (string.IsNullOrWhiteSpace(dto.TagName))
        {
            return new ParsedGitHubRelease { Error = dto.Message ?? "The GitHub release has no version tag." };
        }

        var version = NormalizeVersion(dto.TagName);
        if (!Version.TryParse(version, out _))
        {
            return new ParsedGitHubRelease { Error = $"Unrecognized release tag '{dto.TagName}'." };
        }

        var key = ArtifactKey(arch);
        var zipName = PortableZipName(arch);
        var assets = dto.Assets ?? [];
        var zip = assets.FirstOrDefault(a => NameIs(a, zipName))
            ?? assets.FirstOrDefault(a => IsFallbackZip(a, arch));

        var artifacts = new Dictionary<string, UpdateArtifactInfo>(StringComparer.Ordinal);
        string? shaUrl = null;
        if (zip?.BrowserDownloadUrl is { Length: > 0 } url)
        {
            var sha = ParseDigest(zip.Digest);
            shaUrl = assets.FirstOrDefault(a => NameIs(a, zipName + ".sha256") || NameIs(a, PortableZipName(arch) + ".sha256"))
                ?.BrowserDownloadUrl;
            if (sha is null)
            {
                shaUrl ??= assets.FirstOrDefault(a =>
                    a.Name?.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase) == true
                    && a.Name.Contains(arch, StringComparison.OrdinalIgnoreCase))?.BrowserDownloadUrl;
            }

            artifacts[key] = new UpdateArtifactInfo
            {
                Url = url,
                Size = zip.Size,
                Sha256 = sha ?? string.Empty,
            };
        }

        var notes = dto.Body ?? string.Empty;
        if (notes.Length > MaxNotesLength) notes = notes[..MaxNotesLength];

        return new ParsedGitHubRelease
        {
            Sha256Url = string.IsNullOrEmpty(artifacts.GetValueOrDefault(key)?.Sha256) ? shaUrl : null,
            Release = new UpdateReleaseInfo
            {
                Version = version,
                Title = string.IsNullOrWhiteSpace(dto.Name) ? $"LiteGlass {version}" : dto.Name.Trim(),
                Description = notes,
                ChangelogUrl = dto.HtmlUrl ?? string.Empty,
                UpdateUrl = dto.HtmlUrl ?? string.Empty,
                PublishedDate = dto.PublishedAt ?? string.Empty,
                Artifacts = artifacts,
            },
        };
    }


    /// <summary>
    /// Whether a downloaded byte count matches the release asset. A missing size is not a failure.
    /// </summary>
    public static bool SizeMatches(long expected, long actual) => expected <= 0 || expected == actual;


    private static bool NameIs(GitHubAssetDto asset, string name) =>
        string.Equals(asset.Name, name, StringComparison.OrdinalIgnoreCase);


    private static bool IsFallbackZip(GitHubAssetDto asset, string arch)
    {
        var name = asset.Name;
        if (string.IsNullOrEmpty(name)) return false;
        return name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            && name.Contains("LiteGlass", StringComparison.OrdinalIgnoreCase)
            && name.Contains($"win-{arch}", StringComparison.OrdinalIgnoreCase)
            && !name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase);
    }


    private static string? ParseDigest(string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest)) return null;
        return ParseSha256File(digest);
    }
}
