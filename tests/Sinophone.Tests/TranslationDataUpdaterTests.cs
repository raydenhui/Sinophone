using System.IO.Compression;
using System.Text;
using Sinophone.Data;

namespace Sinophone.Tests;

public class TranslationDataUpdaterTests
{
    [Theory]
    [InlineData(null, "abc", true)]
    [InlineData("", "abc", true)]
    [InlineData("abc", "abc", false)]
    [InlineData("abc", "def", true)]
    [InlineData("abc", null, false)]
    [InlineData("abc", "", false)]
    public void ShouldUpdate_ComparesPublishedVersionWithInstalled(
        string? installed, string? published, bool expected)
        => Assert.Equal(expected, TranslationDataUpdater.ShouldUpdate(installed, published));

    [Fact]
    public void TryValidateArtifact_AcceptsArtifactWithSheets()
    {
        var stream = ArtifactStream("""{"FormatVersion":1,"Language":"CN","Sheets":{"DefaultTalk":{}}}""");

        Assert.True(TranslationDataUpdater.TryValidateArtifact(stream, out var error));
        Assert.Empty(error);
    }

    [Fact]
    public void TryValidateArtifact_RejectsStreamThatIsNotGzip()
    {
        var stream = new MemoryStream(new byte[] { 1, 2, 3, 4 });

        Assert.False(TranslationDataUpdater.TryValidateArtifact(stream, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void TryValidateArtifact_RejectsUnsupportedFormatVersion()
    {
        var stream = ArtifactStream("""{"FormatVersion":99,"Sheets":{"DefaultTalk":{}}}""");

        Assert.False(TranslationDataUpdater.TryValidateArtifact(stream, out var error));
        Assert.Contains("99", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryValidateArtifact_RejectsArtifactWithoutSheets()
    {
        var stream = ArtifactStream("""{"FormatVersion":1,"Sheets":{}}""");

        Assert.False(TranslationDataUpdater.TryValidateArtifact(stream, out var error));
        Assert.Contains("no sheets", error, StringComparison.Ordinal);
    }

    [Fact]
    public void DataFilePath_MatchesThePublishedFileNames()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var updater = CreateUpdater(directory);

            Assert.Equal(
                Path.Combine(directory, TranslationStore.ResourceCnSuffix),
                updater.DataFilePath(ChineseVariant.Simplified));
            Assert.Equal(
                Path.Combine(directory, TranslationStore.ResourceTcSuffix),
                updater.DataFilePath(ChineseVariant.Traditional));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void OpenData_ReadsTheDownloadedFileWhenPresent()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var updater = CreateUpdater(directory);
            var path = updater.DataFilePath(ChineseVariant.Simplified);
            File.WriteAllBytes(path, [10, 20, 30]);

            using var stream = updater.OpenData(ChineseVariant.Simplified);

            Assert.NotNull(stream);
            Assert.Equal(3, (int)stream!.Length);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void NewUpdater_IsIdleAndHasNoPendingReload()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var updater = CreateUpdater(directory);

            Assert.False(updater.State.IsBusy);
            Assert.False(updater.State.HasUpdate);
            Assert.Null(updater.State.LastError);
            Assert.False(updater.ConsumeReloadRequest());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static TranslationDataUpdater CreateUpdater(string directory)
        => new(new Configuration(), directory, new FakePluginLog());

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "sinophone-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static MemoryStream ArtifactStream(string json)
    {
        var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionMode.Compress, leaveOpen: true))
        {
            gzip.Write(Encoding.UTF8.GetBytes(json));
        }

        output.Position = 0;
        return output;
    }
}
