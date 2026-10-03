using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace VRCLogAnalyzer.Core
{
    /// <summary>
    /// VRChat の output_log_*.txt を 1 ファイル分解析して LogEvent を列挙する。
    /// ファイル内の文脈（現在いるワールド、自分のユーザー ID）はインスタンス内に保持するため、1 ファイルごとに new すること。
    /// </summary>
    public sealed class LogParser
    {
        /// <summary>
        /// 解析処理のバージョン。取り込む種別を増やすなど解析結果が変わる修正をしたら 1 つ上げる。
        /// 上がっていると、取り込み済みのログも次回の取り込みで 1 度だけ読み直す（登録済みの行は重複しない）。
        /// 1 = v2.0.0 開発版、2 = インバイト／リクエストインバイト対応
        /// </summary>
        public const int Version = 2;

        // 例: "2026.10.02 21:14:10 Debug      -  [Behaviour] Entering Room: World Name"
        private static readonly Regex LineHead = new(
            @"^(?<ts>\d{4}\.\d{2}\.\d{2} \d{2}:\d{2}:\d{2}) (?<level>\w+)\s+-\s+(?<msg>.*)$",
            RegexOptions.Compiled);

        private const string UserIdPattern = @"usr_[0-9a-fA-F-]+";

        private static readonly Regex EnteringRoom = new(@"^\[Behaviour\] Entering Room: (?<name>.+)$", RegexOptions.Compiled);
        private static readonly Regex JoiningWorld = new(@"^\[Behaviour\] Joining (?<wid>wrld_[0-9a-fA-F-]+):(?<inst>\S+)$", RegexOptions.Compiled);
        private static readonly Regex JoiningOrCreatingRoom = new(@"^\[Behaviour\] Joining or Creating Room: (?<name>.+)$", RegexOptions.Compiled);
        private static readonly Regex LeftRoom = new(@"^\[Behaviour\] OnLeftRoom$", RegexOptions.Compiled);
        // "OnPlayerLeftRoom" にはマッチさせないため、イベント名の直後に半角スペースを必須にしている
        private static readonly Regex PlayerJoined = new($@"^\[Behaviour\] OnPlayerJoined (?<name>.+?)(?: \((?<uid>{UserIdPattern})\))?$", RegexOptions.Compiled);
        private static readonly Regex PlayerLeft = new($@"^\[Behaviour\] OnPlayerLeft (?<name>.+?)(?: \((?<uid>{UserIdPattern})\))?$", RegexOptions.Compiled);
        private static readonly Regex UserAuthenticated = new($@"^User Authenticated: (?<name>.+?) \((?<uid>{UserIdPattern})\)$", RegexOptions.Compiled);
        private static readonly Regex VideoResolve = new(@"^\[Video Playback\] Attempting to resolve URL '(?<url>.+)'$", RegexOptions.Compiled);

        // 例: Received Notification: <Notification from username:NAME, sender user id:usr_... to usr_... of type: invite,
        //     id: not_..., created at: 10/02/2026 12:30:50 UTC, details: {{worldId=wrld_...:123~region(jp), worldName=WORLD}},
        //     type:invite, m seen:False, message: "...">
        private static readonly Regex ReceivedNotification = new(
            $@"^Received Notification: <Notification from username:(?<name>.*?), sender user id:(?<uid>{UserIdPattern})? to \S* of type: (?<type>invite|requestInvite), id: (?<nid>\S+), created at: (?<created>\d{{2}}/\d{{2}}/\d{{4}} \d{{2}}:\d{{2}}:\d{{2}}) UTC, details: \{{\{{(?<details>.*?)\}}\}}(?:.*?, message: ""(?<msg>.*?)""?>?)?\s*$",
            RegexOptions.Compiled);
        private static readonly Regex InviteDetails = new(@"worldId=(?<wid>wrld_[0-9a-fA-F-]+)(?::(?<inst>[^,\s]+))?(?:, worldName=(?<wname>.*))?", RegexOptions.Compiled);

        /// <summary>エラーのスタックトレースとして保持する継続行の上限</summary>
        internal const int MaxErrorDetailLines = 40;
        internal const int MaxErrorDetailLength = 8000;

        private readonly string _sourceFile;

        // 自分自身
        private string _localUserId = "";
        private string _localUserName = "";

        // 現在いるワールド（入室イベント確定後）
        private string _visitTimestamp = "";
        private string _worldName = "";
        private string _worldId = "";
        private string _instanceId = "";

        // 入室処理中のワールド。VRChat は Entering Room → Joining wrld_... → Joining or Creating Room の順に出力する
        private string? _pendingName;
        private string _pendingTimestamp = "";
        private string _pendingWorldId = "";
        private string _pendingInstanceId = "";
        private bool _enterEmitted = true;
        // Joining wrld_ で入室を確定させた直後の Joining or Creating Room は重複なので読み飛ばす
        private bool _joinOrCreateExpected;

        // 継続行（スタックトレース）を収集中のエラー
        private LogEvent? _pendingError;
        private StringBuilder? _pendingErrorDetail;
        private int _pendingErrorLines;

        public LogParser(string sourceFile)
        {
            _sourceFile = sourceFile;
        }

        public IEnumerable<LogEvent> Parse(TextReader reader)
        {
            string? line;
            int lineNumber = 0;
            while ((line = reader.ReadLine()) != null)
            {
                lineNumber++;
                foreach (var e in ParseLine(line, lineNumber))
                {
                    yield return e;
                }
            }

            if (FlushError() is { } last)
            {
                yield return last;
            }
        }

        private IEnumerable<LogEvent> ParseLine(string line, int lineNumber)
        {
            var head = LineHead.Match(line);
            if (!head.Success)
            {
                AppendErrorContinuation(line);
                yield break;
            }

            if (FlushError() is { } error)
            {
                yield return error;
            }

            string ts = ToTimestamp(head.Groups["ts"].Value);
            string level = head.Groups["level"].Value;
            string msg = head.Groups["msg"].Value.TrimEnd();

            if (level is "Error" or "Exception")
            {
                _pendingError = NewEvent(LogEventType.Error, ts, lineNumber);
                _pendingErrorDetail = new StringBuilder(msg);
                _pendingErrorLines = 0;
                yield break;
            }

            // [Behaviour] / User Authenticated / [Video Playback] 以外は対象外なので先に弾く
            if (msg.Length == 0 || (msg[0] != '['
                && !msg.StartsWith("User Authenticated", StringComparison.Ordinal)
                && !msg.StartsWith("Received Notification", StringComparison.Ordinal)))
            {
                yield break;
            }

            Match m;
            if ((m = EnteringRoom.Match(msg)).Success)
            {
                _pendingName = m.Groups["name"].Value;
                _pendingTimestamp = ts;
                _pendingWorldId = "";
                _pendingInstanceId = "";
                _enterEmitted = false;
                _joinOrCreateExpected = false;
            }
            else if ((m = JoiningWorld.Match(msg)).Success)
            {
                _pendingWorldId = m.Groups["wid"].Value;
                _pendingInstanceId = m.Groups["inst"].Value;
                if (!_enterEmitted && _pendingName != null)
                {
                    _joinOrCreateExpected = true;
                    yield return EmitWorldEnter(_pendingName, lineNumber);
                }
            }
            else if ((m = JoiningOrCreatingRoom.Match(msg)).Success)
            {
                if (_joinOrCreateExpected)
                {
                    _joinOrCreateExpected = false;
                }
                else
                {
                    // Entering Room / Joining wrld_ が揃わない形式のログ向けのフォールバック
                    if (_pendingName == null)
                    {
                        _pendingTimestamp = ts;
                    }
                    yield return EmitWorldEnter(m.Groups["name"].Value, lineNumber);
                }
            }
            else if (LeftRoom.IsMatch(msg))
            {
                if (_worldName.Length > 0)
                {
                    yield return NewEvent(LogEventType.WorldLeave, ts, lineNumber);
                }
            }
            else if ((m = PlayerJoined.Match(msg)).Success)
            {
                yield return NewPlayerEvent(LogEventType.PlayerJoin, ts, lineNumber, m);
            }
            else if ((m = PlayerLeft.Match(msg)).Success)
            {
                yield return NewPlayerEvent(LogEventType.PlayerLeave, ts, lineNumber, m);
            }
            else if ((m = VideoResolve.Match(msg)).Success)
            {
                var e = NewEvent(LogEventType.Video, ts, lineNumber);
                e.Detail = m.Groups["url"].Value;
                yield return e;
            }
            else if ((m = ReceivedNotification.Match(msg)).Success)
            {
                yield return NewNotificationEvent(ts, lineNumber, m);
            }
            else if ((m = UserAuthenticated.Match(msg)).Success)
            {
                _localUserName = m.Groups["name"].Value;
                _localUserId = m.Groups["uid"].Value;
            }
        }

        private LogEvent EmitWorldEnter(string name, int lineNumber)
        {
            _visitTimestamp = _pendingTimestamp;
            _worldName = name;
            _worldId = _pendingWorldId;
            _instanceId = _pendingInstanceId;
            _enterEmitted = true;
            _pendingName = null;
            _pendingWorldId = "";
            _pendingInstanceId = "";
            return NewEvent(LogEventType.WorldEnter, _visitTimestamp, lineNumber);
        }

        private LogEvent NewPlayerEvent(LogEventType type, string ts, int lineNumber, Match m)
        {
            var e = NewEvent(type, ts, lineNumber);
            e.PlayerName = m.Groups["name"].Value;
            e.UserId = m.Groups["uid"].Value;
            e.IsSelf = _localUserId.Length > 0 && e.UserId.Length > 0
                ? e.UserId == _localUserId
                : _localUserName.Length > 0 && e.PlayerName == _localUserName;
            return e;
        }

        /// <summary>
        /// 自分宛てのインバイト／リクエストインバイト。
        /// 日時は通知の作成日時（UTC をローカル時刻に変換）。ログインのたびに出し直される過去の通知も、届いた日時で記録される。
        /// 詳細は 1 行目に招待先のワールド名（インバイトのみ）、以降にメッセージ。
        /// </summary>
        private LogEvent NewNotificationEvent(string ts, int lineNumber, Match m)
        {
            var type = m.Groups["type"].Value == "invite" ? LogEventType.Invite : LogEventType.RequestInvite;
            var e = NewEvent(type, ts, lineNumber);
            if (DateTime.TryParseExact(m.Groups["created"].Value, "MM/dd/yyyy HH:mm:ss", CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var createdUtc))
            {
                e.Timestamp = LogEvent.FormatTimestamp(createdUtc.ToLocalTime());
            }
            e.PlayerName = m.Groups["name"].Value;
            e.UserId = m.Groups["uid"].Value;
            e.NotificationId = m.Groups["nid"].Value;

            var lines = new List<string>();
            var d = InviteDetails.Match(m.Groups["details"].Value);
            if (type == LogEventType.Invite && d.Success)
            {
                string target = d.Groups["wname"].Value.Trim();
                string location = d.Groups["inst"].Success ? $"{d.Groups["wid"].Value}:{d.Groups["inst"].Value}" : d.Groups["wid"].Value;
                lines.Add(target.Length > 0 ? $"→ {target}" : $"→ {location}");
                lines.Add(location);
            }
            if (m.Groups["msg"].Success && m.Groups["msg"].Value.Length > 0)
            {
                lines.Add(m.Groups["msg"].Value);
            }
            e.Detail = string.Join("\n", lines);
            return e;
        }

        private LogEvent NewEvent(LogEventType type, string ts, int lineNumber) => new()
        {
            Timestamp = ts,
            EventType = type,
            VisitTimestamp = _visitTimestamp,
            WorldName = _worldName,
            WorldId = _worldId,
            InstanceId = _instanceId,
            SourceFile = _sourceFile,
            LineNumber = lineNumber,
        };

        private void AppendErrorContinuation(string line)
        {
            if (_pendingErrorDetail == null || string.IsNullOrWhiteSpace(line))
            {
                return;
            }
            if (_pendingErrorLines >= MaxErrorDetailLines || _pendingErrorDetail.Length >= MaxErrorDetailLength)
            {
                return;
            }
            _pendingErrorDetail.Append('\n').Append(line.TrimEnd());
            _pendingErrorLines++;
        }

        private LogEvent? FlushError()
        {
            if (_pendingError == null)
            {
                return null;
            }
            var e = _pendingError;
            string detail = _pendingErrorDetail!.ToString();
            e.Detail = detail.Length > MaxErrorDetailLength ? detail[..MaxErrorDetailLength] : detail;
            _pendingError = null;
            _pendingErrorDetail = null;
            return e;
        }

        /// <summary>"2026.10.02 21:14:10" → "2026-10-02 21:14:10"</summary>
        internal static string ToTimestamp(string vrcTimestamp)
        {
            if (vrcTimestamp.Length < 10)
            {
                return vrcTimestamp;
            }
            return string.Create(vrcTimestamp.Length, vrcTimestamp, static (span, src) =>
            {
                src.AsSpan().CopyTo(span);
                span[4] = '-';
                span[7] = '-';
            });
        }
    }
}
