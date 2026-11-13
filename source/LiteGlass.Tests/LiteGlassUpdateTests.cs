using ImageGlass.Common.Photoing;
using Xunit;
using ImageGlass.Common.ServiceProviders.Update;
using System.Text;

namespace LiteGlass.Tests;


public class LiteGlassUpdateTests
{
    [Fact]
    public void NewerTagComparesAboveTheRunningBuild()
    {
        Assert.True(GitHubReleaseParser.CompareVersions("1.0.0.1", "v1.0.0.2") > 0);
        Assert.Equal(0, GitHubReleaseParser.CompareVersions("1.0.0.1", "v1.0.0.1"));
        Assert.True(GitHubReleaseParser.CompareVersions("1.0.0.2", "1.0.0.1") < 0);
    }


    [Fact]
    public void ReleaseJsonSelectsThePortableZipAndItsDigest()
    {
        const string json = """
            {
              "tag_name": "v1.0.0.2",
              "name": "LiteGlass 1.0.0.2",
              "body": "Notes for the portable build.",
              "html_url": "https://github.com/ghostltx/LiteGlass/releases/tag/v1.0.0.2",
              "published_at": "2026-11-13T00:00:00Z",
              "assets": [
                {
                  "name": "LiteGlass-win-x64-portable.zip",
                  "size": 1234,
                  "browser_download_url": "https://example.test/LiteGlass-win-x64-portable.zip",
                  "digest": "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
                }
              ]
            }
            """;

        var parsed = GitHubReleaseParser.Parse(json, "x64");

        Assert.Null(parsed.Error);
        Assert.Equal("1.0.0.2", parsed.Release?.Version);
        Assert.Equal("Notes for the portable build.", parsed.Release?.Description);
        Assert.True(parsed.HasInstallableZip);
        var artifact = parsed.Release!.Artifacts!["win-x64-zip"];
        Assert.Equal(1234, artifact.Size);
        Assert.StartsWith("0123456789abcdef", artifact.Sha256);
        Assert.Null(parsed.Sha256Url);
    }


    [Fact]
    public void MissingDigestPointsAtTheSha256Sidecar()
    {
        const string json = """
            {
              "tag_name": "v1.0.0.2",
              "name": "LiteGlass 1.0.0.2",
              "assets": [
                {
                  "name": "LiteGlass-win-x64-portable.zip",
                  "size": 50,
                  "browser_download_url": "https://example.test/app.zip"
                },
                {
                  "name": "LiteGlass-win-x64-portable.zip.sha256",
                  "size": 80,
                  "browser_download_url": "https://example.test/app.zip.sha256"
                }
              ]
            }
            """;

        var parsed = GitHubReleaseParser.Parse(json, "x64");
        Assert.False(parsed.HasInstallableZip);
        Assert.Equal("https://example.test/app.zip.sha256", parsed.Sha256Url);

        var withHash = parsed.WithSha256(
            GitHubReleaseParser.ParseSha256File("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa  LiteGlass-win-x64-portable.zip"),
            "win-x64-zip");
        Assert.True(withHash.HasInstallableZip);
    }


    [Fact]
    public void SizeCheckRejectsAShortDownload()
    {
        Assert.False(GitHubReleaseParser.SizeMatches(100, 99));
        Assert.True(GitHubReleaseParser.SizeMatches(100, 100));
        Assert.True(GitHubReleaseParser.SizeMatches(0, 100));
    }


    [Fact]
    public void UpdaterScriptKeepsPortableSettings()
    {
        var script = LiteGlassUpdaterScript.Build();
        Assert.Contains("igconfig.json", script);
        Assert.Contains(".igportable", script);
        Assert.Contains("LiteGlass.exe", script);
        Assert.Contains("/XD _cache _logs _update", script);
        Assert.Contains("robocopy", script);
    }
}


public class FormatSupportTests
{
    [Fact]
    public void BuiltInFormatListIncludesTheNewExtensions()
    {
        var root = FindRepoRoot();
        var text = File.ReadAllText(Path.Combine(root, "source", "ImageGlass.Lib", "Common", "Types", "Const.cs"));
        foreach (var ext in new[] { ".ai;", ".eps;", ".pdf;", ".dwg;" })
        {
            Assert.Contains(ext, text);
        }
    }


    [Fact]
    public void PdfHeaderAndEpsPreviewAreLocated()
    {
        var pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n");
        Assert.Equal(0, PdfiumLocator.FindPdfOffset(pdf));

        var ai = new byte[16];
        Encoding.ASCII.GetBytes("%PDF-").CopyTo(ai.AsSpan(8));
        Assert.Equal(8, PdfiumLocator.FindPdfOffset(ai));

        var eps = new byte[64];
        eps[0] = 0xC5; eps[1] = 0xD0; eps[2] = 0xD3; eps[3] = 0xC6;
        BitConverter.GetBytes(30).CopyTo(eps, 20);
        BitConverter.GetBytes(34).CopyTo(eps, 24);
        var preview = PdfiumLocator.ReadDosPreview(eps);
        Assert.NotNull(preview);
        Assert.Equal("tiff", preview.Value.Kind);
        Assert.Equal(30, preview.Value.Offset);
        Assert.Equal(34, preview.Value.Length);
    }


    [Fact]
    public void ZeroMediaBoxPdfDoesNotThrowAndUsesLetterSize()
    {
        // The page PDFium would measure as 0x0: MediaBox and CropBox are both empty.
        var pdf = """
            %PDF-1.4
            1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj
            2 0 obj<</Type/Pages/Count 1/Kids[3 0 R]>>endobj
            3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 0 0]/CropBox[0 0 0 0]>>endobj
            trailer<</Root 1 0 R>>
            %%EOF
            """;
        var bytes = Encoding.ASCII.GetBytes(pdf);
        Assert.Equal(0, PdfiumLocator.FindPdfOffset(bytes));
        Assert.Contains("/MediaBox[0 0 0 0]", pdf, StringComparison.Ordinal);

        var media = ReadBox(pdf, "MediaBox");
        var crop = ReadBox(pdf, "CropBox");
        Assert.False(media!.Value.HasArea);
        Assert.False(crop!.Value.HasArea);
        Assert.Null(ReadBox(pdf, "ArtBox"));

        var size = PdfPageSize.Resolve(0, 0, rotation: 0, media, crop, bleed: null, trim: null, art: null);
        Assert.True(size.UsedDefault);
        Assert.Equal(PdfPageSize.LetterWidthPt, size.WidthPt);
        Assert.Equal(PdfPageSize.LetterHeightPt, size.HeightPt);
        Assert.True(size.WidthPt >= 1 && size.HeightPt >= 1);
    }


    [Fact]
    public void MissingMediaBoxUsesArtBoxAndRotation()
    {
        var pdf = """
            %PDF-1.4
            3 0 obj<</Type/Page/Parent 2 0 R/ArtBox[10 20 410 320]>>endobj
            %PDF-1.4
            """;
        Assert.Null(ReadBox(pdf, "MediaBox"));
        var art = ReadBox(pdf, "ArtBox");
        Assert.True(art!.Value.HasArea);

        var upright = PdfPageSize.Resolve(0, 0, 0, media: null, crop: null, bleed: null, trim: null, art);
        Assert.False(upright.UsedDefault);
        Assert.Equal(400, upright.WidthPt);
        Assert.Equal(300, upright.HeightPt);

        var turned = PdfPageSize.Resolve(0, 0, rotation: 1, media: null, crop: null, bleed: null, trim: null, art);
        Assert.Equal(300, turned.WidthPt);
        Assert.Equal(400, turned.HeightPt);
    }


    [Fact]
    public void ReportedPageSizeWinsOverAZeroBox()
    {
        var zero = new PdfPageBox(0, 0, 0, 0);
        var size = PdfPageSize.Resolve(200, 100, 0, zero, zero, null, null, null);
        Assert.False(size.UsedDefault);
        Assert.Equal(200, size.WidthPt);
        Assert.Equal(100, size.HeightPt);
        Assert.Null(size.BoxToApply);
    }


    [Fact]
    public void IllustratorFileWithoutPdfSaysSo()
    {
        Assert.Contains("PDF-compatible", PdfiumLocator.AiNotPdfCompatibleMessage, StringComparison.Ordinal);
        Assert.Equal(-1, PdfiumLocator.FindPdfOffset("%!PS-Adobe-3.0"u8));
    }


    [Fact]
    public void MissingDwgToolHasAClearMessage()
    {
        Assert.Null(DwgCommand.FindTool(Path.GetTempPath()));
        Assert.Contains("dwg2SVG", DwgCommand.MissingToolMessage);
        Assert.Contains("hang", DwgCommand.TimeoutMessage, StringComparison.OrdinalIgnoreCase);
    }


    private static PdfPageBox? ReadBox(string pdf, string name)
    {
        var token = "/" + name;
        var at = pdf.IndexOf(token, StringComparison.Ordinal);
        if (at < 0) return null;

        var open = pdf.IndexOf('[', at);
        var close = pdf.IndexOf(']', open);
        var parts = pdf[(open + 1)..close].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return new PdfPageBox(
            float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
            float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
            float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture),
            float.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture));
    }


    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "README.md"))
                && Directory.Exists(Path.Combine(dir.FullName, "source")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the repository root.");
    }
}
