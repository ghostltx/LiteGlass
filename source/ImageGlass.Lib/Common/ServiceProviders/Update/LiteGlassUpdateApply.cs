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
using ImageGlass.Common.Loggers;
using ImageGlass.Common.Types;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.IO.Compression;
using System.Threading.Tasks;

namespace ImageGlass.Common.ServiceProviders.Update;


/// <summary>
/// Swaps a staged portable zip into the install folder after this process exits, then relaunches.
/// Settings files and the portable marker are left in place.
/// </summary>
public static class LiteGlassUpdateApply
{
    public const string ExeName = LiteGlassUpdaterScript.ExeName;


    /// <summary>
    /// True when the zip actually contains the app executable.
    /// </summary>
    public static bool PackageContainsApp(string zipPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            return zip.Entries.Any(entry => entry.Name.Equals(LiteGlassUpdaterScript.ExeName, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }


    /// <summary>
    /// Applies a verified staged package before the window is created. Returns true when the process should exit.
    /// </summary>
    public static bool TryLaunchStagedUpdate()
    {
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            var release = AppUpdateDownloader.ReadPendingRelease();
            if (release is null || string.IsNullOrWhiteSpace(release.Version)) return false;
            if (GitHubReleaseParser.CompareVersions(Core.BuildInfo.Version, release.Version) <= 0) return false;

            var zipPath = AppUpdateDownloader.FindCachedPackage(release.Version);
            if (zipPath is null || !File.Exists(zipPath)) return false;
            if (!PackageContainsApp(zipPath)) return false;

            var artifact = release.Artifacts?.Values.FirstOrDefault(item => item.IsInstallable);
            if (artifact is null) return false;
            if (!AppUpdateDownloader.VerifyAsync(zipPath, artifact.Sha256).GetAwaiter().GetResult()) return false;

            return StartUpdater(zipPath, exitProcess: false);
        }
        catch (Exception ex)
        {
            UpdateTrace.Mark($"apply:staged {ex.Message}");
            return false;
        }
    }


    /// <summary>
    /// Starts the updater script. When <paramref name="exitProcess"/> is set, this process ends immediately.
    /// </summary>
    public static bool StartUpdater(string zipPath, bool exitProcess)
    {
        var destDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (string.IsNullOrEmpty(destDir) || !File.Exists(zipPath)) return false;

        var scriptPath = Path.Combine(Path.GetTempPath(), $"LiteGlass-update-{Environment.ProcessId}.ps1");
        File.WriteAllText(scriptPath, LiteGlassUpdaterScript.Build());

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-WindowStyle");
        psi.ArgumentList.Add("Hidden");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(scriptPath);
        psi.ArgumentList.Add("-WaitPid");
        psi.ArgumentList.Add(Environment.ProcessId.ToString());
        psi.ArgumentList.Add("-ZipPath");
        psi.ArgumentList.Add(zipPath);
        psi.ArgumentList.Add("-DestDir");
        psi.ArgumentList.Add(destDir);

        var started = Process.Start(psi);
        if (started is null) return false;

        UpdateTrace.Mark($"apply:updater {zipPath}");
        if (exitProcess) Environment.Exit(0);
        return true;
    }
}
