namespace VRCLogAnalyzer.Core.Tests
{
    /// <summary>自分宛てのインバイト／リクエストインバイト（名前・ID・ワールドはすべて架空）</summary>
    public sealed class InviteTests : IDisposable
    {
        private const string Friend = "usr_00000000-0000-0000-0000-000000000002";
        private const string Me = "usr_00000000-0000-0000-0000-000000000001";
        private const string World = "wrld_33333333-3333-3333-3333-333333333333";

        private static string InviteLine(string date, string time, string notificationId, string created) =>
            $"{date} {time} Debug      -  Received Notification: <Notification from username:Friend, A (Away), sender user id:{Friend} to {Me} of type: invite, id: {notificationId}, created at: {created} UTC, details: {{{{worldId={World}:23878~group(grp_00000000-0000-0000-0000-000000000009)~region(jp), worldName=［JP］ Invite, World}}}}, type:invite, m seen:False, message: \"This is a generated invite\">";

        private static string RequestInviteLine(string date) =>
            $"{date} 22:00:05 Debug      -  Received Notification: <Notification from username:Requester, sender user id:{Friend} to {Me} of type: requestInvite, id: not_00000000-0000-0000-0000-0000000000r1, created at: 10/02/2026 13:00:04 UTC, details: {{{{}}}}, type:requestInvite, m seen:False, message: \"\">";

        private static readonly string Text = string.Join("\n",
            "2026.10.02 21:14:10 Debug      -  [Behaviour] Entering Room: Here",
            $"2026.10.02 21:14:10 Debug      -  [Behaviour] Joining {World}:1~region(jp)",
            InviteLine("2026.10.02", "21:30:51", "not_00000000-0000-0000-0000-0000000000a1", "10/02/2026 12:30:50"),
            RequestInviteLine("2026.10.02"),
            // invite / requestInvite 以外の通知は取り込まない
            $"2026.10.02 22:00:06 Debug      -  Received Notification: <Notification from username:Group, sender user id: to {Me} of type: group, id: not_00000000-0000-0000-0000-0000000000g1, created at: 10/02/2026 13:00:05 UTC, details: {{{{}}}}, type:group, m seen:False, message: \"news\">");

        private readonly string _dir = Path.Combine(Path.GetTempPath(), "VRCLogAnalyzerTests", Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        private static List<LogEvent> Parse(string text) =>
            new LogParser("output_log_test.txt").Parse(new StringReader(text)).ToList();

        [Fact]
        public void InviteIsParsedWithSenderTargetWorldAndCreatedTime()
        {
            var invite = Parse(Text).Single(e => e.EventType == LogEventType.Invite);

            Assert.Equal("Friend, A (Away)", invite.PlayerName);
            Assert.Equal(Friend, invite.UserId);
            Assert.Equal("not_00000000-0000-0000-0000-0000000000a1", invite.NotificationId);
            // 日時は通知の作成日時（UTC）をローカル時刻にしたもの
            var expected = new DateTime(2026, 10, 2, 12, 30, 50, DateTimeKind.Utc).ToLocalTime();
            Assert.Equal(LogEvent.FormatTimestamp(expected), invite.Timestamp);
            // 詳細の 1 行目は招待先ワールド、2 行目はインスタンス、3 行目はメッセージ
            var lines = invite.Detail.Split('\n');
            Assert.Equal("→ ［JP］ Invite, World", lines[0]);
            Assert.StartsWith(World + ":23878~group(", lines[1]);
            Assert.Equal("This is a generated invite", lines[2]);
            // ワールド列は受け取ったときにいたワールド
            Assert.Equal("Here", invite.WorldName);
        }

        [Fact]
        public void RequestInviteIsParsedAndOtherNotificationsAreIgnored()
        {
            var events = Parse(Text);
            var req = events.Single(e => e.EventType == LogEventType.RequestInvite);
            Assert.Equal("Requester", req.PlayerName);
            Assert.Equal(Friend, req.UserId);
            Assert.Equal("", req.Detail);
            Assert.Equal(3, events.Count); // ワールド入室・インバイト・リクエストインバイト
        }

        [Fact]
        public void NotificationsRepeatedAtLoginAreNotDuplicated()
        {
            var logDir = Path.Combine(_dir, "logs");
            Directory.CreateDirectory(logDir);
            File.WriteAllText(Path.Combine(logDir, "output_log_2026-10-02_21-14-00.txt"), Text);
            // 次のログイン時に同じ通知がもう一度ログに出る
            File.WriteAllText(Path.Combine(logDir, "output_log_2026-10-03_09-00-00.txt"), string.Join("\n",
                InviteLine("2026.10.03", "09:00:01", "not_00000000-0000-0000-0000-0000000000a1", "10/02/2026 12:30:50"),
                RequestInviteLine("2026.10.03")));

            using var store = new EventStore(Path.Combine(_dir, "test.db"));
            new LogImporter(store).Import(logDir);

            var types = new HashSet<LogEventType> { LogEventType.Invite, LogEventType.RequestInvite };
            Assert.Equal(2, store.Query(new EventFilter { Types = types }, 100).Count);
        }
    }
}
