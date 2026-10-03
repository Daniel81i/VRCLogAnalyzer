using System.IO;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using NLog;
using VRCLogAnalyzer.Core;

namespace VRCLogAnalyzer
{
    public partial class MainWindow : Window
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private const int MaxRows = 10000;

        private AppSettings _settings;
        private readonly string? _dbOverride;
        private readonly string? _logDirOverride;
        private List<LogEvent> _events = new();
        private EventFilter _lastFilter = new();
        private bool _importing;

        /// <summary>右クリックメニューの操作対象（右クリックした行、またはグループ見出しの先頭行）</summary>
        private LogEvent? _menuTarget;
        private bool _menuOnGroupHeader;

        public MainWindow(AppSettings settings, string? dbOverride, string? logDirOverride)
        {
            InitializeComponent();
            ApplyCulture();
            _settings = settings;
            ApplyDetailPane(settings.ShowDetail);
            _dbOverride = dbOverride;
            _logDirOverride = logDirOverride;
            SetRange(7);
        }

        private string DatabasePath => _dbOverride ?? _settings.DatabasePath;
        private string LogDir => _logDirOverride ?? _settings.EffectiveLogDir;

        private async void Window_ContentRendered(object? sender, EventArgs e)
        {
            bool isFirstBoot;
            string? backupNotice;
            using (var store = new EventStore(DatabasePath))
            {
                isFirstBoot = store.IsEmpty;
                backupNotice = store.TakePendingBackupNotice();
            }
            if (backupNotice != null)
            {
                // 旧形式の DB を移行する前にバックアップを取った場合は 1 度だけ知らせる
                MessageBox.Show(this, Loc.F("Db.BackupNotice", backupNotice), "VRCLogAnalyzer", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            if (isFirstBoot)
            {
                ShowDialog(new HelpWindow());
            }
            // 取り込みは差分のみで軽いため、起動時に毎回自動で実行する
            await RunImportAsync();
        }

        // ---- データ取り込み ----

        private async Task RunImportAsync()
        {
            if (_importing)
            {
                return;
            }
            _importing = true;
            UpdateMenu.IsEnabled = false;
            var progress = new Progress<ImportProgress>(p =>
                StatusText.Text = p.Total == 0 ? Loc.T("Status.Checking") : Loc.F("Status.Importing", p.Processed, p.Total));
            string dbPath = DatabasePath;
            string logDir = LogDir;
            try
            {
                var result = await Task.Run(() =>
                {
                    using var store = new EventStore(dbPath);
                    return new LogImporter(store).Import(logDir, progress);
                });
                string done = Loc.F("Status.ImportDone", result.EventsInserted, result.FilesScanned, result.FilesSkipped);
                if (result.LegacyMigrated > 0)
                {
                    done += " " + Loc.F("Status.LegacyMigrated", result.LegacyMigrated);
                }
                Search();
                StatusText.Text = done + " " + StatusText.Text;
            }
            catch (UnsupportedDatabaseException ex)
            {
                logger.Error(ex, "Database was not opened to avoid damaging it");
                StatusText.Text = Loc.F("Status.ImportFailed", ex.Message);
                App.ShowUnsupportedDatabase(ex, this);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Log import failed");
                StatusText.Text = Loc.F("Status.ImportFailed", ex.Message);
            }
            finally
            {
                _importing = false;
                UpdateMenu.IsEnabled = true;
            }
        }

        // ---- 検索 ----

        private EventFilter BuildFilter()
        {
            var types = new HashSet<LogEventType>();
            void Add(CheckBox cb, LogEventType t)
            {
                if (cb.IsChecked == true)
                {
                    types.Add(t);
                }
            }
            Add(TypeWorldEnter, LogEventType.WorldEnter);
            Add(TypeWorldLeave, LogEventType.WorldLeave);
            Add(TypePlayerJoin, LogEventType.PlayerJoin);
            Add(TypePlayerLeave, LogEventType.PlayerLeave);
            Add(TypeVideo, LogEventType.Video);
            Add(TypeError, LogEventType.Error);
            Add(TypeInvite, LogEventType.Invite);
            Add(TypeRequestInvite, LogEventType.RequestInvite);

            return new EventFilter
            {
                From = StartDate.SelectedDate,
                To = EndDate.SelectedDate,
                Types = types,
                PlayerName = QueryUsername.Text.Trim(),
                WorldName = QueryWorldname.Text.Trim(),
                Keyword = QueryKeyword.Text.Trim(),
                ExcludeSelf = ExcludeSelf.IsChecked == true,
            };
        }

        private void Search()
        {
            var filter = BuildFilter();
            List<LogEvent> events;
            using (var store = new EventStore(DatabasePath))
            {
                events = store.Query(filter, MaxRows);
            }
            bool truncated = events.Count > MaxRows;
            if (truncated)
            {
                events.RemoveAt(events.Count - 1);
            }
            _events = events;
            _lastFilter = filter;
            BindEvents();

            StatusText.Text = truncated ? Loc.F("Status.Truncated", MaxRows) : Loc.F("Status.Count", events.Count);
        }

        private void BindEvents()
        {
            var view = new ListCollectionView(_events);
            if (GroupByVisit.IsChecked == true)
            {
                view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(LogEvent.VisitGroup)));
            }
            EventGrid.ItemsSource = view;
            DetailText.Text = "";
        }

        private void Button_Search(object sender, RoutedEventArgs e) => Search();

        private void Filter_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && e.OriginalSource is TextBox)
            {
                Search();
                e.Handled = true;
            }
        }

        private void GroupByVisit_Changed(object sender, RoutedEventArgs e)
        {
            if (IsLoaded)
            {
                BindEvents();
            }
        }

        private void SetRange(int? days)
        {
            if (days is { } d)
            {
                StartDate.SelectedDate = DateTime.Today.AddDays(-(d - 1));
                EndDate.SelectedDate = DateTime.Today;
            }
            else
            {
                StartDate.SelectedDate = null;
                EndDate.SelectedDate = null;
            }
        }

        private void Range_Today(object sender, RoutedEventArgs e) { SetRange(1); Search(); }
        private void Range_Week(object sender, RoutedEventArgs e) { SetRange(7); Search(); }
        private void Range_Month(object sender, RoutedEventArgs e) { SetRange(30); Search(); }
        private void Range_All(object sender, RoutedEventArgs e) { SetRange(null); Search(); }

        // ---- 一覧の操作 ----

        private LogEvent? Selected => EventGrid.SelectedItem as LogEvent;

        private void EventGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Selected is not { } ev)
            {
                DetailText.Text = "";
                return;
            }
            var sb = new StringBuilder();
            sb.Append(ev.Timestamp).Append("  ").Append(Loc.EventType(ev.EventType)).AppendLine();
            if (ev.WorldName.Length > 0)
            {
                sb.Append(Loc.T("Detail.World")).Append(ev.WorldName);
                if (ev.WorldId.Length > 0)
                {
                    sb.Append("  (").Append(ev.WorldId).Append(':').Append(ev.InstanceId).Append(')');
                }
                sb.AppendLine();
            }
            if (ev.PlayerName.Length > 0)
            {
                sb.Append(Loc.T("Detail.User")).Append(ev.PlayerName);
                if (ev.UserId.Length > 0)
                {
                    sb.Append("  (").Append(ev.UserId).Append(')');
                }
                if (ev.IsSelf)
                {
                    sb.Append(Loc.T("Detail.Self"));
                }
                sb.AppendLine();
            }
            if (ev.Detail.Length > 0)
            {
                sb.AppendLine().Append(ev.Detail);
            }
            DetailText.Text = sb.ToString();
        }

        private void EventGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // グループ表示時、* 幅の列がウィンドウ拡大に追従しない WPF の不具合があるため、
            // 詳細列の幅は「一覧の幅 - 他の列の幅 - スクロールバー」で明示的に決める
            if (e.WidthChanged)
            {
                FitDetailColumn();
            }
        }

        private void FitDetailColumn()
        {
            double others = EventGrid.Columns.Where(c => c != DetailColumn).Sum(c => c.ActualWidth);
            double available = EventGrid.ActualWidth - others - SystemParameters.VerticalScrollBarWidth - 4;
            DetailColumn.Width = new DataGridLength(Math.Max(DetailColumn.MinWidth, available));
        }

        private void EventGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (Selected is { } ev && ev.EventType == LogEventType.Video)
            {
                OpenBrowser(ev.Detail);
            }
        }

        /// <summary>
        /// 右クリックした位置から操作対象を決める。WPF の DataGrid ではメニューが「選択中の行」に対して動くため、
        /// グループ見出しを右クリックすると別のワールドがコピーされる、値が無い項目を選ぶと何も起きない、といった問題があった。
        /// </summary>
        private void EventGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            _menuTarget = null;
            _menuOnGroupHeader = false;
            for (var dep = e.OriginalSource as DependencyObject; dep != null && dep != EventGrid; dep = GetParent(dep))
            {
                if (dep is DataGridRow { Item: LogEvent ev } row)
                {
                    // 複数選択中の行を右クリックした場合は選択を保つ
                    if (!row.IsSelected)
                    {
                        EventGrid.SelectedItem = ev;
                    }
                    _menuTarget = ev;
                    break;
                }
                if (dep is GroupItem { DataContext: CollectionViewGroup group } && group.Items.Count > 0 && group.Items[0] is LogEvent first)
                {
                    _menuTarget = first;
                    _menuOnGroupHeader = true;
                    break;
                }
            }

            if (_menuTarget is not { } t)
            {
                // 一覧の余白などを右クリックした場合はメニューを出さない
                e.Handled = true;
                return;
            }

            bool onRow = !_menuOnGroupHeader;
            MenuCopyUser.IsEnabled = onRow && t.PlayerName.Length > 0;
            MenuCopyWorld.IsEnabled = t.WorldName.Length > 0;
            MenuCopyDetail.IsEnabled = onRow && t.Detail.Length > 0;
            MenuCopyRows.IsEnabled = onRow;
            MenuFilterUser.IsEnabled = onRow && t.PlayerName.Length > 0;
            MenuFilterWorld.IsEnabled = t.WorldName.Length > 0;
            MenuOpenWorld.IsEnabled = t.WorldUrl.Length > 0;
            MenuOpenUser.IsEnabled = onRow && t.UserUrl.Length > 0;
            MenuOpenVideo.IsEnabled = onRow && t.EventType == LogEventType.Video;
        }

        private static DependencyObject? GetParent(DependencyObject d) =>
            d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);

        private void CopyText(string text)
        {
            if (text.Length == 0)
            {
                return;
            }
            try
            {
                // 他のアプリがクリップボードを使用中だと失敗することがあるため、リトライ付きの SetDataObject を使う
                Clipboard.SetDataObject(text, true);
                StatusText.Text = Loc.F("Status.Copied", text.Length > 80 ? text[..80] + "…" : text.ReplaceLineEndings(" "));
            }
            catch (System.Runtime.InteropServices.ExternalException ex)
            {
                logger.Warn(ex, "Failed to copy to clipboard");
                StatusText.Text = Loc.T("Status.CopyFailed");
            }
        }

        private void Copy_PlayerName(object sender, RoutedEventArgs e) => CopyText(_menuTarget?.PlayerName ?? "");
        private void Copy_WorldName(object sender, RoutedEventArgs e) => CopyText(_menuTarget?.WorldName ?? "");
        private void Copy_Detail(object sender, RoutedEventArgs e) => CopyText(_menuTarget?.Detail ?? "");

        private void Copy_Rows(object sender, RoutedEventArgs e)
        {
            var rows = EventGrid.SelectedItems.OfType<LogEvent>()
                .OrderBy(ev => ev.Timestamp, StringComparer.Ordinal)
                .Select(ev => string.Join("\t", ev.Timestamp, Loc.EventType(ev.EventType), ev.WorldName, ev.PlayerName, ev.DetailSummary));
            CopyText(string.Join(Environment.NewLine, rows));
        }

        private void Filter_ByPlayer(object sender, RoutedEventArgs e)
        {
            if (_menuTarget is { PlayerName.Length: > 0 } ev)
            {
                QueryUsername.Text = ev.PlayerName;
                Search();
            }
        }

        private void Filter_ByWorld(object sender, RoutedEventArgs e)
        {
            if (_menuTarget is { WorldName.Length: > 0 } ev)
            {
                QueryWorldname.Text = ev.WorldName;
                Search();
            }
        }

        private void Open_World(object sender, RoutedEventArgs e) => OpenBrowser(_menuTarget?.WorldUrl ?? "");
        private void Open_User(object sender, RoutedEventArgs e) => OpenBrowser(_menuTarget?.UserUrl ?? "");

        private void Open_Video(object sender, RoutedEventArgs e)
        {
            if (_menuTarget is { EventType: LogEventType.Video } ev)
            {
                OpenBrowser(ev.Detail);
            }
        }

        /// <summary>http(s) の URL のみ既定のブラウザで開く（ログ由来の文字列でローカルファイル等が開かれないようにする）</summary>
        public static void OpenBrowser(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                logger.Warn(ex, "Failed to open browser: {0}", url);
            }
        }

        // ---- メニュー ----

        private async void Menu_UpdateDb(object sender, RoutedEventArgs e) => await RunImportAsync();

        private void Menu_Export(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                FileName = $"vrc-log-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Filter = Loc.T("Export.Filter"),
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }
            try
            {
                // 画面の表示上限に関係なく、現在の検索条件に合う全件を出力する
                List<LogEvent> all;
                using (var store = new EventStore(DatabasePath))
                {
                    all = store.QueryAll(_lastFilter);
                }
                CsvExporter.WriteFile(dialog.FileName, all);
                MessageBox.Show(this, Loc.F("Export.Done", all.Count), "VRCLogAnalyzer");
            }
            catch (IOException ex)
            {
                logger.Error(ex, "CSV export failed");
                MessageBox.Show(this, Loc.F("Export.Failed", ex.Message), "VRCLogAnalyzer", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void Menu_Settings(object sender, RoutedEventArgs e)
        {
            var win = new SettingWindow(_settings, DatabasePath);
            if (ShowDialog(win) != true)
            {
                return;
            }
            _settings = AppSettings.Load(App.AppPath);
            ShowPendingBackupNotice();
            if (Loc.Resolve(_settings.Language) != Loc.CurrentLanguage)
            {
                Loc.Apply(_settings.Language);
                ApplyCulture();
                BindEvents();
            }
            await RunImportAsync();
        }

        /// <summary>設定で DB の場所を変えた先が旧形式だった場合などに、移行前バックアップを知らせる</summary>
        private void ShowPendingBackupNotice()
        {
            try
            {
                using var store = new EventStore(DatabasePath);
                if (store.TakePendingBackupNotice() is { } backup)
                {
                    MessageBox.Show(this, Loc.F("Db.BackupNotice", backup), "VRCLogAnalyzer", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (UnsupportedDatabaseException)
            {
                // この後の取り込みでメッセージを表示する
            }
        }

        /// <summary>下部の詳細欄（ID やエラーのスタックトレース）の表示を切り替える。既定は非表示</summary>
        private void ApplyDetailPane(bool show)
        {
            ShowDetailMenu.IsChecked = show;
            var visibility = show ? Visibility.Visible : Visibility.Collapsed;
            DetailSplitter.Visibility = visibility;
            DetailText.Visibility = visibility;
            SplitterRow.Height = new GridLength(show ? 5 : 0);
            DetailRow.MinHeight = show ? 40 : 0;
            DetailRow.Height = new GridLength(show ? 110 : 0);
        }

        private void Menu_ShowDetail(object sender, RoutedEventArgs e)
        {
            bool show = ShowDetailMenu.IsChecked;
            ApplyDetailPane(show);
            _settings.ShowDetail = show;
            try
            {
                _settings.Save();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 表示の切り替え自体はできているので、保存できなくても次回起動時に戻るだけ
                logger.Warn(ex, "Failed to save settings file");
            }
        }

        private void Menu_Exit(object sender, RoutedEventArgs e) => Close();
        private void Menu_FirstHelp(object sender, RoutedEventArgs e) => ShowDialog(new HelpWindow());
        private void Menu_Readme(object sender, RoutedEventArgs e) => OpenBrowser("https://github.com/sechiro/VRCLogAnalyzer");
        private void Menu_Credit(object sender, RoutedEventArgs e) => ShowDialog(new CreditWindow());

        private bool? ShowDialog(Window w)
        {
            w.Owner = this;
            return w.ShowDialog();
        }

        /// <summary>日付表示（DatePicker）の書式を表示言語に合わせる</summary>
        private void ApplyCulture()
        {
            Language = System.Windows.Markup.XmlLanguage.GetLanguage(Loc.CurrentLanguage);
        }
    }
}
