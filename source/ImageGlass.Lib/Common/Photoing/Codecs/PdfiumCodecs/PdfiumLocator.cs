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
using System.Buffers.Binary;

namespace ImageGlass.Common.Photoing;


/// <summary>
/// A TIFF or WMF preview stored in a DOS EPS binary header.
/// </summary>
public readonly record struct EpsPreview(int Offset, int Length, string Kind);


/// <summary>
/// Finds PDF bytes inside PDF, AI, and EPS files, and DOS EPS previews. No native code.
/// </summary>
public static class PdfiumLocator
{
    public static readonly string[] Extensions = [".pdf", ".ai", ".eps"];

    /// <summary>
    /// How far into a file the PDF header is searched. Illustrator writes it at the start.
    /// </summary>
    public const int MaxHeaderSearch = 32 * 1024 * 1024;

    private static ReadOnlySpan<byte> PdfSignature => "%PDF-"u8;


    public static bool IsSupportedExtension(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var ext = System.IO.Path.GetExtension(path);
        foreach (var candidate in Extensions)
        {
            if (ext.Equals(candidate, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }


    /// <summary>
    /// Offset of <c>%PDF-</c>, or -1 when the span has no PDF header in the search window.
    /// </summary>
    public static int FindPdfOffset(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) return -1;
        var window = data.Length <= MaxHeaderSearch ? data : data[..MaxHeaderSearch];
        return window.IndexOf(PdfSignature);
    }


    /// <summary>
    /// Reads the DOS EPS binary preview, preferring TIFF over WMF.
    /// </summary>
    public static EpsPreview? ReadDosPreview(ReadOnlySpan<byte> data)
    {
        if (data.Length < 30) return null;
        if (data[0] != 0xC5 || data[1] != 0xD0 || data[2] != 0xD3 || data[3] != 0xC6) return null;

        var tiff = ReadChunk(data, 20);
        if (tiff is not null) return tiff.Value with { Kind = "tiff" };

        var wmf = ReadChunk(data, 12);
        if (wmf is not null) return wmf.Value with { Kind = "wmf" };
        return null;
    }


    private static EpsPreview? ReadChunk(ReadOnlySpan<byte> data, int fieldOffset)
    {
        var offset = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(fieldOffset, 4));
        var length = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(fieldOffset + 4, 4));
        if (offset <= 0 || length <= 32) return null;
        if ((long)offset + length > data.Length) return null;
        return new EpsPreview(offset, length, string.Empty);
    }
}
