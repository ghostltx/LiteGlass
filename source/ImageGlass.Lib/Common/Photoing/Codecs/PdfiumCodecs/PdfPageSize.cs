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

namespace ImageGlass.Common.Photoing;


/// <summary>
/// One PDF page box in unrotated user space (left, bottom, right, top).
/// </summary>
public readonly record struct PdfPageBox(float Left, float Bottom, float Right, float Top)
{
    public double Width => Math.Abs(Right - Left);

    public double Height => Math.Abs(Top - Bottom);

    /// <summary>
    /// A box PDFium can render. A zero CropBox or MediaBox is not one.
    /// </summary>
    public bool HasArea =>
        double.IsFinite(Width) && double.IsFinite(Height) && Width >= 1 && Height >= 1;


    /// <summary>
    /// Orders the corners so right is greater than left and top is greater than bottom.
    /// </summary>
    public PdfPageBox Normalized => new(
        Math.Min(Left, Right),
        Math.Min(Bottom, Top),
        Math.Max(Left, Right),
        Math.Max(Bottom, Top));
}


/// <summary>
/// Chooses a page size when <c>FPDF_GetPageWidthF</c> / <c>HeightF</c> are missing or zero.
/// No native code: the PDFium caller supplies the boxes it read.
/// </summary>
public static class PdfPageSize
{
    /// <summary>
    /// US Letter, used only when every box is missing or empty.
    /// </summary>
    public const double LetterWidthPt = 612;

    public const double LetterHeightPt = 792;


    /// <summary>
    /// The size to render, and the unrotated box to write back when the reported size was unusable.
    /// </summary>
    public readonly record struct Result(
        double WidthPt,
        double HeightPt,
        bool UsedDefault,
        PdfPageBox? BoxToApply);


    /// <summary>
    /// <paramref name="rotation"/> is PDFium's 0/1/2/3 (0, 90, 180, 270 degrees clockwise).
    /// A usable reported size already includes that rotation. Box sizes do not, so those are swapped.
    /// </summary>
    public static Result Resolve(
        double reportedWidthPt,
        double reportedHeightPt,
        int rotation,
        PdfPageBox? media,
        PdfPageBox? crop,
        PdfPageBox? bleed,
        PdfPageBox? trim,
        PdfPageBox? art)
    {
        if (IsUsable(reportedWidthPt) && IsUsable(reportedHeightPt))
            return new Result(reportedWidthPt, reportedHeightPt, false, null);

        foreach (var candidate in new[] { media, crop, bleed, trim, art })
        {
            if (candidate is not { HasArea: true } box) continue;

            var normalized = box.Normalized;
            var (width, height) = ApplyRotation(normalized.Width, normalized.Height, rotation);
            return new Result(width, height, false, normalized);
        }

        var (letterWidth, letterHeight) = ApplyRotation(LetterWidthPt, LetterHeightPt, rotation);
        return new Result(
            letterWidth,
            letterHeight,
            true,
            new PdfPageBox(0, 0, (float)LetterWidthPt, (float)LetterHeightPt));
    }


    /// <summary>
    /// Odd quarter-turns swap the axes. Values outside 0..3 still swap on the odd ones.
    /// </summary>
    public static bool RotationSwapsAxes(int rotation) => (rotation & 1) == 1;


    private static (double Width, double Height) ApplyRotation(double width, double height, int rotation) =>
        RotationSwapsAxes(rotation) ? (height, width) : (width, height);


    private static bool IsUsable(double points) => double.IsFinite(points) && points >= 1;
}
