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
| ポップアップ | ダイアログのスタック管理、背面ブロック、ポーズ連動 |
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
- MessagePipe / Addressables は入っていれば自動で有効になります。
- R3 本体は NuGet 配布のため `Assets/packages.config` に自動で追記され、NuGetForUnity が復元します。

### 3. セットアップ

`Tools > Abubu > Setup Project` を実行すると以下が生成されます。

- `Assets/Abubu/AbubuSettings.asset` … 全機能の設定
- `Assets/Abubu/SoundLibrary.asset` … サウンド定義 (キー → AudioClip)
- `Assets/Abubu/EffectLibrary.asset` … エフェクト定義 (キー → Prefab + SE)
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

// 入力 (Input System 導入時)
var move = GameInput.Move;
GameInput.Jump.Subscribe(_ => Jump()).AddTo(this);

// ポップアップ
Popups.Push(confirmDialogPrefab);
Popups.Pop();
```

### キーの定数化

`"jump"` のような文字列は打ち間違いが実行時まで分かりません。`Tools > Abubu > Generate Keys` を実行すると、SoundLibrary / EffectLibrary / Build Profiles のシーン一覧から定数クラスが `Assets/Abubu/Generated/AbubuKeys.cs` に生成されます (ライブラリやシーン一覧を編集したら再実行してください)。

```csharp
Sound.PlaySe(SoundKeys.Se.Jump);
Sound.PlayBgm(SoundKeys.Bgm.Stage1);
Fx.Play(EffectKeys.Explosion, enemy.position);
Scenes.Load(SceneKeys.Game);
```

存在しないキーを指定したときの警告には「もしかして: "jump"」のように近いキーが表示されます。

### static ショートカットの注意

`Sound` / `Scenes` / `Fx` / `GameEvents` / `GameInput` / `Popups` は `ProjectContext` を介したグローバルなアクセスです。

- `ProjectContext` が無い・アプリ終了中は、何もせずに戻ります (未セットアップのときだけ警告を 1 回出します)。
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

```csharp
public sealed class Player : MonoBehaviour
{
    [Inject] private ISoundService _sound;
    [Inject] private ISceneService _scene;

    private async UniTaskVoid GameOver()
    {
        _sound.Se.Play("dead");
        await _sound.Bgm.StopAsync(fadeDuration: 2f);
        await _scene.LoadWithPayloadAsync("Result", new ResultData(score));
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

## 機能詳細

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

- `AbubuSettings > Boot > Boot Scene Name` を設定すると、エディタでどのシーンを再生しても Boot シーンから起動し、初期化後に元のシーンへ戻ります (元のシーンが Build Profiles に登録されている必要があります)。

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

### エフェクト / プール

- `EffectLibrary` に Key / Prefab / SE キー / 寿命 / 事前生成数を登録します。
- 寿命 0 ならパーティクルの長さから自動計算して返却します (ループするパーティクルは `Stop()` で返却)。
- プールされたオブジェクトの状態リセットは uPools の `IPoolCallbackReceiver` (`OnRent` / `OnReturn`) に書きます。通知先はインスタンス生成時に集めるので、プレハブに最初から付けておいてください。
- シーン遷移時、貸し出し中のオブジェクトは自動で回収されます。

### セーブ

- 保存先は `AbubuSettings > Save > Storage` (Auto / File / PlayerPrefs)。Auto は WebGL で PlayerPrefs、それ以外は File。
- File 保存は一時ファイル経由で書き込むため、書き込み中に落ちてもデータが壊れません。本体が壊れていた場合は 1 つ前のデータ (`.bak`) から復旧します。
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

### イベントバス

- `IEventBus` / `GameEvents` は型登録不要。手早く使いたい時向け。
- MessagePipe 導入時は `BindMessagePipe` と `GlobalMessagePipe` の設定が自動で行われます。メッセージ型は IL2CPP の制約で登録が必要です。

```csharp
Container.BindAbubuMessage<EnemyDied>();   // Installer
[Inject] IPublisher<EnemyDied> _publisher;  // 使う側
```

### 入力 (Input System 導入時)

- `IInputService` は Input System の型を外に出さず、`Move` / `Look` (ReactiveProperty) と `Jump` / `Attack` / `Interact` / `Submit` / `Cancel` (Observable) を公開します。
- 既定ではプロジェクト共通の Input Actions (Unity 6 テンプレートの `InputSystem_Actions`) を使います。別のアセットを使うときは `Resources/AbubuInputSettings` (Create > Abubu > Input Settings) を作り、Action のパスを書き換えます。
- ポーズ中はゲーム操作用の ActionMap (既定 `Player`) が無効になり、UI の操作だけ受け付けます。会話中などは `SetGameplayEnabled(false)` で止められます。
- `RebindAsync("Player/Jump")` でキー割り当てを対話的に変更し、結果は `ISaveService` に保存されて次回起動時に復元されます (`ResetBindings()` で初期状態に戻る)。
- `CurrentDevice` で最後に操作したデバイス (キーボード / ゲームパッド / タッチ) が分かるので、ボタン表示の切り替えに使えます。

### ポーズ

- `IPauseService.Pause(owner)` / `Resume(owner)` は呼び出し元 (owner) を数え、全員が解除したときだけ再開します。ポーズメニューの上に確認ダイアログを重ねても、片方を閉じただけでは再開しません。
- `Time.timeScale` を書き換えるのはこのサービスだけです (`AbubuSettings > Pause > Stop Time Scale` で無効化可)。
- 音と入力は `IsPaused` を購読して自動で反映されます。

### ポップアップ

```csharp
var dialog = _popups.Push(confirmDialogPrefab);   // 専用 Canvas の下に生成され、背面は操作できなくなる
_popups.Pop();                                    // 一番手前を閉じる
_popups.PopAll();
```

- プレハブのコンポーネントに `IPopup` を実装すると、`OnOpened` / `OnClosing` の通知を受け取れます。
- プレハブは DI コンテナ経由で生成されるので、ポップアップのコンポーネントで `[Inject]` が使えます (シーンに SceneContext があればそのコンテナ、無ければ ProjectContext)。
- `Resources/AbubuUiSettings` (Create > Abubu > UI Settings) で、ポーズ連動 (`Pause While Open`)・背面の色・背面クリックで閉じる・シーン切り替え時に閉じる (`Close On Scene Change`、既定でオン)・Sorting Order を設定できます。
- Cancel キーで閉じたい場合は `GameInput.Cancel.Subscribe(_ => Popups.Pop())` のように結線します。

### デバッグ表示

- `DebugOverlay` をシーンに置く (`GameObject > Abubu > Debug Overlay`) か、`DebugOverlay.EnsureExists()` を呼ぶと、FPS・メモリ・timeScale・解像度が表示されます。
- 画面左上の `Dbg` ボタンかキー F1 で詳細の表示/非表示を切り替えます。
- `DebugOverlay.Register("score", () => score.ToString())` で任意の項目を追加できます。リリースビルドでは既定で何も表示しません。

### セットアップの診断

`Tools > Abubu > Validate Setup` は次を調べて Console に出力します (何も変更しません)。

- `Resources/ProjectContext.prefab` と `AbubuInstaller` の有無
- `AbubuSettings` のライブラリ未設定
- Boot シーン / 最初のシーンが Build Profiles に入っているか
- SoundLibrary / EffectLibrary の空キー・重複キー・Clip / Prefab 未設定・存在しない SE キー

### Addressables (導入時のみ)

```csharp
var prefab = await _assets.LoadAsync<GameObject>("Enemy/Slime");                  // ロード時のシーンが閉じたら自動解放
await _assets.PreloadAsync(new[] { "Boss", "BossBgm" }, progress: progress);       // 先読み
var icon = await _assets.LoadAsync<Sprite>("UI/Icon", AssetScope.Global);          // 明示的に解放するまで保持
_assets.Release<Sprite>("UI/Icon");
```

- 同じキーの多重ロードは参照カウントで 1 ハンドルにまとめます。
- アドレスの自動付与は SmartAddresser を使えます。

## カスタマイズ

- 既定実装 (`ISceneTransition` / `ISaveStorage` / `ISaveSerializer`) は `IfNotBound` で登録されています。差し替えるときは、ProjectContext の Installers で **AbubuInstaller より前** に置いた Installer で先にバインドします。

```csharp
Container.Bind<ISceneTransition>().To<MyTransition>().AsSingle();
```
- ロード画面: `SceneLoadingView` を付けたプレハブを `AbubuSettings > Scene > Loading View Prefab` に設定 (表示は `SceneLoadingScreen` が `ISceneService.ShowLoadingScreen` を購読して行います)
- 自前の ProjectContext Installer から使う: `AbubuInstaller.Install(Container, settings);`
- 自作モジュールを自動登録する: `IAbubuModuleInstaller` を実装し、アセンブリに `[assembly: AbubuModuleInstaller(typeof(MyInstaller))]` を付けます。走査されるのは名前が `Abubu.` で始まるアセンブリだけなので、asmdef の名前を `Abubu.MyModule` のようにしてください。

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
