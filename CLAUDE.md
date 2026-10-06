# VRCLogAnalyzer

sechiro/VRCLogAnalyzer (MIT) のフォーク。git remote `upstream` が元リポジトリ。最終的に GitHub で fork として公開する。

## 言語

- このプロジェクトの標準言語は日本語。ユーザーへの返答・コードコメント・コミットメッセージ・ドキュメントは日本語で書く
- アプリの UI は日本語と英語に対応する（`src/VRCLogAnalyzer/Resources/Strings.*.xaml`）。UI に表示する文字列はコードや XAML に直書きせず、必ず両方の言語ファイルにキーを追加する
- アプリの動作ログ（NLog）のメッセージは英語（Issue に貼られることを想定）

## 公開時のプライバシー（必ず守る）

GitHub に公開するため、以下をリポジトリのファイル・コミットメッセージ・コミット履歴に残さない。

- PC のユーザー名、ローカルの個人フォルダパス（`C:\Users\<名前>\...`、スクラッチパッドのパスなど）
- VRChat の表示名・ユーザー ID・自分や他人の実際のログ内容（テストやドキュメントの例は架空の名前と `usr_00000000-...` 形式の ID を使う）
- メールアドレス（コミットの author は GitHub の noreply アドレスを使う）
- 実際の DB ファイル、アプリの動作ログ、スクリーンショットに写り込んだ個人情報

コミット前に `git grep` で上記が含まれていないことを確認する。パスは `%USERPROFILE%` や `~` など汎用的な表記で書く。

## ビルド・テスト

- `dotnet build` / `dotnet test`（.NET 10 SDK。TFM は `Directory.Build.props` で一元管理）
- 配布用ビルド: `dotnet publish src/VRCLogAnalyzer -c Release -p:PublishProfile=win-x64 -o artifacts/publish`（ランタイム同梱・exe 1 つ。`artifacts/` は git 管理外）。GitHub Actions も同じプロファイルを使う。リリースは `v*` タグの push で Release の下書きができる
- 想定環境は「Windows 11 の最近のビルド」。古い OS 向けの互換対応は考えない
- 実データで確認する場合は、ユーザーの本番 DB（`~/Documents/VRCLogAnalyzer/VRCLogAnalyzer.db`）に書き込まず、コピーを作って `VRCLogAnalyzer.exe /analyze /db <copy>` を使う
- **アプリは必ず PowerShell（`Start-Process`）から起動する。** Git Bash は `/analyze` などの `/` で始まる引数をパスに書き換えるため、オプションが効かずに本番 DB を開いてしまう（実際に一度起きた）。現在は不明な引数があると何もせず終了するが、それでも Git Bash からは起動しない

## DB の扱い

- `DatabaseGuard` が DB を開く前に中身を書き換えずに判定する: 無い → 新規作成 / 現行 → そのまま / 旧形式・古い構造 → **バックアップしてから移行**し、次に画面を開いたとき 1 度だけ通知 / 新しいバージョンの DB・認識できないファイル → 書き込まずに終了
- 構造バージョンは `AppMeta` の `SchemaVersion`。テーブル構造を変えたら `DatabaseGuard.CurrentSchemaVersion` を上げ、`EventStore` のコンストラクタに移行処理を追加する
- VRChat のログ形式が変わっただけなら DB の移行は不要（`LogParser` を直す）
- 取り込む種別を増やすなど解析結果が変わる修正をしたら `LogParser.Version` を上げる。取り込み済みのログも次回 1 度だけ読み直される（上げないと過去ログから新しい種別が拾われない）

## 構成

- `src/VRCLogAnalyzer.Core`: `LogParser`（1 ファイル単位でステートフルに解析）、`EventStore`（SQLite、`LogEvent` 1 テーブル）、`LogImporter`（差分取り込み）、`LegacyMigrator`（旧テーブルからの 1 回限りの移行）。UI 文字列を持たない
- `src/VRCLogAnalyzer`: WPF UI。DB 接続は操作ごとに開く（取り込みは別スレッドで別接続）
- 重複排除キーは `(SourceFile, LineNumber)` の一意インデックス。ログは追記のみなので行番号は安定

## ログ形式の前提（2026-10 時点で実ログ確認済み）

- 入室: `[Behaviour] Entering Room: <name>` → `[Behaviour] Joining wrld_...:<instance>` → `[Behaviour] Joining or Creating Room: <name>`
- 退室: `[Behaviour] OnLeftRoom`（`OnPlayerLeftRoom` は別物）
- ユーザー: `[Behaviour] OnPlayerJoined <name> (usr_...)` / `OnPlayerLeft <name> (usr_...)`、自分は `User Authenticated: <name> (usr_...)`
- 動画: `[Video Playback] Attempting to resolve URL '<url>'`
- インバイト: `Received Notification: <Notification from username:<name>, sender user id:usr_... to usr_... of type: invite|requestInvite, id: not_..., created at: MM/dd/yyyy HH:mm:ss UTC, details: {{worldId=..., worldName=...}}, ..., message: "...">`。ログインのたびに過去の通知が出し直されるため `NotificationId` で重複排除し、日時は created at（UTC→ローカル）を使う。requestInvite は 2026-10 時点で実ログ未確認（invite と同じ形式と想定）。自分がリクエストインバイトを送って相手が承諾した場合、自分のログには通常の invite として届く（requestInvite が届くのはお願いされた側のみ）。自分が送ったインバイトは `[API] Send notification:<...of type: invite...>` として残るが、現在は取り込んでいない
- `[API]` 行にユーザー/ワールドの詳細 JSON は出力されなくなっている
- VRChat 起動中は最新ログが書き込み中のため、`FileShare.ReadWrite` で開く必要がある（旧リリース版はこれが原因で落ちていた）
