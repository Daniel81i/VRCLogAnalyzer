using System.Globalization;
using System.Windows;
using System.Windows.Data;
using VRCLogAnalyzer.Core;

namespace VRCLogAnalyzer
{
    /// <summary>
    /// UI 文字列のローカライズ。Resources/Strings.{lang}.xaml をアプリのリソースに差し替えて切り替える。
    /// XAML からは {DynamicResource キー}、コードからは Loc.T / Loc.F で参照する。
    /// </summary>
    public static class Loc
    {
        public static readonly IReadOnlyList<string> SupportedLanguages = new[] { "ja", "en" };

        private static ResourceDictionary? _current;

        public static string CurrentLanguage { get; private set; } = "ja";

        public static event EventHandler? LanguageChanged;

        /// <summary>設定値（"ja" / "en" / 空 = 自動）から言語を決めて適用する</summary>
        public static void Apply(string setting)
        {
            string lang = Resolve(setting);
            var dict = new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/Resources/Strings.{lang}.xaml", UriKind.Absolute),
            };
            var merged = Application.Current.Resources.MergedDictionaries;
            if (_current != null)
            {
                merged.Remove(_current);
            }
            merged.Add(dict);
            _current = dict;

            CurrentLanguage = lang;
            var culture = CultureInfo.GetCultureInfo(lang);
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            LanguageChanged?.Invoke(null, EventArgs.Empty);
        }

        internal static string Resolve(string setting)
        {
            if (SupportedLanguages.Contains(setting))
            {
                return setting;
            }
            return CultureInfo.InstalledUICulture.TwoLetterISOLanguageName == "ja" ? "ja" : "en";
        }

        public static string T(string key) => Application.Current?.TryFindResource(key) as string ?? key;

        public static string F(string key, params object[] args) => string.Format(CultureInfo.CurrentUICulture, T(key), args);

        public static string EventType(LogEventType type) => T("EventType." + type);
    }

    /// <summary>LogEventType → 表示名</summary>
    public sealed class EventTypeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is LogEventType t ? Loc.EventType(t) : "";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    /// <summary>ワールド訪問グループのキー → 表示名（入室前のイベントは空キー）</summary>
    public sealed class VisitGroupConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is string s && s.Length > 0 ? s : Loc.T("Group.BeforeEnter");

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
