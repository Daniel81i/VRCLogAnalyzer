using System.Globalization;
using System.Text;
using NLog;
using SQLite;

namespace VRCLogAnalyzer.Core
{
    public enum DbState
    {
        /// <summary>ファイルが無い（または空）。新規作成してよい</summary>
        NotFound,
        /// <summary>このバージョンの構造。そのまま使える</summary>
        Current,
        /// <summary>旧バージョン（sechiro 版）や古い構造。バックアップしてから移行する</summary>
        NeedsUpgrade,
        /// <summary>より新しいバージョンのアプリで作られた。壊さないよう開かない</summary>
        Newer,
        /// <summary>VRCLogAnalyzer の DB として認識できない。壊さないよう開かない</summary>
        Unrecognized,
    }

    public sealed record DbInspection(DbState State, int? SchemaVersion, string Reason);

    /// <summary>開くと壊すおそれのある DB を開こうとしたときの例外</summary>
    public sealed class UnsupportedDatabaseException : Exception
    {
        public DbInspection Inspection { get; }
        public string DatabasePath { get; }

        public UnsupportedDatabaseException(string databasePath, DbInspection inspection)
            : base($"Unsupported database ({inspection.State}, version={inspection.SchemaVersion}): {inspection.Reason}: {databasePath}")
        {
            DatabasePath = databasePath;
            Inspection = inspection;
        }
    }

    /// <summary>
    /// DB を開く前の安全確認。中身を一切書き換えずに状態を判定し、移行が必要なら移行前にバックアップを取る。
    /// </summary>
    public static class DatabaseGuard
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// DB の構造バージョン。LogEvent などのテーブル構造を変更したら 1 つ上げ、EventStore に移行処理を追加すること。
        /// 0 = 旧バージョン（sechiro 版）
        /// 1 = v2.0.0 開発版（LogEvent）
        /// 2 = インバイト対応（LogEvent.NotificationId 列と一意インデックスを追加）
        /// </summary>
        public const int CurrentSchemaVersion = 2;
        public const string SchemaVersionKey = "SchemaVersion";
        /// <summary>移行時に作ったバックアップのパス。次に画面を開いたときに 1 度だけ通知して削除する</summary>
        public const string PendingBackupNoticeKey = "PendingBackupNotice";

        private static readonly byte[] SqliteHeader = Encoding.ASCII.GetBytes("SQLite format 3\0");

        public static DbInspection Inspect(string path)
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length == 0)
            {
                return new DbInspection(DbState.NotFound, null, "file not found");
            }
            if (!HasSqliteHeader(path))
            {
                return new DbInspection(DbState.Unrecognized, null, "not a SQLite database");
            }

            try
            {
                using var conn = new SQLiteConnection(new SQLiteConnectionString(path, SQLiteOpenFlags.ReadOnly, true));
                var tables = conn.QueryScalars<string>("SELECT name FROM sqlite_master WHERE type = 'table'").ToHashSet(StringComparer.OrdinalIgnoreCase);

                if (tables.Contains(nameof(LogEvent)))
                {
                    int? version = null;
                    if (tables.Contains(nameof(AppMeta)))
                    {
                        string? v = conn.ExecuteScalar<string?>("SELECT Value FROM AppMeta WHERE Key = ?", SchemaVersionKey);
                        if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                        {
                            version = parsed;
                        }
                    }
                    // バージョン記録が無いものは v2.0.0 の開発版で作られた DB（構造バージョン 1 として扱う）
                    int effective = version ?? 1;
                    if (effective > CurrentSchemaVersion)
                    {
                        return new DbInspection(DbState.Newer, effective, "created by a newer version");
                    }
                    return effective < CurrentSchemaVersion
                        ? new DbInspection(DbState.NeedsUpgrade, effective, "older schema")
                        : new DbInspection(DbState.Current, effective, "ok");
                }
                if (tables.Contains("UserEncounterHistory") || tables.Contains("WorldVisitHistory"))
                {
                    return new DbInspection(DbState.NeedsUpgrade, 0, "legacy (v1.x) database");
                }
                if (tables.Count == 0)
                {
                    return new DbInspection(DbState.NotFound, null, "empty database");
                }
                return new DbInspection(DbState.Unrecognized, null, "unknown tables: " + string.Join(", ", tables.Take(5)));
            }
            catch (SQLiteException ex)
            {
                logger.Warn(ex, "Failed to inspect database: {0}", path);
                return new DbInspection(DbState.Unrecognized, null, "cannot be read: " + ex.Message);
            }
        }

        /// <summary>
        /// DB を開く準備をする。開けない DB なら例外、移行が必要ならバックアップを作成してそのパスを返す。
        /// </summary>
        public static (DbInspection Inspection, string? BackupPath) Prepare(string path)
        {
            var inspection = Inspect(path);
            switch (inspection.State)
            {
                case DbState.Newer:
                case DbState.Unrecognized:
                    throw new UnsupportedDatabaseException(path, inspection);
                case DbState.NeedsUpgrade:
                    string backup = Backup(path, inspection.SchemaVersion ?? 0);
                    logger.Info("Backed up database before migration (schema {0} -> {1}): {2}", inspection.SchemaVersion, CurrentSchemaVersion, backup);
                    return (inspection, backup);
                default:
                    return (inspection, null);
            }
        }

        /// <summary>同じフォルダに「VRCLogAnalyzer.db.backup-v{旧バージョン}-{日時}」としてコピーする</summary>
        internal static string Backup(string path, int fromVersion)
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string backup = $"{path}.backup-v{fromVersion}-{stamp}";
            for (int i = 2; File.Exists(backup); i++)
            {
                backup = $"{path}.backup-v{fromVersion}-{stamp}-{i}";
            }
            File.Copy(path, backup);
            // 書き込み途中のデータが WAL に残っている場合に備えて一緒にコピーする
            foreach (string suffix in new[] { "-wal", "-shm" })
            {
                if (File.Exists(path + suffix))
                {
                    File.Copy(path + suffix, backup + suffix);
                }
            }
            return backup;
        }

        private static bool HasSqliteHeader(string path)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var buf = new byte[SqliteHeader.Length];
                return fs.Read(buf, 0, buf.Length) == buf.Length && buf.AsSpan().SequenceEqual(SqliteHeader);
            }
            catch (IOException)
            {
                // ロック中などで読めない場合は SQLite 側の判定に任せる
                return true;
            }
        }
    }
}
