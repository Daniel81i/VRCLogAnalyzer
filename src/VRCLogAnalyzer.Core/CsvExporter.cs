using System.Text;

namespace VRCLogAnalyzer.Core
{
    /// <summary>RFC 4180 形式で CSV を書き出す。</summary>
    public static class CsvExporter
    {
        private static readonly string[] Header =
        {
            "Timestamp", "EventType", "WorldName", "WorldId", "InstanceId", "VisitTimestamp",
            "PlayerName", "UserId", "IsSelf", "Detail", "SourceFile",
        };

        public static void Write(TextWriter writer, IEnumerable<LogEvent> events)
        {
            writer.Write(string.Join(",", Header));
            writer.Write("\r\n");
            foreach (var e in events)
            {
                WriteRow(writer,
                    e.Timestamp, e.EventType.ToString(), e.WorldName, e.WorldId, e.InstanceId, e.VisitTimestamp,
                    e.PlayerName, e.UserId, e.IsSelf ? "1" : "0", e.Detail, e.SourceFile);
            }
        }

        /// <summary>Excel で文字化けしないよう BOM 付き UTF-8 で書き出す。</summary>
        public static void WriteFile(string path, IEnumerable<LogEvent> events)
        {
            using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
            Write(writer, events);
        }

        private static void WriteRow(TextWriter writer, params string[] fields)
        {
            for (int i = 0; i < fields.Length; i++)
            {
                if (i > 0)
                {
                    writer.Write(',');
                }
                writer.Write(Escape(fields[i]));
            }
            writer.Write("\r\n");
        }

        internal static string Escape(string value)
        {
            // ユーザー名は他人が自由に付けられるため、Excel で数式として実行されないよう先頭に ' を付ける
            if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
            {
                value = "'" + value;
            }
            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
            {
                return value;
            }
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
