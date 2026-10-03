using System.IO;
using System.Windows;
using System.Windows.Threading;
using NLog;
using VRCLogAnalyzer.Core;

namespace VRCLogAnalyzer
{
    public partial class App : Application
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        public static string AppPath => AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

        // 終了コード
        private const int ExitImportFailed = 1;
        private const int ExitBadArguments = 2;
        private const int ExitUnsupportedDatabase = 3;

        /// <summary>
        /// コマンドライン（/xxx・-xxx・--xxx のいずれの形式でも可）:
        ///   /analyze          ウィンドウを開かずにログを取り込んで終了する（タスクスケジューラ用）
        ///   /db &lt;path&gt;        データベースファイルを指定する（設定より優先）
        ///   /logdir &lt;path&gt;    VRChat のログフォルダを指定する（設定より優先）
        /// 認識できない引数があれば何もせずに終了する（指定した DB と違う DB に書き込む事故を防ぐため）。
        /// </summary>
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            var settings = AppSettings.Load(AppPath);
            Loc.Apply(settings.Language);
            bool analyzeOnly = false;
            string? dbOverride = null;
            string? logDirOverride = null;
            var unknown = new List<string>();

            for (int i = 0; i < e.Args.Length; i++)
            {
                string arg = e.Args[i];
                string name = arg.StartsWith("--", StringComparison.Ordinal) ? arg[2..]
                    : arg.StartsWith('/') || arg.StartsWith('-') ? arg[1..]
                    : "";
                switch (name.ToLowerInvariant())
                {
                    case "analyze":
                        analyzeOnly = true;
                        break;
                    case "db" when i + 1 < e.Args.Length:
                        dbOverride = e.Args[++i];
                        break;
                    case "logdir" when i + 1 < e.Args.Length:
                        logDirOverride = e.Args[++i];
                        break;
                    default:
                        unknown.Add(arg);
                        break;
                }
            }

            if (unknown.Count > 0)
            {
                logger.Error("Unknown or incomplete arguments: {0}", string.Join(" ", unknown));
                if (!analyzeOnly)
                {
                    MessageBox.Show(Loc.F("Args.Unknown", string.Join(" ", unknown)), "VRCLogAnalyzer", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                ExitApp(ExitBadArguments);
                return;
            }

            string dbPath = dbOverride ?? settings.DatabasePath;
            string logDir = logDirOverride ?? settings.EffectiveLogDir;

            if (analyzeOnly)
            {
                int exitCode = 0;
                try
                {
                    // 移行が必要な場合はここでバックアップが作られ、次に画面を開いたときに通知される
                    using var store = new EventStore(dbPath);
                    new LogImporter(store).Import(logDir);
                }
                catch (UnsupportedDatabaseException ex)
                {
                    logger.Error(ex, "Database was not opened to avoid damaging it");
                    exitCode = ExitUnsupportedDatabase;
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Log import failed");
                    exitCode = ExitImportFailed;
                }
                ExitApp(exitCode);
                return;
            }

            // 画面を開く前に DB を確認する（移行が必要ならここでバックアップされる）
            try
            {
                using var store = new EventStore(dbPath);
            }
            catch (UnsupportedDatabaseException ex)
            {
                logger.Error(ex, "Database was not opened to avoid damaging it");
                ShowUnsupportedDatabase(ex);
                ExitApp(ExitUnsupportedDatabase);
                return;
            }

            var window = new MainWindow(settings, dbOverride, logDirOverride);
            MainWindow = window;
            window.Show();
        }

        private void ExitApp(int exitCode)
        {
            LogManager.Shutdown();
            Shutdown(exitCode);
        }

        /// <summary>開くと壊すおそれのある DB だった場合のメッセージ</summary>
        public static void ShowUnsupportedDatabase(UnsupportedDatabaseException ex, Window? owner = null)
        {
            string key = ex.Inspection.State == DbState.Newer ? "Db.Newer" : "Db.Unrecognized";
            string text = Loc.F(key, ex.DatabasePath);
            if (owner != null)
            {
                MessageBox.Show(owner, text, "VRCLogAnalyzer", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            else
            {
                MessageBox.Show(text, "VRCLogAnalyzer", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Application_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            logger.Error(e.Exception, "Unhandled exception");
            MessageBox.Show(Loc.F("Error.Unexpected", e.Exception.Message), "VRCLogAnalyzer", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            LogManager.Shutdown();
            base.OnExit(e);
        }
    }
}
