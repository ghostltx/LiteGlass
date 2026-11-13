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
namespace ImageGlass.Common.ServiceProviders.Update;


/// <summary>
/// The text of the portable-zip updater. Kept free of app services so it can be tested on its own.
/// </summary>
public static class LiteGlassUpdaterScript
{
    public const string ExeName = "LiteGlass.exe";


    /// <summary>
    /// Names the updater must not overwrite. Portable settings live in these files.
    /// </summary>
    public static readonly string[] PreservedFileNames =
    [
        "igconfig.json",
        "igconfig.admin.json",
        "igconfig.default.json",
        ".igportable",
    ];


    /// <summary>
    /// Folders the updater must not replace.
    /// </summary>
    public static readonly string[] PreservedDirectoryNames = ["_cache", "_logs", "_update"];


    /// <summary>
    /// PowerShell that waits for this process, copies the new build, and starts it again.
    /// </summary>
    public static string Build()
    {
        var files = string.Join(' ', PreservedFileNames);
        var dirs = string.Join(' ', PreservedDirectoryNames);
        return $$"""
            param(
                [int]$WaitPid = 0,
                [Parameter(Mandatory = $true)][string]$ZipPath,
                [Parameter(Mandatory = $true)][string]$DestDir
            )
            $ErrorActionPreference = 'Stop'
            if ($WaitPid -gt 0) {
                try { Wait-Process -Id $WaitPid -Timeout 90 -ErrorAction SilentlyContinue } catch {}
            }
            $deadline = (Get-Date).AddSeconds(45)
            $exePath = Join-Path $DestDir '{{ExeName}}'
            while ((Test-Path $exePath) -and (Get-Date) -lt $deadline) {
                try {
                    $stream = [System.IO.File]::Open($exePath, 'Open', 'Read', 'ReadWrite')
                    $stream.Dispose()
                    break
                } catch {
                    Start-Sleep -Milliseconds 200
                }
            }
            $temp = Join-Path $env:TEMP ('LiteGlass-upd-' + [guid]::NewGuid().ToString('N'))
            New-Item -ItemType Directory -Path $temp | Out-Null
            try {
                Expand-Archive -LiteralPath $ZipPath -DestinationPath $temp -Force
                $exe = Get-ChildItem -LiteralPath $temp -Filter '{{ExeName}}' -Recurse -File | Select-Object -First 1
                if (-not $exe) { throw '{{ExeName}} was not in the update package.' }
                & robocopy $exe.DirectoryName $DestDir /E /R:2 /W:1 /NFL /NDL /NJH /NJS /nc /ns /np /XF {{files}} /XD {{dirs}}
                if ($LASTEXITCODE -ge 8) { throw "robocopy failed with exit code $LASTEXITCODE" }
                $staged = Join-Path $DestDir '_cache\_update'
                if (Test-Path -LiteralPath $staged) { Remove-Item -LiteralPath $staged -Recurse -Force -ErrorAction SilentlyContinue }
                Start-Process -FilePath (Join-Path $DestDir '{{ExeName}}')
            } finally {
                if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue }
            }
            """;
    }
}
