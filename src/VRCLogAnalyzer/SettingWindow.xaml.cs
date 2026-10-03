using System.IO;
using System.Windows;
using Microsoft.Win32;
using NLog;
using VRCLogAnalyzer.Core;

namespace VRCLogAnalyzer
{
    public partial class SettingWindow : Window
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private readonly AppSettings _settings;

        private sealed record LanguageOption(string Code, string Label);

        /// <param name="currentDatabasePath">現在使用中の DB ファイル（/db 指定時はそのパス）</param>
        public SettingWindow(AppSettings settings, string currentDatabasePath)
        {
            InitializeComponent();
            _settings = settings;
            CurrentDbText.Text = currentDatabasePath;

            // 言語名はどの言語で表示中でも読めるよう、その言語自身の表記で固定する
            LanguageCombo.ItemsSource = new[]
            {
                new LanguageOption("", Loc.T("Settings.LanguageAuto")),
                new LanguageOption("ja", "日本語"),
                new LanguageOption("en", "English"),
            };
            LanguageCombo.SelectedValue = Loc.SupportedLanguages.Contains(settings.Language) ? settings.Language : "";

            DbPathConfigMyDocuments.IsChecked = settings.DbLocation == DbLocation.MyDocuments;
            DbPathConfigAppPath.IsChecked = settings.DbLocation == DbLocation.AppPath;
            LogDirText.Text = settings.LogDir;
            DefaultLogDirText.Text = Loc.F("Settings.LogDirDefault", LogImporter.DefaultLogDirectory);
        }

        private void Button_BrowseLogDir(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                InitialDirectory = string.IsNullOrWhiteSpace(LogDirText.Text) ? LogImporter.DefaultLogDirectory : LogDirText.Text,
            };
            if (dialog.ShowDialog(this) == true)
            {
                LogDirText.Text = dialog.FolderName;
            }
        }

        private void Button_Click_OK(object sender, RoutedEventArgs e)
        {
            _settings.Language = LanguageCombo.SelectedValue as string ?? "";
            _settings.DbLocation = DbPathConfigAppPath.IsChecked == true ? DbLocation.AppPath : DbLocation.MyDocuments;
            _settings.LogDir = LogDirText.Text.Trim();
            try
            {
                _settings.Save();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.Error(ex, "Failed to save settings file");
                MessageBox.Show(this, Loc.F("Settings.SaveFailed", ex.Message), "VRCLogAnalyzer", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DialogResult = true;
        }
    }
}
