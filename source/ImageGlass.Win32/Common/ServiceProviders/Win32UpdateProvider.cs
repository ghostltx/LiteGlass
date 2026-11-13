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
using ImageGlass.Common;
using ImageGlass.Common.Loggers;
using ImageGlass.Common.ServiceProviders;
using ImageGlass.Common.ServiceProviders.Update;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Networking.Connectivity;

namespace ImageGlass.Win32.Common.ServiceProviders;


/// <summary>
/// Update provider for the portable Windows build. A staged zip is applied by a small script.
/// </summary>
public class Win32UpdateProvider : UpdateProvider
{
    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public override bool IsInstallSupported => OperatingSystem.IsWindows();


    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public override string ArtifactKey => GitHubReleaseParser.ArtifactKey(BHelper.ArchToken);


    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public override bool IsMeteredConnection
    {
        get
        {
            // an unreadable cost must not block updates forever, so failure reads as unmetered
            try
            {
                var cost = NetworkInformation.GetInternetConnectionProfile()?.GetConnectionCost();
                if (cost is null) return false;

                return cost.NetworkCostType != NetworkCostType.Unrestricted
                    || cost.Roaming
                    || cost.OverDataLimit;
            }
            catch { return false; }
        }
    }


    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public override Task<UpdateOpResult> ApplyAndRestartAsync(string packageFilePath,
        UpdateReleaseInfo release, CancellationToken ct = default)
    {
        _ = ct;
        if (!IsInstallSupported)
        {
            return Task.FromResult(UpdateOpResult.Fail("IGE: This LiteGlass build cannot install its own updates."));
        }

        if (!File.Exists(packageFilePath))
        {
            return Task.FromResult(UpdateOpResult.Fail($"IGE: The downloaded update package is missing: {packageFilePath}"));
        }

        if (!LiteGlassUpdateApply.PackageContainsApp(packageFilePath))
        {
            return Task.FromResult(UpdateOpResult.Fail(
                "IGE: The update package does not contain LiteGlass.exe.",
                packageFilePath));
        }

        try
        {
            UpdateTrace.Mark($"apply:begin {release.Version}");
            // The script waits for this process, copies the new files, and relaunches. Settings stay put.
            if (!LiteGlassUpdateApply.StartUpdater(packageFilePath, exitProcess: true))
            {
                return Task.FromResult(UpdateOpResult.Fail("IGE: LiteGlass could not start its updater."));
            }

            return Task.FromResult(UpdateOpResult.Ok());
        }
        catch (Exception ex)
        {
            UpdateTrace.Mark($"apply:exception {ex}");
            return Task.FromResult(UpdateOpResult.Fail(ex));
        }
    }
}
