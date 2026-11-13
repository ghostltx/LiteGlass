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
using SkiaSharp;
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace ImageGlass.Common.Photoing;


/// <summary>
/// A small PDFium surface. The native library is BSD-licensed and ships beside the executable.
/// </summary>
internal static partial class PdfiumLibrary
{
    private const int RenderFlags = 0x01 | 0x10; // annotations, RGBA byte order
    private const int MaxEdge = 4096;
    private const int MaxFileBytes = 256 * 1024 * 1024;

    private static readonly Lock _gate = new();
    private static bool _ready;
    private static string? _loadError;


    public static string? LoadError
    {
        get
        {
            lock (_gate) return _loadError;
        }
    }


    /// <summary>
    /// Renders the first page. The caller owns the returned image.
    /// </summary>
    public static SKImage RenderFirstPage(ReadOnlyMemory<byte> pdfBytes)
    {
        if (pdfBytes.Length == 0) throw new InvalidOperationException("The file has no PDF content to render.");
        if (pdfBytes.Length > MaxFileBytes)
        {
            throw new InvalidOperationException("This file is too large to render with the built-in PDF engine.");
        }

        lock (_gate)
        {
            EnsureLoaded();
            return RenderFirstPageLocked(pdfBytes.Span);
        }
    }


    private static void EnsureLoaded()
    {
        if (_ready) return;
        if (_loadError is not null) throw new InvalidOperationException(_loadError);

        try
        {
            FPDF_InitLibrary();
            _ready = true;
        }
        catch (Exception ex)
        {
            _loadError = "PDF, AI, and EPS rendering needs pdfium.dll next to LiteGlass. "
                + "It was not loaded. " + ex.Message;
            throw new InvalidOperationException(_loadError);
        }
    }


    private static SKImage RenderFirstPageLocked(ReadOnlySpan<byte> pdfBytes)
    {
        IntPtr document = IntPtr.Zero;
        IntPtr page = IntPtr.Zero;
        IntPtr bitmap = IntPtr.Zero;
        var pinned = GCHandle.Alloc(pdfBytes.ToArray(), GCHandleType.Pinned);
        try
        {
            document = FPDF_LoadMemDocument(pinned.AddrOfPinnedObject(), pdfBytes.Length, IntPtr.Zero);
            if (document == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    "PDFium could not open this file. It may not contain a readable first page.");
            }

            if (FPDF_GetPageCount(document) < 1)
            {
                throw new InvalidOperationException("This PDF has no pages.");
            }

            page = FPDF_LoadPage(document, 0);
            if (page == IntPtr.Zero) throw new InvalidOperationException("PDFium could not open the first page.");

            var widthPt = FPDF_GetPageWidthF(page);
            var heightPt = FPDF_GetPageHeightF(page);
            if (widthPt < 1 || heightPt < 1)
            {
                throw new InvalidOperationException("The first page has no visible size.");
            }

            var scale = 144d / 72d;
            var width = Math.Max(1, (int)Math.Ceiling(widthPt * scale));
            var height = Math.Max(1, (int)Math.Ceiling(heightPt * scale));
            var edge = Math.Max(width, height);
            if (edge > MaxEdge)
            {
                var shrink = MaxEdge / (double)edge;
                width = Math.Max(1, (int)Math.Floor(width * shrink));
                height = Math.Max(1, (int)Math.Floor(height * shrink));
            }

            bitmap = FPDFBitmap_Create(width, height, 1);
            if (bitmap == IntPtr.Zero) throw new InvalidOperationException("PDFium could not allocate a page bitmap.");

            FPDFBitmap_FillRect(bitmap, 0, 0, width, height, 0xFFFFFFFF);
            FPDF_RenderPageBitmap(bitmap, page, 0, 0, width, height, 0, RenderFlags);

            var stride = FPDFBitmap_GetStride(bitmap);
            var buffer = FPDFBitmap_GetBuffer(bitmap);
            if (buffer == IntPtr.Zero || stride < width * 4)
            {
                throw new InvalidOperationException("PDFium returned an empty page.");
            }

            var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            var image = new SKBitmap(info);
            unsafe
            {
                var source = (byte*)buffer;
                var dest = (byte*)image.GetPixels();
                var rowBytes = image.RowBytes;
                var copy = Math.Min(stride, rowBytes);
                for (var y = 0; y < height; y++)
                {
                    Buffer.MemoryCopy(source + (y * stride), dest + (y * rowBytes), rowBytes, copy);
                }
            }

            return SkiaCodec.ToSKImageNoCopy(image)
                ?? throw new InvalidOperationException("The rendered page could not be stored.");
        }
        finally
        {
            if (bitmap != IntPtr.Zero) FPDFBitmap_Destroy(bitmap);
            if (page != IntPtr.Zero) FPDF_ClosePage(page);
            if (document != IntPtr.Zero) FPDF_CloseDocument(document);
            if (pinned.IsAllocated) pinned.Free();
        }
    }


    [LibraryImport("pdfium")]
    private static partial void FPDF_InitLibrary();

    [LibraryImport("pdfium")]
    private static partial IntPtr FPDF_LoadMemDocument(IntPtr data, int size, IntPtr password);

    [LibraryImport("pdfium")]
    private static partial void FPDF_CloseDocument(IntPtr document);

    [LibraryImport("pdfium")]
    private static partial int FPDF_GetPageCount(IntPtr document);

    [LibraryImport("pdfium")]
    private static partial IntPtr FPDF_LoadPage(IntPtr document, int index);

    [LibraryImport("pdfium")]
    private static partial void FPDF_ClosePage(IntPtr page);

    [LibraryImport("pdfium")]
    private static partial double FPDF_GetPageWidthF(IntPtr page);

    [LibraryImport("pdfium")]
    private static partial double FPDF_GetPageHeightF(IntPtr page);

    [LibraryImport("pdfium")]
    private static partial IntPtr FPDFBitmap_Create(int width, int height, int alpha);

    [LibraryImport("pdfium")]
    private static partial void FPDFBitmap_FillRect(IntPtr bitmap, int left, int top, int width, int height, uint color);

    [LibraryImport("pdfium")]
    private static partial void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page, int startX, int startY,
        int sizeX, int sizeY, int rotate, int flags);

    [LibraryImport("pdfium")]
    private static partial IntPtr FPDFBitmap_GetBuffer(IntPtr bitmap);

    [LibraryImport("pdfium")]
    private static partial int FPDFBitmap_GetStride(IntPtr bitmap);

    [LibraryImport("pdfium")]
    private static partial void FPDFBitmap_Destroy(IntPtr bitmap);
}
