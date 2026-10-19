# Abubu Base Package

ゲームジャム / 新規プロジェクトの「最初の 30 分」を省略するための基盤パッケージです。

| 機能 | 概要 |
|---|---|
| Bootstrap | どのシーンから再生しても共通初期化が先に走る。任意で Boot シーン経由の起動 |
| シーン遷移 | フェード / ロード画面 / 遷移先へのデータ受け渡し |
| サウンド | BGM クロスフェード / SE (プール・同時発音数制限・連打防止) / 環境音レイヤー / ボイス (重ならない) / AudioMixer 対応 |
| プール / エフェクト | プレハブ単位の PoolManager、エフェクト + SE の同時再生と自動返却 |
| セーブ | File / PlayerPrefs 切り替え (WebGL は自動で PlayerPrefs)、バージョン管理とマイグレーション |
| イベントバス | 登録不要の `IEventBus` (R3)、MessagePipe の Zenject 連携 |
| Addressables | 参照カウント付きロード、シーン単位の自動解放、先読み |
| 画面設定 | 解像度 / 表示モード / 画質 / VSync / FPS の Model + 設定 UI |
| MVP | Presenter 基底クラス、シーンに置くだけで動く View |
| 入力 | Input System のラッパー (Move / Jump など)、キーリバインドの保存、ポーズ連動 |
| ポーズ | 複数の呼び出し元を数えるポーズ管理 (`Time.timeScale` と音・入力へ反映) |
| ポップアップ | ダイアログのスタック管理、背面ブロック、ポーズ連動、開閉アニメーション、ゲームパッドの初期選択 |
| 画面遷移 | 全画面 UI の履歴管理 (戻る / 最初に戻る / 置き換え)、Cancel 入力で「戻る」 |
| ローカライズ | キー → 各言語の文字列 (ScriptableObject / CSV / 独自の取得元)、言語の保存、Text / TMP の自動切り替え |
| ライフサイクル | バックグラウンド移行・フォーカス喪失・終了の通知と自動保存、フォーカス喪失時のポーズ / 消音 |
| ログ | カテゴリ別の ON/OFF、リリースビルドでの Debug / Info の除去、ログのファイル出力 |
| デバッグ | FPS / メモリ / 任意の項目を重ねて表示する `DebugOverlay` |
| 開発支援 | `Validate Setup` による診断、`Generate Keys` によるキー定数の自動生成 |

技術スタック: Zenject (DI) + UniTask + R3 + uPools + LitMotion (+ MessagePipe / Addressables は任意)

## インストール

### 1. パッケージを追加

Package Manager → `+` → **Add package from git URL...**

```
https://github.com/chogeru/AbubuUnityBasePackage.git?path=AbubuUnityBasePackage/Packages/com.abubu.basepackage
```

バージョンを固定したいときは、URL の末尾にタグを付けます（変更履歴は `CHANGELOG.md`）。

```
https://github.com/chogeru/AbubuUnityBasePackage.git?path=AbubuUnityBasePackage/Packages/com.abubu.basepackage#v0.2.0
```

### 2. 依存パッケージを入れる

追加直後に「依存パッケージが不足しています」ダイアログが出るので **インストール** を押す。
(出なかった場合は `Tools > Abubu > Install Dependencies > Required`)

| 区分 | パッケージ |
|---|---|
| 必須 | UniTask, R3 (+NuGetForUnity), Zenject, uPools, LitMotion |
| 任意 (`All`) | MessagePipe, StateVariable, UnityProcessManager, SerializableInterface, Addressables, SmartAddresser, LucidRandom, TweenPlayables, SceneSystem |

- 推奨バージョンの正本は `Setup/Editor/AbubuDependencyInstaller.cs` の表です。導入済みの版と食い違っていないかは `Tools > Abubu > Install Dependencies > Check Versions` で確認できます。
- 必須パッケージが揃うまで Abubu.Runtime はコンパイル対象外になるため、導入途中でもエラーで止まりません。
- 依存パッケージはアセンブリの有無で判定するので、Asset Store などから別の方法で導入済みなら重複して入れません。その場合は Scripting Define Symbols に `ABUBU_DEPS_*` を追加するよう案内します。ダイアログは「今後表示しない」で止められ、`Tools > Abubu > Install Dependencies > Check Dependencies...` / `Ask On Startup` から再確認・再設定できます。`packages.config` と `manifest.json` の既存の内容は保持されます。
- MessagePipe / Addressables は入っていれば自動で有効になります。
- R3 本体は NuGet 配布のため `Assets/packages.config` に自動で追記され、NuGetForUnity が復元します。

### 3. セットアップ

`Tools > Abubu > Setup Project` を実行すると以下が生成されます。

- `Assets/Abubu/AbubuSettings.asset` … 全機能の設定
- `Assets/Abubu/SoundLibrary.asset` … サウンド定義 (キー → AudioClip)
- `Assets/Abubu/EffectLibrary.asset` … エフェクト定義 (キー → Prefab + SE)
- `Assets/Abubu/LocalizationTable.asset` … 翻訳テーブル (キー → 各言語の文字列)
- `Assets/Resources/ProjectContext.prefab` … `AbubuInstaller` 入り (既存の ProjectContext があればそこに追加)

これで完了です。**シーンに SceneContext を置かなくても、どのシーンから再生しても動きます。**

## 使い方 (static ショートカット)

ゲームジャムならまずはこれだけで十分です。

```csharp
using Abubu;

// サウンド
Sound.PlaySe("jump");
Sound.PlaySe("explosion", transform.position); // 3D
Sound.PlayBgm("stage1");                       // クロスフェード
Sound.PlayAmbient("wind");                     // 環境音は複数重ねられる
Sound.StopAmbient("wind");
Sound.PlayVoice("hero_hello");                 // ボイスは SoundLibrary の Voice 欄。再生中のボイスは置き換わる
Sound.StopVoice();

// シーン
Scenes.Load("Game");
Scenes.LoadWith("Result", new ResultData(score)); // データを渡す
Scenes.Reload();

// エフェクト / プール
Fx.Play("explosion", enemy.position);     // SE も一緒に鳴り、終わったら自動でプールに戻る
var bullet = Pools.Rent(bulletPrefab, muzzle.position, muzzle.rotation);
Pools.ReturnAfter(bullet, 3f);

// イベント
GameEvents.Publish(new EnemyDied(100));
GameEvents.Receive<EnemyDied>().Subscribe(e => score += e.Score).AddTo(this);

// セーブ
Saves.Save("highscore", 1200);
var best = Saves.Load("highscore", 0);
await Saves.SaveAsync("progress", progress);   // 書き込みはバックグラウンド (UniTask)
await Saves.FlushAsync();                      // 書き込み待ちをすべて終わらせる
Saves.OnFailed.Subscribe(f => Popups.Toast($"保存に失敗しました ({f.Reason})")).AddTo(this);

// 入力 (Input System 導入時)
var move = GameInput.Move;
GameInput.Jump.Subscribe(_ => Jump()).AddTo(this);

// ダイアログ / トースト (設定不要。見た目はプレハブで差し替え可)
if (await Popups.Confirm("セーブデータを削除しますか？")) DeleteSave();
await Popups.Alert("通信に失敗しました", title: "エラー");
Popups.Toast("保存しました");

// ポップアップ
Popups.Push(settingsPopupPrefab);
Popups.Pop();

// 画面遷移 (戻れる履歴つき)
Screens.Push(settingsScreenPrefab);
Screens.Back();
UiNavigation.Back();   // ポップアップがあれば閉じ、無ければ前の画面に戻る

// ローカライズ
label.text = Loc.Get("menu.start");
label.text = Loc.Get("result.score", score);   // "スコア: {0:N0}" → 言語に合った桁区切り
label.text = Loc.GetPlural("item.count", count);  // item.count.one / item.count.other から選ぶ
Loc.SetLanguage("en");

// ライフサイクル (バックグラウンドに回る・終了する直前)
AppLifecycle.OnSuspend.Subscribe(_ => Saves.Save("progress", progress)).AddTo(this);

// ログ
AbubuLog.Info("Shop", "購入しました");
AbubuLog.Debug("AI", $"target = {target}");   // リリースビルドでは呼び出しごと消える
```

### キーの定数化

`"jump"` のような文字列は打ち間違いが実行時まで分かりません。`Tools > Abubu > Generate Keys` を実行すると、SoundLibrary / EffectLibrary / LocalizationTable / Build Profiles のシーン一覧から定数クラスが `Assets/Abubu/Generated/AbubuKeys.cs` に生成されます (ライブラリやシーン一覧を編集したら再実行してください)。

- 出力先・名前空間・asmdef の生成 (asmdef で分割したプロジェクトから参照するため)・Packages 以下も探すかは `Edit > Project Settings > Abubu` で設定します。設定は `ProjectSettings/AbubuEditorSettings.asset` に保存されるので、git で共有できます。
- 別のキーが同じ定数名になる場合 (`menu_start` と `menu.start` など) は、文字列順で最初のキー以外にキーから計算した固定の接尾辞 (`MenuStart_1a2b`) が付きます。関係ないキーを追加しても、既存の定数名は変わりません。

```csharp
Sound.PlaySe(SoundKeys.Se.Jump);
Sound.PlayBgm(SoundKeys.Bgm.Stage1);
Fx.Play(EffectKeys.Explosion, enemy.position);
Scenes.Load(SceneKeys.Game);
Loc.Get(LocKeys.MenuStart);              // "menu.start"
```

存在しないキーを指定したときの警告には「もしかして: "jump"」のように近いキーが表示されます。

### static ショートカットの注意

`Sound` / `Scenes` / `Fx` / `Pools` / `GameEvents` / `Saves` / `GamePause` / `GameInput` / `Popups` / `Screens` / `UiNavigation` / `Loc` / `AppLifecycle` は `ProjectContext` を介したグローバルなアクセスです。

- `ProjectContext` が無い・アプリ終了中は、何もせずに戻ります (戻り値があるものは既定値)。サービスが見つからないとき (機能を無効にしている・未セットアップ) は、型ごとに 1 回だけ**エラー**を出します。各クラスの `Service` プロパティ (存在確認用) だけは、見つからなくても**警告**です。
- `Popups.Confirm` / `Popups.Alert` は、ダイアログを出せないときに例外を投げます (`false` を返すと「キャンセルされた」と区別できないため)。
- `GamePause.Pause(this)` / `Resume(this)` / `Toggle(this)` / `IsPaused` でポーズできます (`IPauseService` と同じく、呼び出し元ごとに数えます)。
- Enter Play Mode Options (Domain Reload 無効) でも、再生のたびに内部状態をリセットするので前回の状態は残りません。
- 単体テストや再利用するコードでは、`[Inject]` で `ISoundService` などを受け取る形にしてください (テストで差し替えられます)。

## ノーコード

| メニュー / コンポーネント | 用途 |
|---|---|
| `GameObject > Abubu > Volume Settings Panel` | 音量設定 UI を生成 (Master / BGM / SE / 環境音 / ボイス / ミュート) |
| `GameObject > Abubu > Graphics Settings Panel` | 画面設定 UI を生成 (解像度 / 表示モード / 画質 / FPS / VSync) |
| `GameObject > Abubu > Debug Overlay` | FPS / メモリ表示をシーンに追加 |
| `Play BGM On Start` | シーン開始時に BGM 再生 |
| `Play Ambient On Start` | シーン開始時に環境音レイヤー再生 |
| `Play SE On Click` | Button 押下で SE |
| `Load Scene On Click` | Button 押下でシーン遷移 |
| `Localized Text` | Text / TextMeshPro にキーの翻訳を表示し、言語が変わったら書き換える |
| `UI Initial Selection` | ポップアップ・画面が表示されたときに選択するボタンを指定する (ゲームパッド操作用) |

## DI で使う (推奨)

| インターフェース | 内容 |
|---|---|
| `ISoundService` (`.Bgm` / `.Se` / `.Ambient` / `.Voice` / `.Volume`) | サウンド |
| `ISceneService` | シーン遷移 (`IsLoading`, `Progress` を購読可能) |
| `IEffectService` / `IPoolService` | エフェクト / オブジェクトプール |
| `ISaveService` | セーブ |
| `IEventBus` / `IPublisher<T>` `ISubscriber<T>` (MessagePipe) | イベント |
| `IAssetService` | Addressables (導入時のみ) |
| `GraphicsSettingsModel` / `AudioVolumeModel` | 設定値 (ReactiveProperty) |
| `IBootService` | 起動処理の完了待ち |
| `IPauseService` | ポーズ (owner 単位で数える) |
| `IInputService` | 入力 (Input System 導入時) |
| `IPopupService` | ポップアップのスタック管理 |
| `IScreenService` | 画面遷移の履歴管理 |
| `ILocalizationService` | ローカライズ (`Language` を購読可能) |
| `IAppLifecycle` / `IAppSuspendHandler` | 中断・復帰・終了の通知 / 中断時に呼ばれる処理 |

```csharp
public sealed class Player : MonoBehaviour
{
    [Inject] private ISoundService _sound;
    [Inject] private ISceneService _scene;

    private async UniTaskVoid GameOver()
    {
        _sound.Se.Play("dead");
        await _sound.Bgm.StopAsync(fadeDuration: 2f);
        // 遷移中などで受け付けられなかった場合は false
        if (!await _scene.LoadWithPayloadAsync("Result", new ResultData(score))) return;
    }
}

// 遷移先 (SceneContext があるシーン) では Inject で受け取れる
public sealed class ResultPresenter : IInitializable
{
    [Inject] private ResultData _data;
}
// SceneContext が無いシーンでは Scenes.TryGetPayload<ResultData>(out var data)
```

## MVP

```csharp
// Model
public sealed class ScoreModel { public ReactiveProperty<int> Score { get; } = new(0); }

// View : シーンに置くだけで Presenter が自動生成される
public sealed class ScoreView : ViewBase<ScorePresenter>
{
    [SerializeField] private Text label;
    public void SetScore(int v) => label.text = v.ToString();
}

// Presenter : 依存はコンストラクタで受け取る (SceneContext → ProjectContext の順に解決)
public sealed class ScorePresenter : PresenterBase
{
    private readonly ScoreModel _model; private readonly ScoreView _view;
    public ScorePresenter(ScoreModel model, ScoreView view) { _model = model; _view = view; }
    protected override void OnInitialize() => _model.Score.Subscribe(_view.SetScore).AddTo(Disposables);
}
```

View は `IVolumeSettingsView` / `IGraphicsSettingsView` / `ISceneLoadingView` などのインターフェイス越しに使われるので、TextMeshPro や UI Toolkit の View に差し替えられます。`ViewBase` を継承する場合、開始・破棄時の処理は `OnViewStart` / `OnViewDestroy` に書きます。

## 機能詳細

### シーン遷移

- `ISceneService` の `LoadAsync` / `LoadWithPayloadAsync` / `ReloadAsync` / `LoadAdditiveAsync` / `UnloadAsync` は `UniTask<bool>` を返します。遷移中に呼ばれたなど、受け付けなかった場合は false です。
- 遷移をキャンセルすると、元のシーンにフェードで戻ってから `OperationCanceledException` を投げます。
- `ISceneTransitionHook` をバインドすると、遷移の前後に非同期の処理 (セーブ、アセットの解放など) を挟めます。
- フェード用のオブジェクトは最初のフェードまで作られません。見た目は `SceneSettings.FadePrefab` で差し替えられます。VR では Screen Space のフェードが見えないので、`ISceneTransition` を自前で実装してバインドしてください。

### Bootstrap

- `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` で最初のシーンより先に ProjectContext を生成します。
- 起動時の初期化処理は `IBootTask` を実装して登録します。シーン側は `await _boot.WaitReadyAsync()` で完了を待てます。

```csharp
public sealed class LoadCatalogTask : IBootTask
{
    public int Order => 0;
    public async UniTask RunAsync(CancellationToken ct) { /* ... */ }
}
// Installer: Container.Bind<IBootTask>().To<LoadCatalogTask>().AsSingle();
```

- `IBootService.State` / `Failures` で起動処理の状態と失敗したタスクが分かります。`IBootTask.IsCritical` が true のタスクが失敗すると Ready にならず、`WaitReadyAsync` は `BootFailedException` を投げます。シーン側の初期化は `await _boot.WaitReadyAsync()` のあとに書いてください。
- 自動の起動処理 (ProjectContext の自動生成) を止めたい場合は、Scripting Define Symbols に `ABUBU_DISABLE_AUTO_BOOTSTRAP` を追加します。Abubu と関係のない ProjectContext は自動では作りません。
- `AbubuSettings > Boot > Boot Scene Name` を設定すると、エディタでどのシーンを再生しても Boot シーンから起動し、初期化後に元のシーンへ戻ります (元のシーンが Build Profiles に登録されている必要があります)。他の拡張が `playModeStartScene` を設定している場合は上書きしません。

### サウンド

| SoundLibrary の項目 | 説明 |
|---|---|
| Key | 再生時に指定する名前 |
| Clips | 複数入れるとランダム再生 (足音のバリエーションなど) |
| Volume / PitchRange | 音量・ピッチのランダム幅 |
| Cooldown | SE の連打防止 (秒) |
| SpatialBlend | 位置指定再生時の 3D 度合い |
| Includes | 他の SoundLibrary を取り込む (共通 SE + ステージ別 BGM など) |

- SE は AudioSource をプールして使い回し、`MaxSeVoices` を超えると最も古い SE を止めて鳴らします。
- 音量は Master / BGM / SE / 環境音 / ボイス / ミュートを `AudioVolumeModel` で管理し、自動保存されます。
- **AudioMixer**: `AbubuSettings > Sound > Mixer` を設定すると、音量をミキサーの Exposed Parameter (`MasterVolume` / `BgmVolume` / `SeVolume` / `AmbientVolume` / `VoiceVolume`、名前は変更可) に dB で反映します。グループ未指定なら Mixer 内の `BGM` / `SE` / `Ambient` / `Voice` グループを自動で使います。
- キー管理が面倒なら `Sound.PlaySe(audioClip)` のように AudioClip を直接渡しても鳴らせます。
- `IBootTask` でクラウドセーブの取得やセーブスロットの選択をしたあとに音量を読み直す場合は、`AudioVolumeModel.Reload()` を呼びます。
- `SePoolPrewarm` を 0 にすると、音を鳴らすまでサウンド用の GameObject は作られません。
- **メモリ** : `SoundSettings.UseAddressableClips` をオンにすると、`SoundEntry.Address` から Addressables で読み込みます (`IAudioClipProvider` で差し替え可)。`Sound.PreloadAsync` / `Sound.Unload` (`ISoundService.PreloadAsync` / `Unload`) で読み込み・解放のタイミングを指定できます。
- 音量は Model (`IAudioVolumeModel`: 値だけ) と Store (`AudioVolumeStore`: 読み込みと保存) に分かれています (画面設定と同じ)。ポーズ中に読み込みが終わった音は、ポーズが解けるまで鳴りません。`Sound.OnVoiceStarted` / `OnVoiceEnded` / `Volume` は null を返しません。
- `Sound.PlayAttached` (動くオブジェクトに追従する SE)・`Sound.StopSe` / `Sound.StopAll` (`ISePlayer.Stop(SeHandle)`) で止められます。
- **スナップショット** : `Sound.Snapshot` (`IAudioMixerSnapshotService.TransitionTo` / `Push`) で AudioMixer のスナップショットを切り替えます。`SoundSettings.PauseSnapshotName` を設定すると、ポーズ中だけそのスナップショットになります (Mixer 未設定なら何もしません)。
- **ボイスと字幕** : `IVoicePlayer.OnVoiceStarted` / `OnVoiceEnded` (`Sound.OnVoiceStarted`) でボイスの開始・終了が分かります。ローカライズが有効なら、SoundLibrary の `Subtitle Key` を翻訳した字幕が `ISubtitleService` に流れるので、`Subtitle Text` コンポーネントを置くだけで表示できます (下の「字幕」)。

### 画面設定

- `GraphicsSettingsModel` は状態だけを持ちます。値は `Set*` で変更し、Unity への反映は `GraphicsSettingsApplier`、保存は `GraphicsSettingsStore` が行います。
- 初回起動時に既定値を押し付けることはありません。ユーザーが変更・保存した値だけを反映します。
- 選択肢と保存キーは `AbubuSettings > Graphics` で設定します。表示する文言は `IGraphicsSettingsLabels` で差し替えられます (既定は英語)。

### エフェクト / プール

- `EffectLibrary` に Key / Prefab / SE キー / 寿命 / 事前生成数 / 最大保持数 (`MaxPooled`) を登録します。`Includes` で他のライブラリを取り込めます (SoundLibrary と同じ)。寿命 0 のときの既定は `EffectSettings.DefaultLifetime` です。
- `IPoolService` は `Configure` (プレハブごとの設定) / `ReturnAll(Scene)` / `Clear` / `GetStats` / `ReturnAfter(…, ignoreTimeScale)` を持ちます。親を指定せずに借りたオブジェクトはアクティブなシーンに置かれます。プールの保管場所に作られたプレハブは、最初に借りられるまで Awake が走りません。
- 寿命 0 ならパーティクルの長さから自動計算して返却します (ループするパーティクルは `Stop()` で返却)。
- プールされたオブジェクトの状態リセットは uPools の `IPoolCallbackReceiver` (`OnRent` / `OnReturn`) に書きます。通知先はインスタンス生成時に集めるので、プレハブに最初から付けておいてください。
- シーン遷移時、貸し出し中のオブジェクトは自動で回収されます。

### セーブ

- 保存先は `AbubuSettings > Save > Storage` (Auto / File / PlayerPrefs)。Auto は WebGL で PlayerPrefs、それ以外は File。File の保存先フォルダの既定は `AbubuSave` です (以前の既定 `Save` のままの設定アセットは、その値が使われます)。
- `Save` は成功したかを bool で返します。
- 新しいバージョンのアプリで保存されたデータは読み込みません (`TryLoad` が false)。そのデータは `キー.newer` に 1 回だけ退避され、`ForceSave` か `Delete` をするまで、そのキーへの `Save` は false を返します (古いアプリで新しいデータを上書きしないため)。
- File 保存は一時ファイル経由で書き込むため、書き込み中に落ちてもデータが壊れません。本体が壊れていた場合は 1 つ前のデータ (`.bak`) から復旧します (復旧後も `.bak` は残ります)。`Delete` は `.broken` / `.newer` / `.tmp` もまとめて消します。
- 復旧できなかったデータ (破損・マイグレーション未登録) は、次の保存で失われないよう `キー.broken` に退避されます。
- データ形式を変えたら `[SaveVersion(2)]` を付け、`SaveMigration<T>` で旧形式からの変換を書きます。

```csharp
[Serializable, SaveVersion(2)]
public sealed class Progress { public int Stage; public int Coins; }

public sealed class ProgressV1ToV2 : SaveMigration<Progress>
{
    public override int FromVersion => 1;
    public override string Migrate(string json) => json.Replace("\"gold\"", "\"Coins\"");
}
// Installer: Container.Bind<ISaveMigration>().To<ProgressV1ToV2>().AsSingle();
```

- 独自の保存先 (クラウド等) は `ISaveStorage` を実装し、AbubuInstaller より前にバインドして差し替えます。

**セーブスロット**

```csharp
Saves.Slots.Switch(1);                         // スロット 1 を使う
Saves.Slots.Save("progress", progress);        // 今のスロットに保存
var data = Saves.Slots.Load("progress", new Progress());
foreach (var slot in Saves.Slots.List()) { /* セーブ画面の一覧 (要約・プレイ時間) */ }
Saves.Slots.SetSummary("ステージ 3");
Saves.Slots.AddPlayTime(Time.unscaledDeltaTime);
Saves.Slots.Copy(1, 2);
Saves.Slots.DeleteSlot(2);
```

- `ISaveSlotService` はスロットごとのデータ、`ISaveService` そのものはスロットに関係ない「共通の領域」です (音量などの設定は共通の領域に保存されます)。スロット数などは `SaveSettings.Slots` で設定します。
- `AddPlayTime` は毎フレーム呼んで構いません (保存は `SaveSlotOptions.PlayTimeSaveInterval` 秒ごと、既定 10 秒)。スロットのファイル名は区切りが曖昧にならない形式になりました (以前の名前のファイルも読めます)。
- UniTask / R3 で使う場合は `Abubu.Save.Reactive` の `SaveUniTaskAsync` / `LoadUniTaskAsync` / `FlushUniTaskAsync` / `ActiveSlotChangedAsObservable` / `SaveFailedAsObservable` が使えます。

**非同期・変換・失敗の通知**

- `SaveAsync` / `LoadAsync` (`IAsyncSaveService`) はファイルの書き込みをバックグラウンドで行います。中断・終了時には自動で `Flush` して書き込みを終わらせます (ライフサイクルが有効なとき)。
- `ISaveTransform` で保存する文字列を変換できます。`SaveSettings.Compress` で GZip 圧縮、`AesSaveTransform` に自分の鍵を渡すと暗号化 (簡単な改ざん防止) になります。あとで圧縮をオフにしても、圧縮済みの古いデータは読めます。暗号化したデータを鍵なしで読んだ場合は、ファイルに触らず `TransformFailed` として通知します。
- 保存・読み込みに失敗すると `ISaveFailureNotifier.SaveFailed` に理由 (`SaveFailureReason`: DiskFull / AccessDenied / Corrupted / NewerVersion など) が流れます。「容量が足りません」のような表示に使えます。

### イベントバス

- `IEventBus` / `GameEvents` は型登録不要。手早く使いたい時向け。`Receive<T>()` は購読専用の Observable を返します。
- MessagePipe 導入時は `BindMessagePipe` と `GlobalMessagePipe` の設定が自動で行われます。メッセージ型は IL2CPP の制約で登録が必要です。
- 自分で `BindMessagePipe` を呼んでいる場合は、Abubu より前の Installer で呼んでください (Abubu はそれを使い回します)。`GlobalMessagePipe` は未設定のときだけ設定し、`AbubuMessagePipe.SetGlobalProvider = false` で止められます。

```csharp
Container.BindAbubuMessage<EnemyDied>();   // Installer
[Inject] IPublisher<EnemyDied> _publisher;  // 使う側
```

### 入力 (Input System 導入時)

- `IInputService` は、よく使う操作を `Move` / `Look` (ReactiveProperty) と `Jump` / `Attack` / `Interact` / `Submit` / `Cancel` (Observable) として公開します。細かい操作が必要な場合は、Input System の型 (`Actions` の InputActionAsset、`GetAction` の InputAction、`Performed` の CallbackContext) もそのまま使えます。
- 既定ではプロジェクト共通の Input Actions (Unity 6 テンプレートの `InputSystem_Actions`) を使います。別のアセットを使うときは `Resources/AbubuInputSettings` (Create > Abubu > Input Settings) を作り、Action のパスを書き換えます。
- ポーズ中に無効にする ActionMap は `Pause Disabled Maps` で指定します (既定では何も無効にしません)。会話中などは `SetGameplayEnabled(false)` で止められます。
- `RebindAsync("Player/Jump")` でキー割り当てを対話的に変更し、結果は `ISaveService` に保存されて次回起動時に復元されます (`ResetBindings()` で初期状態に戻る)。
- `CurrentDevice` で最後に操作したデバイス (キーボード / ゲームパッド / タッチ) が分かるので、ボタン表示の切り替えに使えます。
- 入力モジュールを使わない場合は、Scripting Define Symbols に `ABUBU_DISABLE_INPUT_MODULE` を追加します。
- 設定アセット (`AbubuInputSettings`) は `AbubuSettings > Module Settings` に入れるか、`Resources/AbubuInputSettings` に置きます。
- **以前のバージョンからの移行** : 既定では ActionMap の有効・無効を切り替えなくなりました。ポーズ中に `Player` を無効にしていた場合は、`Pause Disabled Maps` に `Player` を追加してください。
- **デバイスの判別** : `IInputDeviceService` (`GameInput.Devices`) で最後に操作したデバイス (`CurrentDevice`) とゲームパッドの種類 (`CurrentGamepadKind`: Xbox / PlayStation / Switch / Generic) が分かります。スティックのわずかな揺れで表示が切り替わらないよう、しきい値とちらつき防止が入っています。`GameInput.IsUsingGamepad` / `GameInput.CurrentGamepadKind` でも取れます。
- **ボタンのアイコン** : `InputGlyphs` (デバイス・ゲームパッドの種類ごとのアイコン表) を用意し、`InputGlyphImage` を Image に付けると、操作中のデバイスに合わせたボタンのアイコンを表示します。コードからは `GameInput.GetGlyph`。
- **振動** : `IHapticsService` (`GameInput.Haptics` / `GameInput.Rumble` / `GameInput.StopRumble`) で Light / Medium / Heavy などのプリセットを鳴らせます。全体の強さとオン・オフは保存され、ポーズ中・フォーカスが外れている間は止まります。
- **キー割り当て画面** : `Abubu.Input.UI` の `RebindView` / `RebindPresenter` (View は `IRebindView` で差し替え可) で、キー・ボタンの割り当て画面を作れます。重複したときの扱い (Allow / Swap / Reject)、複合入力 (WASD など) に対応しています。ローカライズが有効なら、文言は翻訳テーブルの `abubu.rebind.*` と `abubu.input.action.*` (例: `abubu.input.action.jump`) から取ります (`AbubuBuiltInStrings.csv` に英語・日本語あり)。
- `InputCursorAutoHide` (既定オフ) で、ゲームパッド操作中にマウスカーソルを隠せます。
- **以前のバージョンからの移行 (割り当て)** : `IInputService` に `ResetBinding` / `SetBindingOverride` / `SaveBindings` / `BindingsChanged` が増えました。`RebindAsync` は既定で、同じ種類のデバイスの操作だけを受け付けます (`RebindMatchBindingDevice`)。

### ポーズ

- `IPauseService.Pause(owner)` / `Resume(owner)` は呼び出し元 (owner) を数え、全員が解除したときだけ再開します。ポーズメニューの上に確認ダイアログを重ねても、片方を閉じただけでは再開しません。
- `Time.timeScale` を書き換えるのはこのサービスだけです (`AbubuSettings > Pause > Stop Time Scale` で無効化可)。書き換え方は `ITimeScaleController` で差し替えられます。ポーズ中に他の処理が timeScale を変えていた場合、再開時はその値を優先します。
- 音と入力は `IsPaused` を購読して自動で反映されます。

### ポップアップ

```csharp
if (await _dialogs.ConfirmAsync("削除しますか？")) Delete();   // IDialogService (= Popups.Confirm)
_toasts.Show("保存しました");                                    // IToastService (= Popups.Toast)

var popup = _popups.Push(settingsPopupPrefab);   // 専用 Canvas の下に生成され、背面は操作できなくなる
_popups.Close(popup);                            // 指定したものだけ閉じる
_popups.Pop();                                   // 一番手前を閉じる
_popups.PopAll();
```

- **確認ダイアログ / トースト** は設定なしで使えます (組み込みの見た目をコードで組み立てます)。見た目を変えるには `Tools > Abubu > Create UI Templates` でプレハブを書き出して編集するか、`UiConfirmDialog` / `UiToast` を付けた自分のプレハブを `AbubuUiSettings > Confirm Dialog Prefab` / `Toast Prefab` に指定します。組み込みの見た目は Unity 標準のフォントなので、日本語などはテンプレートのフォントを差し替えてください。
- ダイアログは OK 以外 (Cancel・Cancel 入力・背面クリック・シーン切り替え) で閉じられると false を返します。ボタンの文言は `AbubuUiSettings > Ok Label / Cancel Label`、ローカライズが有効なら翻訳テーブルの `abubu.common.ok` / `abubu.common.cancel` が使われます。トーストはポーズ中も表示され、続けて出したものは順番に表示されます。
- プレハブのコンポーネントに `IPopup` を実装すると、`OnOpened` / `OnClosing` の通知を受け取れます。
- プレハブは DI コンテナ経由で生成されるので、ポップアップのコンポーネントで `[Inject]` が使えます (シーンに SceneContext があればそのコンテナ、無ければ ProjectContext)。
- `AbubuUiSettings` (`AbubuSettings > Module Settings` に入れる) で、ポーズ連動 (`Pause While Open`)・背面の色・背面クリックで閉じる・シーン切り替え時に閉じる (`Close On Scene Change`、既定でオン)・Sorting Order を設定できます。

### 画面遷移

```csharp
_screens.Push(menuScreenPrefab);       // 今の画面は非表示になり、履歴に残る
_screens.Push(settingsScreenPrefab);
_screens.Back();                       // メニューに戻る (最初の画面では何もせず false)
_screens.Replace(otherScreenPrefab);   // 履歴を増やさずに置き換え
_screens.BackToRoot();
```

- 表示されるのは一番手前の 1 画面だけです。ダイアログのように背面を残して重ねたいものはポップアップを使います (ポップアップは画面より手前に出ます)。
- 画面のコンポーネントに `IScreen` を実装すると、`OnShown` (一番手前になった) / `OnHidden` (一番手前でなくなった) を受け取れます。
- `AbubuUiSettings > Keep Previous Screens` がオン (既定) なら前の画面は非表示で残り、戻ったときにスクロール位置などがそのまま残ります。オフなら破棄し、戻るときにプレハブから作り直します。
- Cancel 入力 (Esc / ゲームパッドの B など) で `UiNavigation.Back()` が実行されます (Input System 導入時、`AbubuUiSettings > Back On Cancel` で既定オン)。自前で Cancel を結線している場合はオフにしてください (二重に戻ります)。
  - 文字入力中・Dropdown のリストが開いている間・デバッグメニューをゲームパッドで操作中は戻りません。ほかに止めたい状態 (キー割り当ての入力待ちなど) は `IUiBackBlocker` をバインドします。
  - 同じキーでポーズメニューを開く場合は、`UiNavigation.HandledBackThisFrame` が true なら開かないようにしてください (ポップアップを閉じたのと同じフレームで開き直さないように)。

### ポップアップ・画面の共通設定

- **開閉アニメーション** : ルートのコンポーネントに `IUiTransition` (`PlayShowAsync` / `PlayHideAsync`) を実装します。閉じるときはアニメーションが終わってから破棄 (画面は非表示) され、途中で同じ画面に戻った場合はキャンセルされます。ポーズ中も動かす場合は unscaled time で再生してください。
- **初期選択** (`Select On Open`) : 既定の `Auto` では、ゲームパッドで操作中なら開いたときに最初のボタン (ルートの `UI Initial Selection` で指定したもの) を選び、マウス操作中なら裏の UI の選択を外します (決定キーで裏のボタンが押されないように)。`Always` は常に選び、`Never` は `UI Initial Selection` が付いているときだけ選びます。ポップアップを閉じると、開く前の選択に戻ります。
- **フォーカスの閉じ込め** : ポップアップには `UI Focus Scope` が自動で付き、ゲームパッドの選択がポップアップの外 (裏の画面のボタン) に出ても中に戻します。下に重なったポップアップは操作できなくなります (CanvasGroup.interactable)。自分のモーダルな UI に `UI Focus Scope` を付けても使えます。
- **確認ダイアログ** の初期選択は、連打で削除などを確定しないよう既定で Cancel です (`Confirm Default Button`)。
- **フォーカスの維持** (Input System 導入時、`Keep Focus On Navigate` で既定オン) : マウスで背景をクリックして選択が外れた後や、ポップアップの裏のボタンが選ばれているときに、スティック・十字キーを倒す (またはゲームパッドに持ち替える) と、一番手前のポップアップ (無ければ画面) の中を選び直します。コードからは `UiNavigation.EnsureSelection()`。
- **スクロール** : ScrollRect に `UI Scroll To Selection` を付けると、ゲームパッドで選択した項目が見えるように自動でスクロールします。
- **長押しボタン** : `UI Hold Button` は押し続けると確定するボタンです (マウス・タッチ・ゲームパッドの決定ボタン。ポーズ中も進む)。セーブデータの削除など、うっかり押されたくない操作に使います。
- **操作の案内** : ポップアップ・画面のルートに `IUiPromptProvider` を実装すると、`IUiPromptService.Top` が一番手前の案内 ("Ⓐ 決定" "Ⓑ 戻る" など) を流します。表示は Input 側の UiButtonPromptBar などが行います。
- **EventSystem** : ポップアップ・画面を出すときに EventSystem が無ければ、シーンをまたいで残る EventSystem を用意します (`Ensure Event System`、既定オン。Input System 導入時は InputSystemUIInputModule にプロジェクトの Input Actions を設定)。シーンに EventSystem があればそちらを優先し、用意したものは止めます。
- **Safe Area** : 自動で作る Canvas の中身はノッチ・角丸を避けた範囲に収まります (`Use Safe Area`、既定オン。Backdrop は画面全体)。自分の Canvas では `UI Safe Area` を中身をまとめる子オブジェクトに付けます。
- **フォント** : 組み込みのダイアログ・トーストのフォントは `Default Font` / `Default Tmp Font` で指定します。ローカライズの言語ごとのフォント (`LocalizationFontSettings`) があればそちらが優先されます。色・大きさは `Widget Style` です。
- **生成方法** : インスタンスは `IUiFactory` で作ります。既定の `ContainerUiFactory` は DI コンテナ経由で生成するので `[Inject]` が使えます。Addressables からの読み込みや自前のプールを使う場合は、`IUiFactory` を AbubuInstaller より前にバインドして差し替えます。
- **Backdrop** : `Backdrop Prefab` を指定すると、背面の覆いをプレハブで作ります (未指定なら `Backdrop Color` の Image)。
- **Canvas** : 既定は Screen Space Overlay で、`Reference Resolution` / `Match Width Or Height` / `Sorting Order` を設定できます。VR (World Space) や独自の Canvas Scaler を使う場合は `Popup Canvas Prefab` / `Screen Canvas Prefab` に Canvas のプレハブを指定してください。
- 画面やポップアップを `Destroy(gameObject)` で閉じても、履歴・Backdrop・ポーズはその場で更新されます。ポップアップ自身の閉じるボタンからは `Popups.Close(gameObject)` を呼んでも閉じられます。
- シーン切り替え時に閉じる (`Close On Scene Change`) のは、開いたときのシーンが破棄されたものだけです。加算ロードでアクティブシーンを切り替えただけでは閉じません。
- UI の設定 (`AbubuUiSettings`) は `AbubuSettings > Module Settings` に入れます (`Setup Project` が作って入れます)。無ければ `Resources/AbubuUiSettings`、それも無ければ既定値を使います。

### ローカライズ

- `AbubuSettings > Localization > Table` の `LocalizationTable` に翻訳を登録します。`Languages` に言語コード (`ja`, `en`, `zh-Hans` など) を並べ、`Entries` の `Values` を同じ順に書きます。
- 量が多い場合は CSV / TSV (UTF-8) を `Csv Sources` に入れます。Google スプレッドシートの書き出しをそのまま使えます。`Tools > Abubu > Localization > Export CSV / Export TSV / Import CSV or TSV` (または LocalizationTable の右クリックメニュー) で、テーブルとスプレッドシートを行き来できます。

```csv
key,ja,en
menu.start,スタート,Start
result.score,スコア: {0:N0},Score: {0:N0}
result.rank,{rank} 位,Rank {rank}
item.count.one,{count} 個,{count} item
item.count.other,{count} 個,{count} items
# ここはコメント,,
```

**言語の決まり方**

- 起動時の言語は「前回選んだ言語 → `Startup Language` → 端末の言語 → `Fallback Language`」の順に決まります。言語コードの表記ゆれ (`zh_hant_tw` / `zh-Hant-TW`) と大文字小文字は気にしなくて構いません。
- 翻訳が無いキーは「選択中の言語 → その親 (`zh-Hant-TW` → `zh-Hant` → `zh`) → `Fallback Language` → テーブルの先頭の言語」の順に探します。どこにも無いときに返すもの (`Missing Key`: キー / `[キー]` / 空文字) と警告の有無 (`Warn Missing Keys`) は設定できます。
- 選んだ言語は保存されます (`Remember Language` で無効化可)。言語選択 UI 用に `GetLanguageName("ja")` → "日本語" があります (テーブルに `language.ja` があればそちらを使う)。

**書式**

- `Get(key, args)` は選択中の言語の書式 (`CultureInfo`) で数値・日付を書式化します (`{0:N0}` → 英語 `1,200` / ドイツ語 `1.200`)。
- 名前付きのプレースホルダー: `Get("result.rank", new Dictionary<string, object> { ["rank"] = 3 })` → `{rank}`。値が無いプレースホルダーは例外にせず、そのまま残します。
- 複数形: `GetPlural("item.count", n)` (`Loc.GetPlural`) は `.zero` (0 のときだけ・任意) / `.one` / `.few` / `.many` / `.other` から言語の規則で選び、`{0}` と `{count}` に数を入れます (CLDR の簡略版: 日中韓は常に other、ロシア語・ポーランド語は one / few / many など)。

**フォント・アセット**

- `Create > Abubu > Localization Fonts` で作った `LocalizationFontSettings` に言語ごとのフォント (TextMeshPro / UI.Text) を登録し、`AbubuSettings > Module Settings` に入れると、`Localized Text` が言語に合わせて切り替えます (日中韓の文字が豆腐にならないように。`*` はほかのすべての言語用)。今のフォントのままにしたい Text は `Keep Font` をオンにします。
- 文字入りの画像・ボイスなどは LocalizationTable の `Assets` に言語ごとに登録し、`Localized Image` (Image / SpriteRenderer)・`Localized Audio` (AudioSource) を付けるか、`GetAsset<T>(key)` / `LocalizedAsset<T>` で取り出します。
- Text / TextMeshPro 以外 (UI Toolkit など) は、`Localized Text` の `On Changed` に処理を登録するか、`ILocalizationService.Changed` を購読してください。

**取得元の差し替え・非同期読み込み**

- 翻訳の取得元は `ILocalizationSource` で差し替えられます。サーバーや Addressables から読み込む場合は `IAsyncLocalizationSource` (と `IAsyncLocalizedAssetSource`) を実装します。読み込み中は `IsLoading` が true になり、言語の切り替えは `SetLanguageAsync` で待てます (続けて切り替えた場合は最後の言語だけが反映されます)。起動処理 (Boot) は最初の言語の読み込みを待ってから進みます。
- **Unity Localization** (`com.unity.localization`) を使う場合: パッケージを入れると連携用のアセンブリが自動で有効になります。`Create > Abubu > Unity Localization Source` で設定アセットを作り (使う String Table Collection の名前を指定)、`AbubuSettings > Module Settings` に追加してください。文字列は Smart Format を通さず、Abubu の書式 (`{0}` / `{name}`) で書式化します。

**確認用の機能**

- `Pseudo Localization` (実行中は `PseudoLocalization = true`) をオンにすると、すべての文字列が `[!!Ŝţåŕţ~~!!]` のように伸びた飾り文字になります。翻訳漏れ (ハードコードされた文言) や、長い言語でのレイアウト崩れを翻訳前に見つけられます。
- `Validate Setup` は未翻訳のキー・重複キーに加えて、シーン・プレハブの `Localized Text` で使われているのにどのテーブルにも無いキーを報告します。

**パッケージ自身の文言**

- 画面設定パネルの選択肢 (表示モード・FPS など) とダイアログの OK / Cancel は、翻訳テーブルの `abubu.*` のキーから取ります (無ければ英語)。`Setup Project` で作られるテーブルには、英語・日本語の訳 (`Runtime/Localization/BuiltIn/AbubuBuiltInStrings.csv`) が最初から入っています。View の Labels を自分で編集している場合は `Integration > Localize Package Ui` をオフにしてください。

### 字幕

```csharp
_subtitles.Current.Subscribe(text => label.text = text ?? "").AddTo(this);   // ISubtitleService
_subtitles.Show("story.intro.01", 3f);   // ボイス以外から出す (3 秒で消える)
```

- ボイス (`Sound.PlayVoice`) を再生すると、SoundLibrary の `Subtitle Key` (未設定ならボイスのキー) の翻訳が字幕として流れ、ボイスが終わると消えます。翻訳が無いボイスでは字幕を出しません。
- `Subtitle Text` コンポーネントを Text / TextMeshPro に付けると表示されます (字幕が無いときは `Hide Root` を非表示にする)。言語を切り替えると、表示中の字幕も新しい言語に変わります。

### ライフサイクル / 自動保存

- `IAppLifecycle.OnSuspend` は「アプリが中断される直前」に 1 回流れます。モバイルはバックグラウンドに回ったあと終了処理なしで落とされることがあるので、保存はここで行ってください。
- 中断として扱うきっかけは `AbubuSettings > App Lifecycle > Suspend Triggers` で選べます (Background / FocusLost / Quit / FocusLostOnWebGL。`None` で無効)。既定は Background + Quit + FocusLostOnWebGL です (WebGL はタブを閉じたときに終了処理が呼ばれないため)。
- `IAppSuspendHandler` を実装してバインドしておくと、中断時に呼ばれます。OS が待ってくれる時間は短いので、同期的に終わる処理だけを書いてください。

```csharp
public sealed class ProgressAutoSave : IAppSuspendHandler
{
    [Inject] private ISaveService _save;
    [Inject] private ProgressModel _progress;
    public void OnSuspend() => _save.Save("progress", _progress.ToData());
}
// Installer: Container.Bind<IAppSuspendHandler>().To<ProgressAutoSave>().AsSingle();
```

- `Save Settings On Suspend` (既定オン) で、音量・画面設定の保存待ちを中断時にすぐ書き込みます。
- `Pause On Focus Lost` / `Mute On Focus Lost` (既定オフ) で、ウィンドウのフォーカスが外れている間ポーズ・消音します。

### ログ

```csharp
private static readonly LogChannel Logger = AbubuLog.For("Player");
Logger.Debug($"pos = {transform.position}");
Logger.Warning("HP がマイナスです");
```

- `Debug` / `Info` はエディタと Development Build でだけ出力され、リリースビルドでは呼び出しごと削除されます (引数の文字列も組み立てられません)。リリースでも残したい場合は Scripting Define Symbols に `ABUBU_LOG_VERBOSE` を追加します。
- `AbubuSettings > Log` で最低レベル (`Min Level`) と、出力しないカテゴリ (`Disabled Categories`) を指定できます。実行中は `AbubuLog.SetCategoryEnabled("AI", false)` で切り替えられます。
- `Write To File` をオンにすると、Console に出たすべてのログ (Unity や他のライブラリのものを含む) が `persistentDataPath/Logs/log_日時.txt` に書き出されます (エディタと WebGL を除く)。実機で起きた不具合の調査に使います。パスは `AbubuLog.FilePath` で取得できます。

### デバッグ表示

- `DebugOverlay` をシーンに置く (`GameObject > Abubu > Debug Overlay`) か、`DebugOverlay.EnsureExists()` を呼ぶと、FPS・メモリ・timeScale・解像度が表示されます。
- 画面左上の `Dbg` ボタンかキー F1 で詳細の表示/非表示を切り替えます。
- `DebugOverlay.RegisterCommand` / `RegisterToggle` / `RegisterSlider` でボタン・スイッチ・スライダー (ステージスキップ・無敵など) を追加できます。カテゴリごとにまとめて表示され、登録したオブジェクトが破棄されると自動で消えます。
- ゲームパッドでも操作できます (L3 + R3 長押しで表示、十字キーで選択、A で実行)。使っている間は `DebugOverlay.HasGamepadFocus` が true になるので、ゲーム側の入力を無視するのに使えます。ログの末尾の表示 (既定オフ) もあります。
- `DebugOverlay.Register("score", () => score.ToString())` で任意の項目を追加できます。リリースビルドでは既定で何も表示しません (表示したい場合は Scripting Define Symbols に `ABUBU_DEBUG_OVERLAY_IN_RELEASE` を追加します)。

### セットアップの診断

`Tools > Abubu > Validate Setup` は次を調べて Console に出力します (何も変更しません)。

- `Resources/ProjectContext.prefab` と `AbubuInstaller` の有無
- `AbubuSettings` のライブラリ未設定
- Boot シーン / 最初のシーンが Build Profiles に入っているか
- SoundLibrary / EffectLibrary の空キー・重複キー・Clip / Prefab 未設定・存在しない SE キー
- LocalizationTable の重複キー・未翻訳のキー、`Localized Text` で使われているのにテーブルに無いキー
- 同名のファイルに無く Missing Script になるクラス
- セーブの保存先フォルダ名の衝突の可能性
- Active Input Handling と EventSystem の Input Module の組み合わせ (StandaloneInputModule が Input System のみの設定で使われている)、InputSystemUIInputModule の Actions Asset 未設定、Input Actions に UI/Navigate・UI/Cancel があるか

### Addressables (導入時のみ)

```csharp
var prefab = await _assets.LoadAsync<GameObject>("Enemy/Slime");                  // ロード時のシーンが閉じたら自動解放
await _assets.PreloadAsync(new[] { "Boss", "BossBgm" }, progress: progress);       // 先読み
var icon = await _assets.LoadAsync<Sprite>("UI/Icon", AssetScope.Global);          // 明示的に解放するまで保持
_assets.Release<Sprite>("UI/Icon");
```

- 同じキーの多重ロードは参照カウントで 1 ハンドルにまとめます。
- アドレスの自動付与は SmartAddresser を使えます。

## 機能の選択と単体での利用

`AbubuSettings > Modules` で、使わない機能をオフにできます (オフの機能はサービスもオブジェクトも作られません)。Save / Pause は他の機能が使うため常に有効です。

| 項目 | 内容 |
|---|---|
| Sound / Pool / Effects / Scene / Boot / Graphics / Localization / App Lifecycle / Events | 各機能。Scene をオフにすると Boot も、Pool をオフにすると Effects も無効になります |
| Auto Install Modules | 導入済みの任意モジュール (UI / Input / MessagePipe / Addressables など) を自動でインストールする |
| Excluded Modules | 自動インストールしないモジュールのアセンブリ名 (例: `Abubu.UI`, `Abubu.MessagePipe`) |

AbubuInstaller を使わず、必要な機能だけを自前の Installer から登録することもできます。

```csharp
using Abubu.Installers;

public sealed class MyInstaller : MonoInstaller
{
    [SerializeField] private SaveSettings saveSettings;

    public override void InstallBindings()
    {
        SaveInstaller.Install(Container, saveSettings);
        PauseInstaller.Install(Container, new PauseSettings());
        LocalizationInstaller.Install(Container, new LocalizationSettings { Table = myTable });
    }
}
```

### アセンブリの構成

機能どうしをつなぐコードは、両方が入っているときだけ有効になる小さなアセンブリに分かれています。`Abubu.Installer` は uGUI / TextMeshPro に依存しません。

| アセンブリ | 内容 |
|---|---|
| `Abubu.Localization` | ローカライズ本体 (uGUI / TMP 不要) |
| `Abubu.Localization.UI` | `Localized Text` / `Localized Image` / `Localized Audio` / `Subtitle Text`、言語ごとのフォント |
| `Abubu.UI.Localization` | ダイアログの文言・フォントの翻訳 |
| `Abubu.Graphics.UI.Localization` | 画面設定パネルの選択肢の翻訳 |
| `Abubu.Input.UI.Localization` | キー割り当て画面の文言の翻訳 |
| `Abubu.UI.Input` | Cancel で戻る・フォーカスの維持・EventSystem の用意・長押しボタンの入力 |

## 既存プロジェクトへの導入

- すでに `Resources/ProjectContext.prefab` がある場合、`Setup Project` は既存の Installer を残したまま `AbubuInstaller` を追加するだけです (既存のバインドは上書きしません)。
- `Setup Project` は `AbubuUiSettings` と、Input System 導入時は `AbubuInputSettings` も作って `Module Settings` に入れます (設定しなくてもゲームパッドで UI を操作でき、B で戻れる状態になります)。
- 主なサービス (`ISaveService` / `ISceneService` / `IPauseService` / `ISoundService` / `IPoolService` / `IEffectService` / `IEventBus` / `IAppLifecycle` / `ILocalizationService` / `IPopupService` / `IScreenService`) は、すでにバインドされていれば登録しません。自前の実装がある場合は AbubuInstaller より前の Installer でバインドしてください。

## 無効化の方法

| 機能 | 止め方 |
|---|---|
| 機能そのもの | `Modules` の各項目をオフ |
| 自動保存 (中断時の保存) | `App Lifecycle > Suspend Triggers` を `None`、または `Save Settings On Suspend` をオフ |
| シーン遷移時のポーズ解除 | `Integration > Resume Pause On Scene Change` をオフ |
| 言語の保存 | `Localization > Remember Language` をオフ |
| ログのファイル出力 | `Log > Write To File` をオフ (既定オフ) |
| Cancel で戻る | `AbubuUiSettings > Back On Cancel` をオフ (既定オン) |
| EventSystem の自動用意 | `AbubuUiSettings > Ensure Event System` をオフ |
| 開いたときの選択 | `AbubuUiSettings > Select On Open` を `Never` |
| シーン切り替え時にポップアップ / 画面を閉じる | `AbubuUiSettings > Close On Scene Change` をオフ |
| ポーズ中の timeScale 変更 | `Pause > Stop Time Scale` をオフ |

## 全体に影響する変更

導入すると、次のものがプロジェクト全体に作用します (どれも上の表の設定で止められます)。

- `Time.timeScale` : `IPauseService` でポーズしている間だけ 0 にします。
- 画質・VSync・フレームレート・解像度 : `Graphics` が有効なとき、ユーザーが変更・保存した設定だけを反映します。
- `DontDestroyOnLoad` のオブジェクト : `[Abubu.UI]` (ポップアップの Canvas)、`[Abubu.Screens]` (画面の Canvas)、`[Abubu.App]` (ライフサイクル通知の中継。Hierarchy には非表示)、サウンド用の AudioSource など。どれも最初に必要になったときに作られます。
- `AudioListener.volume` : `Mute On Focus Lost` がオンのときだけ、フォーカスが外れている間 0 にします。
- エディタの `playModeStartScene` : `Boot > Boot Scene Name` を設定したときだけ、Boot シーンから再生します (他の拡張が設定済みなら上書きしません)。
- IL2CPP : ビルド時に `link.xml` を生成し、リフレクションで使う型がコード削除で消えないようにします。

## カスタマイズ

- 既定実装 (`ISceneTransition` / `ISaveStorage` / `ISaveSerializer` / `ILocalizationSource` / `IUiFactory` / `IDialogLabels` / `IGraphicsSettingsLabels`) は、すでにバインドされていれば登録しません。
- `Setup Project` の出力先 (`Setup Folder` / `Project Context Path`) は `Edit > Project Settings > Abubu` で変えられます。差し替えるときは、ProjectContext の Installers で **AbubuInstaller より前** に置いた Installer で先にバインドします。

```csharp
Container.Bind<ISceneTransition>().To<MyTransition>().AsSingle();
```
- ロード画面: `SceneLoadingView` を付けたプレハブを `AbubuSettings > Scene > Loading View Prefab` に設定 (表示は `SceneLoadingScreen` が `ISceneService.ShowLoadingScreen` を購読して行います)
- 自前の ProjectContext Installer から使う: `AbubuInstaller.Install(Container, settings);`
- 自作モジュールを自動登録する: `IAbubuModuleInstaller` を実装し、アセンブリに `[assembly: AbubuModuleInstaller(typeof(MyInstaller))]` を付けます。走査されるのは `Abubu.Core` を参照しているアセンブリ (と名前が `Abubu.` で始まるもの) です。モジュールの設定アセットは `AbubuSettings > Module Settings` に入れると型ごとに登録されるので、`[InjectOptional]` で受け取れます。
- `Setup Project` に処理を足す: `IAbubuSetupStep` (Editor) を実装したクラスは自動で見つかり、Setup Project の最後に実行されます (任意モジュールの設定アセットを作るなど)。

## 対応プラットフォーム

| プラットフォーム | 状況 |
|---|---|
| Windows / macOS / Linux (Standalone) | 開発・動作確認の対象 |
| WebGL | 設計上の対応あり (セーブは自動で PlayerPrefs)。実機確認は未実施 |
| Android / iOS | 設計上の対応あり (画面設定の一部は PC 向け)。実機確認は未実施 |
| コンソール | 未確認 |

「未実施」の環境で動かしたときは Issue で教えてください。確認できたらこの表を更新します。

## サンプル

Package Manager → Abubu Base Package → Samples → **Basic Demo** の Import を押し、`Tools > Abubu > Create Sample Scenes` を実行すると、全機能を試せるシーンが生成されます。

- 生成物はサンプルの `Generated` フォルダに置かれます。サンプル用のサウンド・エフェクトはサンプル専用のライブラリに入り、本番のライブラリからは Includes で参照されるだけです。
- Build Settings へのシーン追加は任意です。サンプルの登録を外すときは `Tools > Abubu > Remove Sample Registration` を実行します。

## 開発者向け

### サンプルの正本

- 正本は `Samples~/BasicDemo` です。`Assets/Samples/` は Import で作られるコピーで、git の管理対象外です。
- サンプルを直すときは `Samples~` 側を編集し、Package Manager から再 Import して確認します。
- 将来 `Samples~` にシーンやプレハブを直接置く場合は、GUID を固定するため `.meta` も一緒にコミットしてください（Unity はチルダ付きフォルダの .meta を自動生成しません）。

### テスト

- EditMode テストは `Tests/Editor`（`Abubu.Tests.Editor`）と `Tests/Input`（`Abubu.Tests.Input`、Input System 導入時のみ）にあります。`Window > General > Test Runner > EditMode` で実行します。
- PlayMode テスト（プール / エフェクト / ポップアップ。`Destroy` や時間経過を伴うため）は `Tests/Runtime`（`Abubu.Tests.Runtime`）にあります。`Window > General > Test Runner > PlayMode` で実行します。
- git URL で導入したプロジェクトでテストを走らせる場合は、そのプロジェクトの `Packages/manifest.json` に `"testables": ["com.abubu.basepackage"]` を追加してください。

### CI

- `.github/workflows/unity-test.yml` が game-ci/unity-test-runner でコンパイルと EditMode / PlayMode テストを実行します（full / minimal の 2 構成）。
- リポジトリの Settings > Secrets に `UNITY_LICENSE`、`UNITY_EMAIL`、`UNITY_PASSWORD` の登録が必要です（取得方法: https://game.ci/docs/github/activation ）。

## ライセンス

MIT（`LICENSE.md`）。依存ライブラリのライセンスは `THIRD-PARTY-NOTICES.md` を参照してください。
