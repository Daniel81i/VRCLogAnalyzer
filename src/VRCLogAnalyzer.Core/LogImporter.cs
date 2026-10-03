using System.Text;
using NLog;

namespace VRCLogAnalyzer.Core
{
    public sealed record ImportProgress(int Processed, int Total, string CurrentFile);

    public sealed record ImportResult(int FilesScanned, int FilesSkipped, int EventsInserted, int LegacyMigrated);

    /// <summary>VRChat のログフォルダを走査し、変更のあったログファイルだけを解析して EventStore に取り込む。</summary>
    public sealed class LogImporter
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        public static string DefaultLogDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "Low", "VRChat", "VRChat");

        private const string ParserVersionKey = "ParserVersion";

        private readonly EventStore _store;

        public LogImporter(EventStore store)
        {
            _store = store;
        }

        public ImportResult Import(string? logDir = null, IProgress<ImportProgress>? progress = null, CancellationToken ct = default)
        {
            logDir ??= DefaultLogDirectory;

            string parserVersion = LogParser.Version.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (_store.GetMeta(ParserVersionKey) != parserVersion)
            {
                // 解析処理が更新されたので、取り込み済みのログも読み直して新しい種別を拾う
                logger.Info("Parser version changed ({0} -> {1}). Re-reading all log files.", _store.GetMeta(ParserVersionKey) ?? "none", parserVersion);
                _store.ResetProcessedFiles();
            }
            logger.Info("VRChat Log Dir: {0}", logDir);

            var files = Directory.Exists(logDir)
                ? new DirectoryInfo(logDir).GetFiles("output_log_*.txt").OrderBy(f => f.Name, StringComparer.Ordinal).ToArray()
                : Array.Empty<FileInfo>();
            if (files.Length == 0)
            {
                logger.Warn("No log files found: {0}", logDir);
            }

            int skipped = 0;
            int inserted = 0;
            for (int i = 0; i < files.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                var f = files[i];
                progress?.Report(new ImportProgress(i, files.Length, f.Name));

                if (_store.IsUpToDate(f))
                {
                    skipped++;
                    continue;
                }

                try
                {
                    // VRChat 起動中でも読めるよう ReadWrite 共有で開く
                    using var fs = f.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(fs, Encoding.UTF8);
                    var events = new LogParser(f.Name).Parse(reader);
                    int n = _store.InsertFileEvents(f, events);
                    inserted += n;
                    logger.Info("Processed: {0} (+{1} events)", f.Name, n);
                }
                catch (IOException ex)
                {
                    // 1 ファイルの失敗で全体を止めない。ProcessedFile は更新されないので次回再試行される
                    logger.Error(ex, "Failed to read log file: {0}", f.Name);
                }
            }
            progress?.Report(new ImportProgress(files.Length, files.Length, ""));

            _store.SetMeta(ParserVersionKey, parserVersion);
            int migrated = new LegacyMigrator(_store).MigrateOnce();
            logger.Info("Import finished. files={0}, skipped={1}, inserted={2}, legacy={3}", files.Length, skipped, inserted, migrated);
            return new ImportResult(files.Length, skipped, inserted, migrated);
        }
    }
}
