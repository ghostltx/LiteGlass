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
using Avalonia;
using ImageGlass.Common.Types;
using ImageMagick;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ImageGlass.Common.Photoing;


/// <summary>
/// Renders the first page of PDF, PDF-compatible AI, and EPS (PDF stream or DOS preview).
/// </summary>
public sealed class PdfiumCodecAdapter : PhDisposable, ICodec
{
    public string CodecId { get; } = "pdfium";

    public string CodecName { get; } = "PDFium";

    public int MetadataPriority { get; } = 250;

    public int DecodePriority { get; } = 250;

    public int EncodePriority { get; }

    public IReadOnlyList<string> DecodingExtensions => PdfiumLocator.Extensions;

    public bool SupportsEncoding { get; }

    public IReadOnlyList<string> EncodingExtensions => [];


    public bool CanLoadMetadata(string filePath) => PdfiumLocator.IsSupportedExtension(filePath);

    public bool CanDecode(PhotoMetadata metadata, CodecSelectionContext context) =>
        PdfiumLocator.IsSupportedExtension(metadata.FilePath);

    public bool CanEncode(string destFilePath, CodecEncodeContext context) => false;

    public bool CanEncodeMultiFrame(string destFilePath, CodecEncodeContext context) => false;

    public Task<CodecEncodeResult> EncodeAsync(CodecEncodeRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new CodecEncodeResult(false, true));

    public Task<CodecEncodeResult> EncodeMultiFrameAsync(CodecMultiFrameEncodeRequest request,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new CodecEncodeResult(false, true));


    public Task<PhotoMetadata> LoadMetadataAsync(string filePath, PhotoReadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Page size comes from the decode. Rendering here would open the file twice.
        return Task.FromResult(new PhotoMetadata(filePath)
        {
            FrameCount = 1,
            HasAlpha = true,
        });
    }


    public async Task<CodecDecodeResult> DecodeAsync(PhotoMetadata metadata, PhotoReadOptions options,
        CodecSelectionContext context, CancellationToken cancellationToken = default)
    {
        var image = await RenderAsync(metadata.FilePath, cancellationToken).ConfigureAwait(false);
        return new CodecDecodeResult
        {
            CodecId = CodecId,
            ContentKind = CodecContentKind.StaticRaster,
            Size = new Size(image.Width, image.Height),
            SingleFrame = image,
            DecodeScale = 1,
        };
    }


    private static async Task<SKImage> RenderAsync(string filePath, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return await Task.Run(() => RenderBytes(bytes, filePath), cancellationToken).ConfigureAwait(false);
    }


    private static SKImage RenderBytes(byte[] bytes, string filePath)
    {
        var span = bytes.AsSpan();
        var ext = Path.GetExtension(filePath);
        var pdfOffset = PdfiumLocator.FindPdfOffset(span);
        if (pdfOffset >= 0)
        {
            var pdf = pdfOffset == 0 ? bytes : bytes[pdfOffset..];
            return PdfiumLibrary.RenderFirstPage(pdf);
        }

        if (ext.Equals(".eps", StringComparison.OrdinalIgnoreCase)
            && PdfiumLocator.ReadDosPreview(span) is { } preview)
        {
            var chunk = bytes.AsSpan(preview.Offset, preview.Length).ToArray();
            return RenderPreview(chunk, preview.Kind);
        }

        if (ext.Equals(".ai", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "This Illustrator file has no PDF-compatible content. LiteGlass renders the PDF stream Illustrator embeds by default, not legacy PostScript.");
        }

        if (ext.Equals(".eps", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "This EPS file has no PDF stream and no TIFF or WMF preview. LiteGlass does not run PostScript.");
        }

        throw new InvalidOperationException("This file is not a PDF that PDFium can open.");
    }


    private static SKImage RenderPreview(byte[] chunk, string kind)
    {
        try
        {
            using var magick = new MagickImage(chunk);
            var image = SkiaCodec.FromMagick(magick, null, false);
            if (image is not null) return image;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"The embedded {kind.ToUpperInvariant()} preview in this EPS file could not be read. {ex.Message}");
        }

        throw new InvalidOperationException($"The embedded {kind.ToUpperInvariant()} preview in this EPS file was empty.");
    }
}
