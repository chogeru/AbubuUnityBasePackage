# Changelog

このファイルは [Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) 形式で、
バージョン番号は [Semantic Versioning](https://semver.org/lang/ja/) に従います。

導入側は git URL の末尾に `#v0.3.0` のようにタグを付けると、バージョンを固定できます。

## [Unreleased]

## [0.3.0] - 2026-10-19

### Breaking Changes
- **名前空間・型名の変更** (Unity / Unity Localization の型と衝突しないようにするため。アセンブリ名は変えていないので `ExcludedModules` などの文字列はそのまま使える。ScriptableObject / MonoBehaviour は .meta の GUID を維持しているので、作成済みのアセットやシーンの参照は切れない)

  | 変更前 | 変更後 |
  |---|---|
  | `namespace Abubu.Scene` | `Abubu.SceneFlow` |
  | `namespace Abubu.Input` / `Abubu.Input.UI` | `Abubu.Inputs` / `Abubu.Inputs.UI` |
  | `namespace Abubu.Graphics` | `Abubu.DisplaySettings` |
  | `LocalizationSettings` | `AbubuLocalizationSettings` |
  | `LocalizationTable` | `AbubuLocalizationTable` |
  | `LocalizedAsset<T>` | `LocalizedAssetKey<T>` |

  移行は `using Abubu.Scene;` → `using Abubu.SceneFlow;` のように `using` を置き換えるだけで済む (static ショートカット `Sound` / `Scenes` / `GameInput` などの名前は変わらない)
- 音量を Model (`IAudioVolumeModel`) と保存 (`AudioVolumeStore`) に分割 (`AudioVolumeInstaller` で登録)。画面設定 UI・音量 UI・ロード画面を `Abubu.Graphics.UI` / `Abubu.Audio.UI` / `Abubu.Scene.UI` アセンブリに分離し、`Abubu.Presentation` は MVP の基底 (`PresenterBase` / `ViewBase`) だけになった。`Saves.SaveAsync` / `LoadAsync` は `UniTask` を返す (`Abubu.Save.Reactive`)。`Sound.Preload` → `Sound.PreloadAsync`
- `IInputService` に `CurrentScheme` / `IsRebinding`、`IInputDeviceService` に `CurrentScheme` / `DeviceLost` / `DeviceRegained` (`Observable<InputDeviceConnection>`) を追加。`IRumbleBackend` / `IHapticsService.SupportsRumble`、`IInputGlyphService.GetCompositeGlyphs` を追加 (実装している場合は対応が必要)。`RebindAsync` は同時に呼ばれると false を返す。`RebindExcludedPaths` の既定に `<Mouse>/scroll` を追加
- キー割り当ての入力待ち中は B / Esc で戻らず、UI の選択も動かない。デバッグメニューをゲームパッドで操作している間はゲームの入力を止める
- アセンブリの分割: `LocalizedText` / `LocalizedImage` / `LocalizedAudio` / `SubtitleText` を `Abubu.Localization.UI` に移動 (.meta の GUID は維持)。言語ごとのフォントは `LocalizationSettings.Fonts` から `LocalizationFontSettings` (Module Settings に入れる) / `ILocalizedFontService` に移動し、`ILocalizationService.GetFont` を削除。翻訳の連携 (`LocalizedDialogLabels` / `LocalizedGraphicsSettingsLabels` など) を連携用アセンブリに移動し、`Abubu.Installer` は uGUI / TMP に依存しなくなった
- `AbubuSettings.Ui` を削除 (`AbubuUiSettings` は `Module Settings` に入れる)。`AbubuSettings` のフィールド名を `Effect` → `Effects`、`App` → `AppLifecycle`、`GraphicsOptions` → `Graphics` に変更 (保存済みの値は引き継がれる)
- `AbubuUiSettings.SelectFirstOnOpen` を `SelectOnOpen` (Never / Auto / Always) に変更。`BackOnCancel` の既定値をオンに変更
- `Loc.Plural` → `Loc.GetPlural`、`Loc.Asset` → `Loc.GetAsset`。`Loc.OnLanguageChanged` は購読時の現在値を流さず、変更だけを流す
- `Popups` / `Screens` / `UiNavigation` / `Loc` / `AppLifecycle` のショートカットは、サービスが無いときに警告ではなくエラーを出す。`Popups.Confirm` / `Alert` はダイアログを出せないときに例外を投げる
- `AbubuInstaller` が任意モジュールを探す対象を「名前が Abubu. で始まる」から「Abubu.Core を参照している (または名前が Abubu. で始まる)」に変更
- `IInputService` に `ResetBinding` / `SetBindingOverride` / `SaveBindings` / `BindingsChanged` を追加 (実装している場合は対応が必要)。`RebindAsync` は既定で同じ種類のデバイスの操作だけを受け付ける (`RebindMatchBindingDevice`)
- `ISaveService.Save` が bool を返すように変更し、`ForceSave<T>` を追加。新しいバージョンで保存されたデータは読み込まず (`TryLoad` が false)、`キー.newer` に 1 回だけ退避する。そのキーへの `Save` は `ForceSave` か `Delete` まで false を返す。`.bak` から復旧しても `.bak` は残す。`Delete` は `.broken` / `.newer` / `.tmp` も消す。File 保存の既定フォルダを `Save` から `AbubuSave` に変更 (既存の設定アセットの値はそのまま)
- `ISceneService` の Load / LoadWithPayload / Reload / LoadAdditive / Unload が `UniTask<bool>` を返すように変更 (遷移中などで受け付けなかった場合は false)。キャンセル時は元のシーンにフェードで戻ってから `OperationCanceledException` を投げる
- `IEventBus.Receive` が Subject を返さないように変更 (購読専用)
- static ショートカットは、サービスが見つからないとき何もせずに戻る代わりに、型ごとに 1 回エラーを出すように変更。`Saves.Save` は bool を返す
- `ViewBase` の Start / OnDestroy を private にし、派生クラスは `OnViewStart` / `OnViewDestroy` を使うように変更
- `GraphicsSettingsModel` は状態だけを持つように変更 (変更は `Set*`、反映は `GraphicsSettingsApplier`、保存は `GraphicsSettingsStore`)。初回起動時に既定値を反映しない
- `AudioVolumeModel` の音量を読み取り専用にし、`Set(VolumeChannel, float)` / `SetMute(bool)` / `Get(VolumeChannel)` / `Reload()` を追加
- `IInputService` は既定で ActionMap の有効・無効を切り替えないように変更。ポーズ中に `Player` を無効にしていた場合は `PauseDisabledMaps` に `Player` を追加する
- Abubu と関係のない ProjectContext を自動で作らないように変更
- `Log` クラスを `AbubuLog` に改名 (このバージョンで追加したもの)

### Added
- ゲームパッドが抜けたときのポーズ (`PauseOnGamepadDisconnect`、既定オン) と「コントローラーを接続してください」のダイアログ (`GamepadReconnectBridge`)、ボタン案内バー (`UiButtonPromptBar` / `InputPromptLabel` / `InputGlyphText`) と一番手前の UI の案内の受け渡し (`UiPromptButtonSource`)、`GameObject > Abubu > Rebind Panel`、`Validate Setup` で InputSystemUIInputModule のアクションがプロジェクト共通のものと違う場合の確認
- UI のコントローラー対応: `UiFocusScope` (選択をポップアップの中に閉じ込める。ポップアップには自動で付く)、下に重なったポップアップの操作を止める、`AbubuUiSettings.SelectOnOpen` (Auto: ゲームパッド操作中だけ選択し、マウス操作中は裏の選択を外す)、確認ダイアログの初期選択を Cancel に (`ConfirmDefaultButton`)、`IUiBackBlocker` と `UiNavigation.HandledBackThisFrame`、方向入力・決定で選び直すときはフレームの最後に行う
- `UiEventSystemProvider` (EventSystem が無ければシーンをまたいで残るものを用意する。`EnsureEventSystem`)、`UiSafeArea` (`UseSafeArea`)、`UiHoldButton` (長押しで確定)、`IUiPromptProvider` / `IUiPromptService` (操作の案内)、`IPopupService.Changed` / `IScreenService.Changed`、`AbubuUiSettings.DefaultFont` / `DefaultTmpFont` / `WidgetStyle` / トーストのフェード時間
- `IAppQuitHandler` / `IAppSuspendFinalizer` (中断・終了の通知がすべて終わってから呼ばれる。セーブの Flush に使う)
- `LocalizationSettings.SaveKey`、`AbubuModules.Pool` (プールを独立して切り替え)、`IAbubuSetupStep` (Setup Project の追加処理)、`Setup Project` で `AbubuUiSettings` / `AbubuInputSettings` を作る、`Validate Setup` の入力・EventSystem の確認
- 入力: `IInputDeviceService` (デバイスとゲームパッドの種類の判別)、`IHapticsService` (振動)、`InputGlyphs` / `InputGlyphImage` (ボタンのアイコン)、`InputCursorAutoHide`、キー割り当て画面 (`Abubu.Input.UI` の RebindView / RebindPresenter / IRebindView)。ローカライズが有効なら割り当て画面の文言を翻訳する (`LocalizedRebindLabels`)
- セーブスロット (`ISaveSlotService` / `Saves.Slots`)、非同期のセーブ (`IAsyncSaveService` の SaveAsync / LoadAsync / Flush、バックグラウンドでの書き込み)、保存データの変換 (`ISaveTransform`: `SaveSettings.Compress` の GZip、`AesSaveTransform`)、失敗の通知 (`ISaveFailureNotifier` / `SaveFailureReason`)。アプリの中断・終了時に自動で Flush する
- ローカライズの拡充: 言語コードの正規化と親言語へのフォールバック (`zh-Hant-TW` → `zh-Hant` → `zh`)、言語ごとの書式 (`CultureInfo`)、名前付きプレースホルダー (`{score}`)、複数形 (`GetPlural` / `Loc.Plural`)、言語ごとのフォント (`LocalizationSettings.Fonts`、`LocalizedText.KeepFont`)、言語ごとのアセット (`LocalizationTable.Assets`、`LocalizedImage` / `LocalizedAudio` / `LocalizedAsset<T>`)、非同期の取得元 (`IAsyncLocalizationSource` / `SetLanguageAsync` / `IsLoading` / `WaitReadyAsync`、起動処理で待つ)、`MissingKeyBehavior`、疑似ローカライズ、`ILocalizationService.Changed`
- Unity Localization (com.unity.localization) 連携 (`Abubu.Localization.UnityLocalization`、導入時のみ有効)
- 翻訳テーブルの CSV / TSV 書き出し・読み込み (Tools > Abubu > Localization)、`Validate Setup` で LocalizedText のキーの存在を確認
- 字幕 (`ISubtitleService` / `SubtitleText`)。ボイスの再生に合わせて Subtitle Key の翻訳を表示する
- パッケージ自身の文言 (画面設定の選択肢・ダイアログのボタン) のローカライズと、英語・日本語の組み込み文言 (`AbubuBuiltInStrings.csv`)。`Integration.LocalizePackageUi` で無効化できる
- `Popups.Confirm` / `Popups.Alert` / `Popups.Toast` (`IDialogService` / `IToastService`)。設定不要の組み込みの見た目と、`Tools > Abubu > Create UI Templates` によるプレハブ化
- コントローラー対応: `UiNavigation.EnsureSelection`、方向入力・ゲームパッドへの持ち替え時に一番手前の UI を選び直す (`KeepFocusOnNavigate`)、`SelectFirstOnOpen`、`UiScrollToSelection`、`IPopupService.Current`
- `Edit > Project Settings > Abubu` (`AbubuEditorSettings`) : Setup Project の出力先、Generate Keys の出力先・名前空間・asmdef 生成・Packages の検索
- ログ (`AbubuLog` / `LogChannel`) : カテゴリ別の ON/OFF、最低レベル、リリースビルドでの Debug / Info の除去 (`ABUBU_LOG_VERBOSE` で残せる)、ログのファイル出力 (`AbubuSettings > Log`)
- ローカライズ (`ILocalizationService` / `Loc` / `LocalizationTable` / `LocalizedText`) : ScriptableObject と CSV の翻訳テーブル、端末言語の判定、選んだ言語の保存、フォールバック、取得元の差し替え (`ILocalizationSource`)。`Validate Setup` の未翻訳チェックと `Generate Keys` の `LocKeys`、`Setup Project` での `LocalizationTable.asset` の生成
- アプリのライフサイクル (`IAppLifecycle` / `AppLifecycle` / `IAppSuspendHandler`) : 中断・復帰・終了の通知、中断時の自動保存 (`SuspendTriggers` で選択)、フォーカス喪失時のポーズ・消音 (既定オフ)
- 画面遷移 (`IScreenService` / `Screens` / `IScreen` / `UiNavigation`) : 全画面 UI の履歴管理 (Push / Back / Replace / BackTo / BackToRoot)、Cancel 入力で戻る (`AbubuUiSettings.BackOnCancel`、Input System 導入時)
- ポップアップ・画面の共通機能 : 開閉アニメーション (`IUiTransition`)、ゲームパッド用の初期選択 (`UiInitialSelection`)、生成方法の差し替え (`IUiFactory`)、`BackdropPrefab`、`PopupCanvasPrefab` / `ScreenCanvasPrefab`、`MatchWidthOrHeight`、`IPopupService.Close` / `Popups.Close`
- 機能ごとの Installer (`Abubu.Installers.SaveInstaller` など) と、`AbubuSettings.Modules` による機能ごとの有効・無効、任意モジュールの自動インストールの除外 (`ExcludedModules`)
- `AbubuSettings.Ui` / `AbubuSettings.ModuleSettings` : UI・任意モジュールの設定アセットを AbubuSettings から渡せる (未設定なら従来どおり Resources)
- `AbubuSettings.Integration.ResumePauseOnSceneChange` : シーン遷移時のポーズ解除を止められる
- `IBootService.State` / `Failures`、`IBootTask.IsCritical` (失敗すると Ready にならず `WaitReadyAsync` が `BootFailedException` を投げる)、`ABUBU_DISABLE_AUTO_BOOTSTRAP`
- `ISceneTransitionHook` (遷移前後の非同期フック)、`SceneSettings.FadePrefab`。FadeTransition は最初のフェードまで生成しない
- `ITimeScaleController` (timeScale の書き換え方の差し替え)。ポーズ中に他の処理が timeScale を変えた場合は、再開時にその値を優先する
- `IPoolService.Configure` / `ReturnAll(Scene)` / `Clear` / `GetStats` / `ReturnAfter(…, ignoreTimeScale)`、`EffectLibrary` の Includes、`EffectEntry.MaxPooled`、`EffectSettings.DefaultLifetime`
- `SoundSettings.SourceTemplates` (カテゴリごとの AudioSource プレハブ)・ミキサーのグループ名の設定・`MixerMinDecibel`・`VolumeSave`
- View のインターフェイス (`IVolumeSettingsView` / `IGraphicsSettingsView` / `ISceneLoadingView`) と `IGraphicsSettingsLabels`
- 依存パッケージのアセンブリによる判定 (Asset Store 等で導入済みなら重複して入れない)、`Check Dependencies...` / `Ask On Startup` メニュー、「今後表示しない」
- ビルド時の `link.xml` の生成 (`AbubuLinkXmlGenerator`)
- `AbubuMessagePipe.SetGlobalProvider`。自前の `BindMessagePipe` があればそれを使い回す
- サンプルの生成物を `Generated` フォルダにまとめ、サンプル専用ライブラリを Includes で参照する形に変更。`Tools > Abubu > Remove Sample Registration`
- `ABUBU_DISABLE_INPUT_MODULE` / `ABUBU_DEBUG_OVERLAY_IN_RELEASE`
- `Validate Setup` : 保存先フォルダ名が `Save` のときに衝突の可能性を知らせる
- ボイスカテゴリ (`SoundCategory.Voice` / `IVoicePlayer` / `ISoundService.Voice` / `Sound.PlayVoice`)。SoundLibrary に Voice 欄、音量 (`AudioVolumeModel.Voice`)・ミキサー (`VoiceVolume` / `Voice` グループ)・ポーズ (`PauseVoice`)・音量設定 UI のスライダーに対応。ボイスは 1 本ずつ再生され、新しいボイスが前のボイスを置き換える
- BGM ダッキング : ボイス再生中は BGM を下げる (`SoundSettings > Ducking`)
- `SoundEntry.Priority` : SE の同時発音数が上限のとき、優先度の低い音から止める
- `SoundEntry.AvoidRepeat` : 複数 Clip のとき直前と同じ Clip を続けて選ばない (既定でオン)
- `Validate Setup` と EditMode テストに「ファイル名と一致せず Missing Script になるクラス」の検出を追加
- `Sound.*` ショートカットで `ISoundService` が見つからないとき、一度だけ警告を表示
- `ISaveBackupStorage` : 本体が壊れていたときに 1 つ前のデータ (`.bak`) から復旧する (`FileSaveStorage` が実装)
- `AbubuUiSettings.CloseOnSceneChange` : シーンが切り替わったらポップアップをすべて閉じる (既定でオン)
- `Generate Keys` が Build Profiles のシーン名から `SceneKeys` も生成
- `GameInput.Interact` / `IsPressed` / `CurrentDevice` / `SetGameplayEnabled`
- `DebugOverlay` のキー切り替えが新 Input System のみのプロジェクトでも動作

### Changed
- `Generate Keys` で別のキーが同じ定数名になる場合、連番ではなくキーから計算した固定の接尾辞を付けるように変更 (キーを追加しても既存の定数名が変わらない)。定数の並び順は定数名順になる
- `Validate Setup` の Missing Script の検出で、プロジェクトの全スクリプトを読み込まないように変更 (対象の型の名前で絞り込む)
- `LocalizationService` の書式化で、値の無いプレースホルダーをエラーにせずそのまま残すように変更
- Abubu.Audio が uPools に依存しないように変更
- プールで親を指定せずに借りたオブジェクトをアクティブなシーンに置くように変更 (以前は DontDestroyOnLoad)
- `BootPlayModeHook` が他の拡張の `playModeStartScene` を上書きしないように変更
- AbubuInstaller は主要なサービスがすでにバインドされていれば登録しない (自前の実装を先にバインドして差し替えられる)
- ポップアップをシーン切り替え時に閉じる条件を「アクティブシーンの変更」から「開いたときのシーンの破棄」に変更 (加算ロードでの SetActiveScene では閉じない)
- ポップアップを外から Destroy したとき、Backdrop・ポーズ・Count をその場で更新するように変更
- ポップアップのプレハブを DI コンテナ経由で生成するように変更 (ポップアップのコンポーネントで `[Inject]` が使える)
- static ショートカット (`Sound` / `GameInput` など) が取得したサービスをキャッシュし、呼び出しごとにコンテナを引かないように変更
- プールの `IPoolCallbackReceiver` をインスタンス生成時に 1 回だけ集めるように変更 (Rent / Return ごとの GC Alloc を削減)。生成後に追加したコンポーネントには通知されない

### Fixed
- 中断時の `OnSuspend` の購読や後から登録した `IAppSuspendHandler` で始まった非同期の書き込みが Flush されない問題 (Flush を最後に行う)
- 非同期の翻訳の取得元で、起動処理 (Boot) が無効だと翻訳が読み込まれない問題 (Initialize で読み込みを始める)
- `ISoundService` だけを差し替えると、音量や各プレイヤーが登録されず解決に失敗する問題。`EffectSeHook` は `ISoundService.Se` を使う
- `SaveSettings.Compress` が、利用者が別の `ISaveTransform` を登録していると無視される問題
- ゲームパッドでポップアップの裏のボタンに選択が移って押せてしまう問題、方向入力で選び直したときに 2 つ目の項目が選ばれる問題、ポップアップが開いている間に画面の切り替えで選択を奪われる問題
- ポップアップの OnClosing 内で Pop を呼ぶと無限再帰になる問題 (スタックから外してから閉じるように変更)
- ポップアップの OnClosing で例外が出ると、そのポップアップが閉じられなくなる問題
- ポップアップの生成に失敗すると Backdrop だけが残り、画面全体が操作できなくなる問題
- `Generate Keys` でキーに改行などの制御文字が含まれると、生成したファイルがコンパイルエラーになる問題
- `EffectLibrary` が `EffectService.cs` 内にあり Missing Script になる問題 (専用ファイルに分離)
- `ISePlayer.PlayAsync` がポーズ中も待ち時間が進む問題
- AudioMixer の Exposed Parameter 未設定の警告が音量変更のたびに出る問題 (パラメータごとに 1 回)
- シーン遷移後も前のシーンのポップアップが残り、ポーズだけ解除される問題
- UI モジュールの Installer が二重に実行されていた問題 (`AbubuModuleInstaller` 属性の重複)
- 読み込めなかったセーブデータ (破損 / マイグレーション未登録) が次の保存で失われる問題 (`キー.broken` に退避)
- Domain Reload 無効時、`Sound.*` の「サービスが見つからない」警告が 2 回目以降の再生で出ない問題
- BGM のクロスフェードをキャンセルすると、音量が途中のまま残る問題 (切り替えを完了させる)

## [0.2.0] - 2026-10-01

### Added
- `LICENSE.md` / `THIRD-PARTY-NOTICES.md` / `CHANGELOG.md`
- `Tools > Abubu > Validate Setup` : ProjectContext・ライブラリ・Boot シーンなどの診断
- `Tools > Abubu > Generate Keys` : SoundLibrary / EffectLibrary からキー定数 (`SoundKeys` / `EffectKeys`) を自動生成
- 存在しないキーを指定したときの警告に「近いキー候補」を表示
- `Abubu.UI` : ポップアップのスタック管理 (`IPopupService` / `Popups`)
- `DebugOverlay` : FPS / メモリ / 任意の項目を表示するデバッグ表示 (F1 で切り替え)
- テスト: Pool / Effect (PlayMode)、Input (EditMode)、キー候補検索
- `Tools > Abubu > Install Dependencies > Check Versions` : manifest.json と推奨バージョンの差分確認
- `.editorconfig` / `.gitattributes`
- README: Input / Pause / UI / デバッグ、対応プラットフォームの節

### Changed
- CI で PlayMode テストも実行するように変更

## [0.1.0]

- 初回リリース (Bootstrap / シーン遷移 / サウンド / プール / エフェクト / セーブ / イベント / Addressables / 画面設定 / MVP)
