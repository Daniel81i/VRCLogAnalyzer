using SQLite;

namespace VRCLogAnalyzer.Core.Tests
{
    public sealed class EventStoreTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "VRCLogAnalyzerTests", Guid.NewGuid().ToString("N"));
        private readonly string _logDir;
        private readonly string _dbPath;

        public EventStoreTests()
        {
            _logDir = Path.Combine(_dir, "logs");
            Directory.CreateDirectory(_logDir);
            _dbPath = Path.Combine(_dir, "test.db");
        }

        public void Dispose()
        {
            SQLiteAsyncConnection.ResetPool();
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        private void WriteLog(string name, string text) => File.WriteAllText(Path.Combine(_logDir, name), text);

        [Fact]
        public void ImportIsIdempotentAndSkipsUnchangedFiles()
        {
            WriteLog("output_log_2026-10-02_21-14-00.txt", SampleLog.Text);
            using var store = new EventStore(_dbPath);
            var importer = new LogImporter(store);

            var first = importer.Import(_logDir);
            var second = importer.Import(_logDir);

            Assert.Equal(10, first.EventsInserted);
            Assert.Equal(0, second.EventsInserted);
            Assert.Equal(1, second.FilesSkipped);
        }

        [Fact]
        public void LogsAreReReadOnceWhenParserVersionChanges()
        {
            WriteLog("output_log_2026-10-02_21-14-00.txt", SampleLog.Text);
            using var store = new EventStore(_dbPath);
            var importer = new LogImporter(store);
            importer.Import(_logDir);

            // 古い解析処理で取り込んだ状態を再現
            store.SetMeta("ParserVersion", "1");
            var reread = importer.Import(_logDir);
            var after = importer.Import(_logDir);

            Assert.Equal(0, reread.FilesSkipped);   // 1 度だけ全ファイルを読み直す
            Assert.Equal(0, reread.EventsInserted); // 登録済みの行は重複しない
            Assert.Equal(1, after.FilesSkipped);    // 次からは通常どおり差分のみ
        }

        [Fact]
        public void AppendedLinesAreImportedIncrementally()
        {
            string path = Path.Combine(_logDir, "output_log_2026-10-02_21-14-00.txt");
            WriteLog(Path.GetFileName(path), SampleLog.Text);
            using var store = new EventStore(_dbPath);
            var importer = new LogImporter(store);
            importer.Import(_logDir);

            File.AppendAllText(path, $"\n2026.10.02 21:16:00 Debug      -  [Behaviour] OnPlayerJoined Newcomer ({SampleLog.FriendId})");
            var result = importer.Import(_logDir);

            Assert.Equal(1, result.EventsInserted);
            var newcomer = store.Query(new EventFilter { PlayerName = "newcomer" }, 100).Single();
            Assert.Equal(SampleLog.World2, newcomer.WorldId);
        }

        [Fact]
        public void FiltersByTypeNameKeywordDateAndSelf()
        {
            WriteLog("output_log_2026-10-02_21-14-00.txt", SampleLog.Text);
            using var store = new EventStore(_dbPath);
            new LogImporter(store).Import(_logDir);

            Assert.Equal(2, store.Query(new EventFilter { Types = new HashSet<LogEventType> { LogEventType.WorldEnter } }, 100).Count);
            Assert.Equal(2, store.Query(new EventFilter { Types = new HashSet<LogEventType> { LogEventType.PlayerJoin }, PlayerName = "" }, 100).Count);
            Assert.Single(store.Query(new EventFilter { Types = new HashSet<LogEventType> { LogEventType.PlayerJoin }, ExcludeSelf = true }, 100));
            Assert.Single(store.Query(new EventFilter { Keyword = "example.com" }, 100));
            Assert.Single(store.Query(new EventFilter { Keyword = "udonvmexception" }, 100));
            Assert.Empty(store.Query(new EventFilter { From = new DateTime(2026, 10, 3) }, 100));
            Assert.Equal(10, store.Query(new EventFilter { From = new DateTime(2026, 10, 2), To = new DateTime(2026, 10, 2) }, 100).Count);
            Assert.Empty(store.Query(new EventFilter { Types = new HashSet<LogEventType>() }, 100));
        }

        /// <summary>
        /// OS の地域設定に関係なく日付で検索できること（yui0471 氏の fork で修正された不具合と同種）。
        /// th-TH は仏暦、fa-IR はペルシャ暦、ar-SA はヒジュラ暦のため、カルチャ依存で書式化すると年がずれる。
        /// </summary>
        [Theory]
        [InlineData("ja-JP")]
        [InlineData("en-US")]
        [InlineData("de-DE")]
        [InlineData("th-TH")]
        [InlineData("fa-IR")]
        [InlineData("ar-SA")]
        public void DateFilterIsIndependentOfCulture(string cultureName)
        {
            WriteLog("output_log_2026-10-02_21-14-00.txt", SampleLog.Text);
            using var store = new EventStore(_dbPath);
            new LogImporter(store).Import(_logDir);

            var saved = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo(cultureName);
                var day = new DateTime(2026, 10, 2);
                Assert.Equal(10, store.Query(new EventFilter { From = day, To = day }, 100).Count);
                Assert.Empty(store.Query(new EventFilter { From = day.AddDays(1) }, 100));
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = saved;
            }
        }

        [Fact]
        public void OpenEndedDateRangesWork()
        {
            WriteLog("output_log_2026-10-02_21-14-00.txt", SampleLog.Text);
            using var store = new EventStore(_dbPath);
            new LogImporter(store).Import(_logDir);

            // 終了日だけ・開始日だけ・両方未指定のいずれも正しく検索できること
            Assert.Equal(10, store.Query(new EventFilter { From = new DateTime(2026, 10, 2), To = null }, 100).Count);
            Assert.Equal(10, store.Query(new EventFilter { From = null, To = new DateTime(2026, 10, 2) }, 100).Count);
            Assert.Empty(store.Query(new EventFilter { From = null, To = new DateTime(2026, 10, 1) }, 100));
            Assert.Equal(10, store.Query(new EventFilter(), 100).Count);
        }

        [Fact]
        public void LikeWildcardsAreTreatedLiterally()
        {
            WriteLog("output_log_2026-10-02_21-14-00.txt", SampleLog.Text);
            using var store = new EventStore(_dbPath);
            new LogImporter(store).Import(_logDir);

            var types = new HashSet<LogEventType> { LogEventType.WorldEnter };
            Assert.Single(store.Query(new EventFilter { Types = types, WorldName = "d_W" }, 100));
            Assert.Single(store.Query(new EventFilter { Types = types, WorldName = "100%" }, 100));
            Assert.Empty(store.Query(new EventFilter { Types = types, WorldName = "Second%World" }, 100));
        }

        [Fact]
        public void QueryReturnsLimitPlusOneToSignalTruncation()
        {
            WriteLog("output_log_2026-10-02_21-14-00.txt", SampleLog.Text);
            using var store = new EventStore(_dbPath);
            new LogImporter(store).Import(_logDir);

            Assert.Equal(4, store.Query(new EventFilter(), 3).Count);
        }

        [Fact]
        public void LegacyTablesAreMigratedOnceOnlyBeforeOldestLog()
        {
            using (var legacy = new SQLiteConnection(_dbPath))
            {
                legacy.Execute("CREATE TABLE UserEncounterHistory (Id integer primary key autoincrement not null, Timestamp varchar, DisplayName varchar, WorldName varchar, WorldVisitTimestamp varchar, Bio varchar)");
                legacy.Execute("CREATE TABLE WorldVisitHistory (Id integer primary key autoincrement not null, WorldName varchar, WorldVisitTimestamp varchar, WorldId varchar, AuthorName varchar, AuthorId varchar, Description varchar, ImageUrl varchar, Url varchar, RawJson varchar)");
                legacy.Execute("INSERT INTO WorldVisitHistory (WorldName, WorldVisitTimestamp, WorldId) VALUES ('Old World', '2026.01.18 21:25:46', '')");
                legacy.Execute("INSERT INTO UserEncounterHistory (Timestamp, DisplayName, WorldName, WorldVisitTimestamp, Bio) VALUES ('2026.01.18 21:26:01', 'OldFriend', 'Old World', '2026.01.18 21:25:46', '')");
                // ログが残っている期間と重複する旧データは移行しない（ログから取り込まれるため）
                legacy.Execute("INSERT INTO UserEncounterHistory (Timestamp, DisplayName, WorldName, WorldVisitTimestamp, Bio) VALUES ('2026.10.02 21:14:31', 'Friend (Away)', 'x', '2026.10.02 21:14:10', '')");
            }
            WriteLog("output_log_2026-10-02_21-14-00.txt", SampleLog.Text);

            using var store = new EventStore(_dbPath);
            var importer = new LogImporter(store);
            var first = importer.Import(_logDir);
            var second = importer.Import(_logDir);

            Assert.Equal(2, first.LegacyMigrated);
            Assert.Equal(0, second.LegacyMigrated);
            var old = store.Query(new EventFilter { To = new DateTime(2026, 1, 31) }, 100);
            Assert.Equal(new[] { LogEventType.WorldEnter, LogEventType.PlayerJoin }, old.Select(e => e.EventType));
            Assert.Equal("2026-01-18 21:25:46", old[1].VisitTimestamp);
            Assert.True(store.TableExists("UserEncounterHistory"));
        }

        [Fact]
        public void MissingLogDirectoryDoesNotThrow()
        {
            using var store = new EventStore(_dbPath);
            var result = new LogImporter(store).Import(Path.Combine(_dir, "nope"));
            Assert.Equal(0, result.FilesScanned);
        }
    }
}
