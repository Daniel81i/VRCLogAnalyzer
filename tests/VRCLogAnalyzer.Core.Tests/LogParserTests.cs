namespace VRCLogAnalyzer.Core.Tests
{
    public class LogParserTests
    {
        private static List<LogEvent> Parse(string text) =>
            new LogParser("output_log_test.txt").Parse(new StringReader(text)).ToList();

        [Fact]
        public void ExtractsEventsInOrder()
        {
            var events = Parse(SampleLog.Text);

            Assert.Equal(new[]
            {
                LogEventType.WorldEnter,
                LogEventType.Video,
                LogEventType.PlayerJoin,
                LogEventType.PlayerJoin,
                LogEventType.Error,
                LogEventType.PlayerLeave,
                LogEventType.WorldLeave,
                LogEventType.PlayerLeave,
                LogEventType.WorldEnter,
                LogEventType.Error,
            }, events.Select(e => e.EventType));
        }

        [Fact]
        public void WorldEnterHasIdInstanceAndEnteringRoomTimestamp()
        {
            var enter = Parse(SampleLog.Text).First(e => e.EventType == LogEventType.WorldEnter);

            Assert.Equal("2026-10-02 21:14:10", enter.Timestamp);
            Assert.Equal("［JP］ Test World ワールド", enter.WorldName);
            Assert.Equal(SampleLog.World1, enter.WorldId);
            Assert.Equal($"47371~private({SampleLog.SelfId})~region(jp)", enter.InstanceId);
            Assert.Equal(enter.Timestamp, enter.VisitTimestamp);
        }

        [Fact]
        public void JoiningOrCreatingRoomDoesNotDuplicateWorldEnter()
        {
            Assert.Equal(2, Parse(SampleLog.Text).Count(e => e.EventType == LogEventType.WorldEnter));
        }

        [Fact]
        public void PlayerEventsCarryUserIdWorldContextAndSelfFlag()
        {
            var events = Parse(SampleLog.Text);
            var joins = events.Where(e => e.EventType == LogEventType.PlayerJoin).ToList();

            Assert.Equal("Me (\"Self\")", joins[0].PlayerName);
            Assert.True(joins[0].IsSelf);
            Assert.Equal("Friend (Away)", joins[1].PlayerName);
            Assert.Equal(SampleLog.FriendId, joins[1].UserId);
            Assert.False(joins[1].IsSelf);
            Assert.All(joins, j => Assert.Equal(SampleLog.World1, j.WorldId));
            Assert.All(joins, j => Assert.Equal("2026-10-02 21:14:10", j.VisitTimestamp));
        }

        [Fact]
        public void LeavesAfterOnLeftRoomBelongToPreviousWorld()
        {
            var events = Parse(SampleLog.Text);
            var selfLeave = events.Last(e => e.EventType == LogEventType.PlayerLeave);

            Assert.True(selfLeave.IsSelf);
            Assert.Equal(SampleLog.World1, selfLeave.WorldId);
            Assert.Equal(SampleLog.World1, events.Single(e => e.EventType == LogEventType.WorldLeave).WorldId);
        }

        [Fact]
        public void OnPlayerLeftRoomIsNotAPlayerLeave()
        {
            Assert.DoesNotContain(Parse(SampleLog.Text), e => e.PlayerName.StartsWith("Room"));
        }

        [Fact]
        public void VideoUrlIsCaptured()
        {
            var video = Parse(SampleLog.Text).Single(e => e.EventType == LogEventType.Video);
            Assert.Equal("https://example.com/live?p=o&w=test", video.Detail);
            Assert.Equal("［JP］ Test World ワールド", video.WorldName);
        }

        [Fact]
        public void ErrorCollectsStackTraceButNotFollowingWarningLines()
        {
            var errors = Parse(SampleLog.Text).Where(e => e.EventType == LogEventType.Error).ToList();

            Assert.Equal(3, errors[0].Detail.Split('\n').Length);
            Assert.StartsWith("[UdonBehaviour] An exception", errors[0].Detail);
            Assert.DoesNotContain("warning stack", errors[0].Detail);
            Assert.Equal("Last line error without trace", errors[1].Detail);
            Assert.Equal(SampleLog.World2, errors[1].WorldId);
        }

        [Fact]
        public void ErrorDetailIsBounded()
        {
            var lines = new List<string> { "2026.10.02 21:14:34 Error      -  boom" };
            lines.AddRange(Enumerable.Repeat("  at Somewhere ()", 500));
            var error = Parse(string.Join("\n", lines)).Single();

            Assert.Equal(LogParser.MaxErrorDetailLines + 1, error.Detail.Split('\n').Length);
        }

        [Fact]
        public void LineNumbersAreStableAndUnique()
        {
            var events = Parse(SampleLog.Text);
            Assert.Equal(events.Count, events.Select(e => e.LineNumber).Distinct().Count());
            Assert.Equal(events.Select(e => e.LineNumber), Parse(SampleLog.Text).Select(e => e.LineNumber));
        }

        [Fact]
        public void LegacyFormatWithoutEnteringRoomStillRecordsWorld()
        {
            var events = Parse(string.Join("\n",
                "2021.07.01 20:00:00 Log        -  [Behaviour] Joining or Creating Room: Old World",
                "2021.07.01 20:00:05 Log        -  [Behaviour] OnPlayerJoined OldUser"));

            Assert.Equal("Old World", events[0].WorldName);
            Assert.Equal("2021-07-01 20:00:00", events[0].Timestamp);
            Assert.Equal("OldUser", events[1].PlayerName);
            Assert.Equal("", events[1].UserId);
            Assert.Equal("Old World", events[1].WorldName);
        }

        [Fact]
        public void HandlesCrLfLineEndings()
        {
            var events = Parse(SampleLog.Text.Replace("\n", "\r\n"));
            Assert.Equal(10, events.Count);
            Assert.Equal("Friend (Away)", events[3].PlayerName);
        }
    }
}
