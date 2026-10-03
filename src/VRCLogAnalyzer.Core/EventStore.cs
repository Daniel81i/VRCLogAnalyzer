using System.Text;
using SQLite;

namespace VRCLogAnalyzer.Core
{
    /// <summary>検索条件。null / 空文字の項目は条件に含めない。</summary>
    public sealed class EventFilter
    {
        public DateTime? From { get; set; }
        /// <summary>この日の 23:59:59 までを含む</summary>
        public DateTime? To { get; set; }
        public ISet<LogEventType> Types { get; set; } = new HashSet<LogEventType>(Enum.GetValues<LogEventType>());
        public string PlayerName { get; set; } = "";
        public string WorldName { get; set; } = "";
        /// <summary>ワールド名・ユーザー名・ID・詳細（動画 URL やエラー文）を横断する部分一致</summary>
        public string Keyword { get; set; } = "";
        public bool ExcludeSelf { get; set; }
    }

    /// <summary>
    /// イベントを格納する SQLite データベース。
    /// 旧バージョンの UserEncounterHistory / WorldVisitHistory テーブルは削除せずに残す。
    /// </summary>
    public sealed class EventStore : IDisposable
    {
        public const string LegacyMigratedKey = "LegacyMigrated";
        private const string LikeEscape = "\\";

        private readonly SQLiteConnection _conn;

        public string DatabasePath { get; }

        /// <summary>今回開いたときに移行前のバックアップを作った場合、そのパス</summary>
        public string? BackupPath { get; }

        /// <summary>
        /// DB を開く。開く前に <see cref="DatabaseGuard"/> で状態を確認し、
        /// 新しいバージョンの DB や認識できないファイルなら <see cref="UnsupportedDatabaseException"/> を投げて一切書き込まない。
        /// 移行が必要な DB はバックアップを作ってから移行する。
        /// </summary>
        public EventStore(string databasePath)
        {
            DatabasePath = databasePath;
            var (_, backupPath) = DatabaseGuard.Prepare(databasePath);
            BackupPath = backupPath;

            string? dir = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            _conn = new SQLiteConnection(databasePath);
            // 別プロセス（タスクスケジューラの /analyze）と同時に開かれても待てるようにする
            _conn.BusyTimeout = TimeSpan.FromSeconds(30);
            _conn.CreateTable<LogEvent>();
            _conn.CreateTable<ProcessedFile>();
            _conn.CreateTable<AppMeta>();

            if (GetMeta(DatabaseGuard.SchemaVersionKey) != DatabaseGuard.CurrentSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture))
            {
                // 構造を変えたバージョンを追加したら、ここで旧バージョンからの移行処理を行う。
                // 1 → 2: NotificationId 列と一意インデックスの追加。CreateTable が既存テーブルに列とインデックスを追加済み
                SetMeta(DatabaseGuard.SchemaVersionKey, DatabaseGuard.CurrentSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            if (backupPath != null)
            {
                SetMeta(DatabaseGuard.PendingBackupNoticeKey, backupPath);
            }
        }

        /// <summary>移行時のバックアップの通知が未表示なら、そのパスを返して通知済みにする</summary>
        public string? TakePendingBackupNotice()
        {
            string? path = GetMeta(DatabaseGuard.PendingBackupNoticeKey);
            if (path != null)
            {
                _conn.Delete<AppMeta>(DatabaseGuard.PendingBackupNoticeKey);
            }
            return path;
        }

        internal SQLiteConnection Connection => _conn;

        public bool IsEmpty => _conn.ExecuteScalar<int>("SELECT EXISTS(SELECT 1 FROM LogEvent)") == 0;

        public bool IsUpToDate(FileInfo file)
        {
            var pf = _conn.Find<ProcessedFile>(file.Name);
            return pf != null && pf.Length == file.Length && pf.LastWriteTimeUtcTicks == file.LastWriteTimeUtc.Ticks;
        }

        /// <summary>1 ファイル分のイベントを 1 トランザクションで登録する。既に登録済みの行（同一ファイル・同一行番号）は無視する。</summary>
        public int InsertFileEvents(FileInfo file, IEnumerable<LogEvent> events)
        {
            int inserted = 0;
            _conn.RunInTransaction(() =>
            {
                foreach (var e in events)
                {
                    inserted += _conn.Insert(e, "OR IGNORE");
                }
                _conn.InsertOrReplace(new ProcessedFile
                {
                    FileName = file.Name,
                    Length = file.Length,
                    LastWriteTimeUtcTicks = file.LastWriteTimeUtc.Ticks,
                });
            });
            return inserted;
        }

        public int InsertEvents(IEnumerable<LogEvent> events)
        {
            int inserted = 0;
            _conn.RunInTransaction(() =>
            {
                foreach (var e in events)
                {
                    inserted += _conn.Insert(e, "OR IGNORE");
                }
            });
            return inserted;
        }

        /// <summary>ログ由来（旧 DB からの移行分を除く）の最古のイベント日時</summary>
        public string? GetOldestLogTimestamp() =>
            _conn.ExecuteScalar<string?>("SELECT MIN(Timestamp) FROM LogEvent WHERE SourceFile NOT LIKE 'legacy:%'");

        public string? GetMeta(string key) => _conn.Find<AppMeta>(key)?.Value;

        /// <summary>全ログファイルを未処理扱いにする（解析処理の更新後に読み直すため）</summary>
        public void ResetProcessedFiles() => _conn.DeleteAll<ProcessedFile>();

        public void SetMeta(string key, string value) => _conn.InsertOrReplace(new AppMeta { Key = key, Value = value });

        public bool TableExists(string name) =>
            _conn.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = ?", name) > 0;

        /// <summary>条件に合うイベントを時系列順で返す。limit 件を超える場合は limit + 1 件返すので、呼び出し側で超過を判定できる。</summary>
        public List<LogEvent> Query(EventFilter filter, int limit)
        {
            var (where, args) = BuildWhere(filter);
            args.Add(limit + 1);
            return _conn.Query<LogEvent>($"SELECT * FROM LogEvent {where} ORDER BY Timestamp, Id LIMIT ?", args.ToArray());
        }

        public List<LogEvent> QueryAll(EventFilter filter)
        {
            var (where, args) = BuildWhere(filter);
            return _conn.Query<LogEvent>($"SELECT * FROM LogEvent {where} ORDER BY Timestamp, Id", args.ToArray());
        }

        internal static (string where, List<object> args) BuildWhere(EventFilter f)
        {
            var conds = new List<string>();
            var args = new List<object>();

            if (f.From is { } from)
            {
                conds.Add("Timestamp >= ?");
                args.Add(LogEvent.FormatTimestamp(from.Date));
            }
            if (f.To is { } to)
            {
                conds.Add("Timestamp <= ?");
                args.Add(LogEvent.FormatTimestamp(to.Date.AddDays(1).AddSeconds(-1)));
            }

            var types = f.Types.Select(t => (int)t).OrderBy(t => t).ToList();
            if (types.Count == 0)
            {
                conds.Add("0");
            }
            else if (types.Count < Enum.GetValues<LogEventType>().Length)
            {
                conds.Add($"EventType IN ({string.Join(",", types)})");
            }

            if (f.PlayerName.Length > 0)
            {
                conds.Add($"PlayerName LIKE ? ESCAPE '{LikeEscape}'");
                args.Add(ToLikePattern(f.PlayerName));
            }
            if (f.WorldName.Length > 0)
            {
                conds.Add($"WorldName LIKE ? ESCAPE '{LikeEscape}'");
                args.Add(ToLikePattern(f.WorldName));
            }
            if (f.Keyword.Length > 0)
            {
                var columns = new[] { "WorldName", "WorldId", "InstanceId", "PlayerName", "UserId", "Detail" };
                conds.Add("(" + string.Join(" OR ", columns.Select(c => $"{c} LIKE ? ESCAPE '{LikeEscape}'")) + ")");
                string pattern = ToLikePattern(f.Keyword);
                args.AddRange(columns.Select(_ => (object)pattern));
            }
            if (f.ExcludeSelf)
            {
                conds.Add("IsSelf = 0");
            }

            return (conds.Count == 0 ? "" : "WHERE " + string.Join(" AND ", conds), args);
        }

        /// <summary>部分一致用の LIKE パターンを作る。% _ \ は文字として扱う。</summary>
        internal static string ToLikePattern(string input)
        {
            var sb = new StringBuilder("%");
            foreach (char c in input.Trim())
            {
                if (c is '%' or '_' or '\\')
                {
                    sb.Append('\\');
                }
                sb.Append(c);
            }
            return sb.Append('%').ToString();
        }

        public void Dispose() => _conn.Dispose();
    }
}
