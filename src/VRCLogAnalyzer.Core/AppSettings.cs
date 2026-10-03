using System.Xml.Linq;
using NLog;

namespace VRCLogAnalyzer.Core
{
    public enum DbLocation
    {
        MyDocuments,
        AppPath,
    }

    /// <summary>
    /// アプリフォルダの VRCLogAnalyzer.config（旧バージョンと同じ appSettings 形式）を読み書きする。
    /// ファイルが無い・壊れている場合は既定値で動作する。
    /// </summary>
    public sealed class AppSettings
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        public const string FileName = "VRCLogAnalyzer.config";
        public const string DatabaseFileName = "VRCLogAnalyzer.db";
        private const string DbPathChoiceKey = "DbPathChoice";
        private const string LogDirKey = "LogDir";
        private const string LanguageKey = "Language";
        private const string ShowDetailKey = "ShowDetail";

        private readonly string _appPath;
        private readonly string _configPath;

        public DbLocation DbLocation { get; set; } = DbLocation.MyDocuments;

        /// <summary>空なら既定の VRChat ログフォルダ</summary>
        public string LogDir { get; set; } = "";

        /// <summary>UI の表示言語（"ja" / "en"）。空なら OS の表示言語に合わせる</summary>
        public string Language { get; set; } = "";

        /// <summary>画面下部の詳細欄を表示するか（既定は非表示）</summary>
        public bool ShowDetail { get; set; }

        public AppSettings(string appPath)
        {
            _appPath = appPath;
            _configPath = Path.Combine(appPath, FileName);
        }

        public static AppSettings Load(string appPath)
        {
            var s = new AppSettings(appPath);
            if (!File.Exists(s._configPath))
            {
                return s;
            }
            try
            {
                var values = XDocument.Load(s._configPath).Root?.Element("appSettings")?.Elements("add")
                    .Where(e => e.Attribute("key") != null)
                    .ToDictionary(e => (string)e.Attribute("key")!, e => (string?)e.Attribute("value") ?? "")
                    ?? new Dictionary<string, string>();
                if (values.TryGetValue(DbPathChoiceKey, out var choice) && Enum.TryParse<DbLocation>(choice, out var loc))
                {
                    s.DbLocation = loc;
                }
                if (values.TryGetValue(LogDirKey, out var logDir))
                {
                    s.LogDir = logDir;
                }
                if (values.TryGetValue(LanguageKey, out var language))
                {
                    s.Language = language;
                }
                if (values.TryGetValue(ShowDetailKey, out var showDetail) && bool.TryParse(showDetail, out bool sd))
                {
                    s.ShowDetail = sd;
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Could not read settings file, using defaults: {0}", s._configPath);
            }
            return s;
        }

        public void Save()
        {
            var doc = new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement("configuration",
                    new XElement("appSettings",
                        new XElement("add", new XAttribute("key", DbPathChoiceKey), new XAttribute("value", DbLocation.ToString())),
                        new XElement("add", new XAttribute("key", LogDirKey), new XAttribute("value", LogDir)),
                        new XElement("add", new XAttribute("key", LanguageKey), new XAttribute("value", Language)),
                        new XElement("add", new XAttribute("key", ShowDetailKey), new XAttribute("value", ShowDetail ? "true" : "false")))));
            doc.Save(_configPath);
        }

        public string DatabasePath => Path.Combine(DbLocation switch
        {
            DbLocation.AppPath => _appPath,
            _ => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VRCLogAnalyzer"),
        }, DatabaseFileName);

        public string EffectiveLogDir => string.IsNullOrWhiteSpace(LogDir) ? LogImporter.DefaultLogDirectory : LogDir;
    }
}
