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
    /// Reads the first page's size. A zero MediaBox or CropBox falls back to the other boxes,
    /// then to US Letter. Never throws for a missing size.
    /// </summary>
    public static PdfPageSize.Result MeasureFirstPage(ReadOnlyMemory<byte> pdfBytes)
    {
        RejectEmptyOrHuge(pdfBytes);
        lock (_gate)
        {
            EnsureLoaded();
            return WithFirstPage(pdfBytes.Span, static page =>
            {
                var rotation = ReadRotation(page);
                return PdfPageSize.Resolve(
                    FPDF_GetPageWidthF(page),
                    FPDF_GetPageHeightF(page),
                    rotation,
                    ReadBox(page, FPDFPage_GetMediaBox),
                    ReadBox(page, FPDFPage_GetCropBox),
                    ReadBox(page, FPDFPage_GetBleedBox),
                    ReadBox(page, FPDFPage_GetTrimBox),
                    ReadBox(page, FPDFPage_GetArtBox));
            });
        }
    }


    /// <summary>
    /// Renders the first page. The caller owns the returned image.
    /// </summary>
    public static SKImage RenderFirstPage(ReadOnlyMemory<byte> pdfBytes)
    {
        RejectEmptyOrHuge(pdfBytes);

        lock (_gate)
        {
            EnsureLoaded();
            return WithFirstPage(pdfBytes.Span, RenderMeasuredPage);
        }
    }


    private static void RejectEmptyOrHuge(ReadOnlyMemory<byte> pdfBytes)
    {
        if (pdfBytes.Length == 0) throw new InvalidOperationException("The file has no PDF content to render.");
        if (pdfBytes.Length > MaxFileBytes)
        {
            throw new InvalidOperationException("This file is too large to render with the built-in PDF engine.");
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


    private static T WithFirstPage<T>(ReadOnlySpan<byte> pdfBytes, Func<IntPtr, T> usePage)
    {
        IntPtr document = IntPtr.Zero;
        IntPtr page = IntPtr.Zero;
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

            return usePage(page);
        }
        finally
        {
            if (page != IntPtr.Zero) FPDF_ClosePage(page);
            if (document != IntPtr.Zero) FPDF_CloseDocument(document);
            if (pinned.IsAllocated) pinned.Free();
        }
    }


    private static SKImage RenderMeasuredPage(IntPtr page)
    {
        var rotation = ReadRotation(page);
        var size = PdfPageSize.Resolve(
            FPDF_GetPageWidthF(page),
            FPDF_GetPageHeightF(page),
            rotation,
            ReadBox(page, FPDFPage_GetMediaBox),
            ReadBox(page, FPDFPage_GetCropBox),
            ReadBox(page, FPDFPage_GetBleedBox),
            ReadBox(page, FPDFPage_GetTrimBox),
            ReadBox(page, FPDFPage_GetArtBox));

        if (size.BoxToApply is { } box)
        {
            // A zero CropBox makes GetPageWidthF return 0 and clips the render. Replace it.
            TrySetBox(page, box);
            var widthPt = FPDF_GetPageWidthF(page);
            var heightPt = FPDF_GetPageHeightF(page);
            if (IsUsable(widthPt) && IsUsable(heightPt))
                size = size with { WidthPt = widthPt, HeightPt = heightPt };
        }

        var scale = 144d / 72d;
        var width = Math.Max(1, (int)Math.Ceiling(size.WidthPt * scale));
        var height = Math.Max(1, (int)Math.Ceiling(size.HeightPt * scale));
        var edge = Math.Max(width, height);
        if (edge > MaxEdge)
        {
            var shrink = MaxEdge / (double)edge;
            width = Math.Max(1, (int)Math.Floor(width * shrink));
            height = Math.Max(1, (int)Math.Floor(height * shrink));
        }

        var bitmap = FPDFBitmap_Create(width, height, 1);
        if (bitmap == IntPtr.Zero) throw new InvalidOperationException("PDFium could not allocate a page bitmap.");

        try
        {
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
            FPDFBitmap_Destroy(bitmap);
        }
    }


    private static int ReadRotation(IntPtr page)
    {
        try { return FPDFPage_GetRotation(page); }
        catch (EntryPointNotFoundException) { return 0; }
    }


    private static PdfPageBox? ReadBox(IntPtr page, BoxReader read)
    {
        try
        {
            if (read(page, out var left, out var bottom, out var right, out var top) == 0) return null;
            return new PdfPageBox(left, bottom, right, top);
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
    }


    private static void TrySetBox(IntPtr page, PdfPageBox box)
    {
        try
        {
            _ = FPDFPage_SetMediaBox(page, box.Left, box.Bottom, box.Right, box.Top);
            _ = FPDFPage_SetCropBox(page, box.Left, box.Bottom, box.Right, box.Top);
        }
        catch (EntryPointNotFoundException) { }
    }


    private static bool IsUsable(double points) => double.IsFinite(points) && points >= 1;


    private delegate int BoxReader(IntPtr page, out float left, out float bottom, out float right, out float top);


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
    private static partial int FPDFPage_GetRotation(IntPtr page);

    [LibraryImport("pdfium")]
    private static partial int FPDFPage_GetMediaBox(IntPtr page, out float left, out float bottom, out float right, out float top);

    [LibraryImport("pdfium")]
    private static partial int FPDFPage_GetCropBox(IntPtr page, out float left, out float bottom, out float right, out float top);

    [LibraryImport("pdfium")]
    private static partial int FPDFPage_GetBleedBox(IntPtr page, out float left, out float bottom, out float right, out float top);

    [LibraryImport("pdfium")]
    private static partial int FPDFPage_GetTrimBox(IntPtr page, out float left, out float bottom, out float right, out float top);

    [LibraryImport("pdfium")]
    private static partial int FPDFPage_GetArtBox(IntPtr page, out float left, out float bottom, out float right, out float top);

    [LibraryImport("pdfium")]
    private static partial int FPDFPage_SetMediaBox(IntPtr page, float left, float bottom, float right, float top);

    [LibraryImport("pdfium")]
    private static partial int FPDFPage_SetCropBox(IntPtr page, float left, float bottom, float right, float top);

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
