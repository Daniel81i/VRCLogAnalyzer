using SQLite;

namespace VRCLogAnalyzer.Core
{
    public enum LogEventType
    {
        WorldEnter = 1,
        WorldLeave = 2,
        PlayerJoin = 3,
        PlayerLeave = 4,
        Video = 5,
        Error = 6,
        /// <summary>自分宛てのインバイト</summary>
        Invite = 7,
        /// <summary>自分宛てのリクエストインバイト（インバイトの要求）</summary>
        RequestInvite = 8,
    }

    /// <summary>
    /// ログから抽出した 1 イベント。全種別を 1 テーブルに格納し、種別でフィルタリングする。
    /// </summary>
    public class LogEvent
    {
        /// <summary>SQLite に保存する日時の書式。文字列比較でそのまま時系列順になる。</summary>
        public const string TimestampFormat = "yyyy-MM-dd HH:mm:ss";

        /// <summary>
        /// DateTime を DB の日時文字列にする。OS の地域設定によっては暦（仏暦・ヒジュラ暦など）や区切り文字が変わり
        /// 検索できなくなるため、必ず InvariantCulture で書式化する。
        /// </summary>
        public static string FormatTimestamp(DateTime value) =>
            value.ToString(TimestampFormat, System.Globalization.CultureInfo.InvariantCulture);

        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        [Indexed]
        public string Timestamp { get; set; } = "";

        [Indexed]
        public LogEventType EventType { get; set; }

        /// <summary>このイベントが発生したワールドに入った日時（ワールド訪問単位のグループ化に使う）</summary>
        public string VisitTimestamp { get; set; } = "";

        public string WorldName { get; set; } = "";
        public string WorldId { get; set; } = "";
        public string InstanceId { get; set; } = "";

        public string PlayerName { get; set; } = "";
        public string UserId { get; set; } = "";

        /// <summary>自分自身の入退室かどうか</summary>
        public bool IsSelf { get; set; }

        /// <summary>動画 URL やエラーメッセージなど種別ごとの詳細</summary>
        public string Detail { get; set; } = "";

        /// <summary>
        /// VRChat の通知 ID（not_...）。インバイトなど通知由来のイベントのみ。
        /// VRChat はログインのたびに過去の通知をログに出し直すため、これで重複を防ぐ（NULL は重複扱いにならない）
        /// </summary>
        [Indexed(Name = "UX_LogEvent_Notification", Unique = true)]
        public string? NotificationId { get; set; }

        /// <summary>取り込み元ログファイル名。LineNumber と組み合わせて重複取り込みを防ぐ。</summary>
        [Indexed(Name = "UX_LogEvent_Source", Order = 1, Unique = true)]
        public string SourceFile { get; set; } = "";

        [Indexed(Name = "UX_LogEvent_Source", Order = 2, Unique = true)]
        public int LineNumber { get; set; }

        [Ignore]
        public string WorldUrl => WorldId.Length > 0 ? $"https://vrchat.com/home/world/{WorldId}" : "";

        [Ignore]
        public string UserUrl => UserId.Length > 0 ? $"https://vrchat.com/home/user/{UserId}" : "";

        /// <summary>ワールド訪問単位でまとめて表示する際のグループキー（入室前のイベントは空文字。表示名は UI 側で付ける）</summary>
        [Ignore]
        public string VisitGroup => VisitTimestamp.Length == 0 ? "" : $"{VisitTimestamp}  {WorldName}";

        /// <summary>一覧表示用の 1 行要約（エラーは 1 行目のみ）</summary>
        [Ignore]
        public string DetailSummary
        {
            get
            {
                int nl = Detail.IndexOf('\n');
                return nl < 0 ? Detail : Detail[..nl];
            }
        }

        public override string ToString() => $"{Timestamp} [{EventType}] {WorldName} {PlayerName} {Detail}";
    }

    /// <summary>取り込み済みログファイル。サイズと更新日時が変わっていなければ再解析しない。</summary>
    public class ProcessedFile
    {
        [PrimaryKey]
        public string FileName { get; set; } = "";
        public long Length { get; set; }
        public long LastWriteTimeUtcTicks { get; set; }
    }

    /// <summary>スキーマバージョンや移行済みフラグなどを保持する key-value テーブル</summary>
    public class AppMeta
    {
        [PrimaryKey]
        public string Key { get; set; } = "";
        public string Value { get; set; } = "";
    }
}
