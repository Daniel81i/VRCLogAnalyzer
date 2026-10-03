using NLog;
using VRCLogAnalyzer.Core.Legacy;

namespace VRCLogAnalyzer.Core
{
    /// <summary>
    /// 旧バージョンの UserEncounterHistory / WorldVisitHistory を LogEvent に 1 度だけ移行する。
    /// 旧テーブルはバックアップとしてそのまま残す。
    /// </summary>
    public sealed class LegacyMigrator
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        public const string UserSource = "legacy:UserEncounterHistory";
        public const string WorldSource = "legacy:WorldVisitHistory";

        private readonly EventStore _store;

        public LegacyMigrator(EventStore store)
        {
            _store = store;
        }

        /// <summary>
        /// 未移行なら移行して件数を返す。
        /// 現在ディスク上に残っているログの期間はログから正確に再取り込みできるため、それより古いデータだけを移行する。
        /// </summary>
        public int MigrateOnce()
        {
            if (_store.GetMeta(EventStore.LegacyMigratedKey) != null)
            {
                return 0;
            }
            bool hasUsers = _store.TableExists(nameof(UserEncounterHistory));
            bool hasWorlds = _store.TableExists(nameof(WorldVisitHistory));
            if (!hasUsers && !hasWorlds)
            {
                _store.SetMeta(EventStore.LegacyMigratedKey, "none");
                return 0;
            }

            string? cutoff = _store.GetOldestLogTimestamp();
            logger.Info("Migrating legacy tables. cutoff={0}", cutoff ?? "(none)");

            var events = new List<LogEvent>();
            var conn = _store.Connection;
            if (hasWorlds)
            {
                foreach (var w in conn.Query<WorldVisitHistory>("SELECT * FROM WorldVisitHistory"))
                {
                    string ts = LogParser.ToTimestamp(w.WorldVisitTimestamp ?? "");
                    if (!IsBefore(ts, cutoff))
                    {
                        continue;
                    }
                    events.Add(new LogEvent
                    {
                        Timestamp = ts,
                        EventType = LogEventType.WorldEnter,
                        VisitTimestamp = ts,
                        WorldName = w.WorldName ?? "",
                        WorldId = w.WorldId ?? "",
                        Detail = w.Description ?? "",
                        SourceFile = WorldSource,
                        LineNumber = w.Id,
                    });
                }
            }
            if (hasUsers)
            {
                foreach (var u in conn.Query<UserEncounterHistory>("SELECT * FROM UserEncounterHistory"))
                {
                    string ts = LogParser.ToTimestamp(u.Timestamp ?? "");
                    if (!IsBefore(ts, cutoff))
                    {
                        continue;
                    }
                    events.Add(new LogEvent
                    {
                        Timestamp = ts,
                        EventType = LogEventType.PlayerJoin,
                        VisitTimestamp = LogParser.ToTimestamp(u.WorldVisitTimestamp ?? ""),
                        WorldName = u.WorldName ?? "",
                        PlayerName = u.DisplayName ?? "",
                        Detail = u.Bio ?? "",
                        SourceFile = UserSource,
                        LineNumber = u.Id,
                    });
                }
            }

            int inserted = _store.InsertEvents(events);
            _store.SetMeta(EventStore.LegacyMigratedKey, LogEvent.FormatTimestamp(DateTime.Now));
            logger.Info("Migrated {0} legacy rows.", inserted);
            return inserted;
        }

        private static bool IsBefore(string ts, string? cutoff) =>
            ts.Length > 0 && (cutoff == null || string.CompareOrdinal(ts, cutoff) < 0);
    }
}
