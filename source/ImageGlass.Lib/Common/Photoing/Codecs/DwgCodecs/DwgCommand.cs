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
using System.IO;

namespace ImageGlass.Common.Photoing;


/// <summary>
/// Locates the bundled LibreDWG <c>dwg2SVG</c> tool and the messages shown when it cannot run.
/// </summary>
public static class DwgCommand
{
    public const int TimeoutSeconds = 20;

    public static readonly string[] ToolFileNames = ["dwg2SVG.exe", "dwg2svg.exe"];


    public static string MissingToolMessage =>
        "This DWG file could not be opened because dwg2SVG from LibreDWG was not found next to LiteGlass. Nothing was changed.";


    public static string TimeoutMessage =>
        "Rendering this DWG file took too long and was stopped so LiteGlass would not hang.";


    public static string FailedMessage(string? detail)
    {
        var text = string.IsNullOrWhiteSpace(detail) ? "LibreDWG could not read this drawing." : detail.Trim();
        if (text.Length > 500) text = text[..500];
        return $"This DWG file could not be rendered. {text}";
    }


    /// <summary>
    /// Looks beside the app and in a <c>libredwg</c> subfolder.
    /// </summary>
    public static string? FindTool(string baseDir)
    {
        if (string.IsNullOrWhiteSpace(baseDir)) return null;

        foreach (var dir in new[] { baseDir, Path.Combine(baseDir, "libredwg") })
        {
            foreach (var name in ToolFileNames)
            {
                var path = Path.Combine(dir, name);
                if (File.Exists(path)) return path;
            }
        }

        return null;
    }
}
