namespace VRCLogAnalyzer.Core.Tests
{
    public class CsvAndSettingsTests
    {
        [Theory]
        [InlineData("plain", "plain")]
        [InlineData("a,b", "\"a,b\"")]
        [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
        [InlineData("line1\nline2", "\"line1\nline2\"")]
        [InlineData("=HYPERLINK(\"x\")", "\"'=HYPERLINK(\"\"x\"\")\"")]
        [InlineData("@user", "'@user")]
        public void CsvEscape(string input, string expected)
        {
            Assert.Equal(expected, CsvExporter.Escape(input));
        }

        [Fact]
        public void SettingsFallBackToDefaultsWhenFileIsMissingOrBroken()
        {
            string dir = Path.Combine(Path.GetTempPath(), "VRCLogAnalyzerTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                Assert.Equal(DbLocation.MyDocuments, AppSettings.Load(dir).DbLocation);

                File.WriteAllText(Path.Combine(dir, AppSettings.FileName), "<not xml");
                Assert.Equal(DbLocation.MyDocuments, AppSettings.Load(dir).DbLocation);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void SettingsReadLegacyFormatAndRoundTrip()
        {
            string dir = Path.Combine(Path.GetTempPath(), "VRCLogAnalyzerTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, AppSettings.FileName),
                    "<?xml version=\"1.0\" encoding=\"utf-8\" ?><configuration><appSettings><add key=\"DbPathChoice\" value=\"AppPath\" /></appSettings></configuration>");
                var s = AppSettings.Load(dir);
                Assert.Equal(DbLocation.AppPath, s.DbLocation);
                Assert.Equal(Path.Combine(dir, AppSettings.DatabaseFileName), s.DatabasePath);

                s.LogDir = @"D:\logs";
                s.DbLocation = DbLocation.MyDocuments;
                Assert.False(s.ShowDetail);
                s.Language = "en";
                s.ShowDetail = true;
                s.Save();
                var reloaded = AppSettings.Load(dir);
                Assert.Equal("en", reloaded.Language);
                Assert.True(reloaded.ShowDetail);
                Assert.Equal(@"D:\logs", reloaded.LogDir);
                Assert.Equal(DbLocation.MyDocuments, reloaded.DbLocation);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
