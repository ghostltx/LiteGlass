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
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ImageGlass.Common.Photoing;


/// <summary>
/// Best-effort AutoCAD DWG via the bundled LibreDWG <c>dwg2SVG</c> tool, then Svg.Skia.
/// A timeout stops a drawing that would otherwise hang the viewer.
/// </summary>
public sealed class DwgCodecAdapter : PhDisposable, ICodec
{
    private static readonly string[] _extensions = [".dwg"];
    private static readonly Lock _gate = new();
    private const int MaxEdge = 2048;


    public string CodecId { get; } = "libredwg";

    public string CodecName { get; } = "LibreDWG";

    public int MetadataPriority { get; } = 250;

    public int DecodePriority { get; } = 250;

    public int EncodePriority { get; }

    public IReadOnlyList<string> DecodingExtensions => _extensions;

    public bool SupportsEncoding { get; }

    public IReadOnlyList<string> EncodingExtensions => [];


    public bool CanLoadMetadata(string filePath) => IsDwg(filePath);

    public bool CanDecode(PhotoMetadata metadata, CodecSelectionContext context) => IsDwg(metadata.FilePath);

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
        return Task.FromResult(new PhotoMetadata(filePath)
        {
            FrameCount = 1,
            HasAlpha = true,
        });
    }


    public async Task<CodecDecodeResult> DecodeAsync(PhotoMetadata metadata, PhotoReadOptions options,
        CodecSelectionContext context, CancellationToken cancellationToken = default)
    {
        var image = await Task.Run(() => Render(metadata.FilePath, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        return new CodecDecodeResult
        {
            CodecId = CodecId,
            ContentKind = CodecContentKind.StaticRaster,
            Size = new Size(image.Width, image.Height),
            SingleFrame = image,
            DecodeScale = 1,
        };
    }


    private static SKImage Render(string filePath, CancellationToken cancellationToken)
    {
        var tool = DwgCommand.FindTool(BHelper.BasePath);
        if (tool is null) throw new InvalidOperationException(DwgCommand.MissingToolMessage);

        var svgPath = Path.Combine(Path.GetTempPath(), $"liteglass-{Guid.NewGuid():N}.svg");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                RunTool(tool, filePath, svgPath, cancellationToken);
            }

            if (!File.Exists(svgPath) || new FileInfo(svgPath).Length == 0)
            {
                throw new InvalidOperationException(DwgCommand.FailedMessage("LibreDWG did not write a drawing."));
            }

            using var svg = SvgCodec.LoadSvg(svgPath);
            var picture = svg.Picture ?? throw new InvalidOperationException(
                DwgCommand.FailedMessage("The converted drawing had nothing to draw."));
            return Rasterize(picture) ?? throw new InvalidOperationException(
                DwgCommand.FailedMessage("The converted drawing had an empty page."));
        }
        finally
        {
            try { if (File.Exists(svgPath)) File.Delete(svgPath); } catch { }
        }
    }


    private static void RunTool(string tool, string filePath, string svgPath, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = tool,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            WorkingDirectory = Path.GetDirectoryName(tool) ?? string.Empty,
        };
        psi.ArgumentList.Add("-o");
        psi.ArgumentList.Add(svgPath);
        psi.ArgumentList.Add(filePath);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException(DwgCommand.MissingToolMessage);
        // Drain both pipes while waiting so a chatty tool cannot fill the buffer and stall.
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var deadline = Environment.TickCount64 + (DwgCommand.TimeoutSeconds * 1000L);
        while (!process.HasExited)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (Environment.TickCount64 > deadline)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                try { process.WaitForExit(2000); } catch { }
                try { _ = stderr.GetAwaiter().GetResult(); } catch { }
                try { _ = stdout.GetAwaiter().GetResult(); } catch { }
                throw new InvalidOperationException(DwgCommand.TimeoutMessage);
            }

            process.WaitForExit(200);
        }

        process.WaitForExit();
        var detail = string.Empty;
        try { detail = stderr.GetAwaiter().GetResult(); } catch { }
        if (string.IsNullOrWhiteSpace(detail))
        {
            try { detail = stdout.GetAwaiter().GetResult(); } catch { }
        }

        if (process.ExitCode != 0 && (!File.Exists(svgPath) || new FileInfo(svgPath).Length == 0))
        {
            throw new InvalidOperationException(DwgCommand.FailedMessage(detail));
        }
    }


    private static SKImage? Rasterize(SKPicture picture)
    {
        var cull = picture.CullRect;
        var width = cull.Width;
        var height = cull.Height;
        if (width <= 0 || height <= 0) return null;

        var scale = Math.Min(MaxEdge / width, MaxEdge / height);
        var targetW = Math.Max(1, (int)Math.Ceiling(width * scale));
        var targetH = Math.Max(1, (int)Math.Ceiling(height * scale));
        var info = new SKImageInfo(targetW, targetH, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        if (surface is null) return null;

        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        if (cull.Left != 0 || cull.Top != 0) canvas.Translate(-cull.Left * scale, -cull.Top * scale);
        canvas.Scale(scale, scale);
        canvas.DrawPicture(picture);
        return surface.Snapshot();
    }


    private static bool IsDwg(string? path) =>
        path?.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase) == true;
}
