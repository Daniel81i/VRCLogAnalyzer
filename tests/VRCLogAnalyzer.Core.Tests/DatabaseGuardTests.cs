using SQLite;

namespace VRCLogAnalyzer.Core.Tests
{
    public sealed class DatabaseGuardTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "VRCLogAnalyzerTests", Guid.NewGuid().ToString("N"));
        private readonly string _dbPath;

        public DatabaseGuardTests()
        {
            Directory.CreateDirectory(_dir);
            _dbPath = Path.Combine(_dir, "VRCLogAnalyzer.db");
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        private string[] Backups() => Directory.GetFiles(_dir, "VRCLogAnalyzer.db.backup-*");

        private void CreateLegacyDb()
        {
            using var c = new SQLiteConnection(_dbPath);
            c.Execute("CREATE TABLE UserEncounterHistory (Id integer primary key autoincrement not null, Timestamp varchar, DisplayName varchar, WorldName varchar, WorldVisitTimestamp varchar, Bio varchar)");
            c.Execute("INSERT INTO UserEncounterHistory (Timestamp, DisplayName, WorldName, WorldVisitTimestamp, Bio) VALUES ('2026.01.18 21:26:01', 'OldFriend', 'Old World', '2026.01.18 21:25:46', '')");
        }

        [Fact]
        public void NewDatabaseIsCreatedWithoutBackupOrNotice()
        {
            Assert.Equal(DbState.NotFound, DatabaseGuard.Inspect(_dbPath).State);

            using (var store = new EventStore(_dbPath))
            {
                Assert.Null(store.BackupPath);
                Assert.Null(store.TakePendingBackupNotice());
            }
            Assert.Empty(Backups());
            Assert.Equal(DbState.Current, DatabaseGuard.Inspect(_dbPath).State);
            Assert.Equal(DatabaseGuard.CurrentSchemaVersion, DatabaseGuard.Inspect(_dbPath).SchemaVersion);
        }

        [Fact]
        public void LegacyDatabaseIsBackedUpOnceAndNoticeIsShownOnce()
        {
            CreateLegacyDb();
            byte[] original = File.ReadAllBytes(_dbPath);
            Assert.Equal(DbState.NeedsUpgrade, DatabaseGuard.Inspect(_dbPath).State);

            using (var store = new EventStore(_dbPath))
            {
                Assert.NotNull(store.BackupPath);
                Assert.Contains(".backup-v0-", store.BackupPath);
            }
            // バックアップは移行前の内容と完全に一致する
            Assert.Equal(original, File.ReadAllBytes(Assert.Single(Backups())));

            using (var store = new EventStore(_dbPath))
            {
                // 2 回目以降はバックアップを作らない
                Assert.Null(store.BackupPath);
                // 通知は 1 度だけ
                Assert.NotNull(store.TakePendingBackupNotice());
                Assert.Null(store.TakePendingBackupNotice());
            }
            Assert.Single(Backups());
            Assert.Equal(DbState.Current, DatabaseGuard.Inspect(_dbPath).State);
        }

        /// <summary>構造バージョン 1（NotificationId 列が無い LogEvent）の DB を作る。バージョン記録の有無を選べる</summary>
        private void CreateSchemaV1Db(bool withVersion)
        {
            using var c = new SQLiteConnection(_dbPath);
            c.Execute("CREATE TABLE LogEvent (Id integer primary key autoincrement not null, Timestamp varchar, EventType integer, VisitTimestamp varchar, WorldName varchar, WorldId varchar, InstanceId varchar, PlayerName varchar, UserId varchar, IsSelf integer, Detail varchar, SourceFile varchar, LineNumber integer)");
            c.Execute("CREATE UNIQUE INDEX UX_LogEvent_Source ON LogEvent (SourceFile, LineNumber)");
            c.Execute("INSERT INTO LogEvent (Timestamp, EventType, VisitTimestamp, WorldName, WorldId, InstanceId, PlayerName, UserId, IsSelf, Detail, SourceFile, LineNumber) VALUES ('2026-10-02 21:14:10', 1, '2026-10-02 21:14:10', 'W', '', '', '', '', 0, '', 'output_log_x.txt', 5)");
            if (withVersion)
            {
                c.Execute("CREATE TABLE AppMeta (Key varchar primary key not null, Value varchar)");
                c.Execute("INSERT INTO AppMeta (Key, Value) VALUES ('SchemaVersion', '1')");
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)] // バージョン記録の無い v2.0.0 開発版の DB
        public void SchemaV1IsBackedUpAndUpgradedToCurrent(bool withVersion)
        {
            CreateSchemaV1Db(withVersion);
            byte[] original = File.ReadAllBytes(_dbPath);
            var before = DatabaseGuard.Inspect(_dbPath);
            Assert.Equal(DbState.NeedsUpgrade, before.State);
            Assert.Equal(1, before.SchemaVersion);

            using (var store = new EventStore(_dbPath))
            {
                Assert.Contains(".backup-v1-", store.BackupPath);
                // 既存データは残り、新しい列が使える
                var old = store.Query(new EventFilter(), 10).Single();
                Assert.Equal("W", old.WorldName);
                Assert.Null(old.NotificationId);
            }
            Assert.Equal(original, File.ReadAllBytes(Assert.Single(Backups())));
            Assert.Equal(DbState.Current, DatabaseGuard.Inspect(_dbPath).State);
            Assert.Equal(DatabaseGuard.CurrentSchemaVersion, DatabaseGuard.Inspect(_dbPath).SchemaVersion);
        }

        [Fact]
        public void NewerDatabaseIsRejectedWithoutWriting()
        {
            using (var store = new EventStore(_dbPath))
            {
                store.SetMeta(DatabaseGuard.SchemaVersionKey, (DatabaseGuard.CurrentSchemaVersion + 1).ToString());
            }
            byte[] before = File.ReadAllBytes(_dbPath);

            var ex = Assert.Throws<UnsupportedDatabaseException>(() => new EventStore(_dbPath));
            Assert.Equal(DbState.Newer, ex.Inspection.State);
            Assert.Equal(before, File.ReadAllBytes(_dbPath));
            Assert.Empty(Backups());
        }

        [Theory]
        [InlineData("this is not a database")]
        [InlineData("SQLite format 3\0 but broken")]
        public void UnrecognizedFileIsRejectedWithoutWriting(string content)
        {
            File.WriteAllText(_dbPath, content);
            byte[] before = File.ReadAllBytes(_dbPath);

            var ex = Assert.Throws<UnsupportedDatabaseException>(() => new EventStore(_dbPath));
            Assert.Equal(DbState.Unrecognized, ex.Inspection.State);
            Assert.Equal(before, File.ReadAllBytes(_dbPath));
        }

        [Fact]
        public void OtherApplicationsDatabaseIsRejected()
        {
            using (var c = new SQLiteConnection(_dbPath))
            {
                c.Execute("CREATE TABLE Customers (Id integer primary key)");
            }
            var ex = Assert.Throws<UnsupportedDatabaseException>(() => new EventStore(_dbPath));
            Assert.Equal(DbState.Unrecognized, ex.Inspection.State);
        }
    }
}
