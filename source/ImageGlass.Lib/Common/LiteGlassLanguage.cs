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
using ImageGlass.Common.Localization;
using System;
using System.Globalization;
using System.Threading.Tasks;

namespace ImageGlass.Common;


/// <summary>
/// The two languages LiteGlass ships, and the fork's public credits.
/// </summary>
public static class LiteGlassLanguage
{
    public const string EnglishFile = "English";

    public const string ChineseFile = "Chinese Simplified.iglang.json";

    public const string ProjectUrl = "https://github.com/ghostltx/LiteGlass";

    public const string Maintainer = "Xelias（吕铁鑫）";


    /// <summary>
    /// Simplified Chinese when the OS UI language is zh, zh-CN, zh-Hans, or zh-SG. Otherwise English.
    /// </summary>
    public static string SystemDefaultFileName(CultureInfo? culture = null)
    {
        var name = (culture ?? CultureInfo.CurrentUICulture).Name;
        if (name.Equals("zh", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("zh-CN", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("zh-Hans", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("zh-SG", StringComparison.OrdinalIgnoreCase))
        {
            return ChineseFile;
        }

        return EnglishFile;
    }


    public static bool IsChinese(string? fileName) =>
        !string.IsNullOrEmpty(fileName)
        && fileName.Contains("Chinese Simplified", StringComparison.OrdinalIgnoreCase);


    /// <summary>
    /// Saves the choice next to the portable app and reloads strings immediately.
    /// </summary>
    public static string Text(LangId id, string fallback)
    {
        try
        {
            var text = Core.Lang[id];
            return string.IsNullOrWhiteSpace(text) ? fallback : text;
        }
        catch
        {
            return fallback;
        }
    }


    public static async Task ApplyAsync(string fileName)
    {
        Core.Config.Language = fileName;
        _ = await Core.Config.SaveAsync().ConfigureAwait(false);
        await Core.Config.LoadCurrentLanguageAsync().ConfigureAwait(false);
    }
}
