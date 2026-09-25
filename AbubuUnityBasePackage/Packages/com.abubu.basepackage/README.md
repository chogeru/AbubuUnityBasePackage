# Abubu Base Package

ゲームジャム / 新規プロジェクトの「最初の 30 分」を省略するための基盤パッケージです。

| 機能 | 概要 |
|---|---|
| Bootstrap | どのシーンから再生しても共通初期化が先に走る。任意で Boot シーン経由の起動 |
| シーン遷移 | フェード / ロード画面 / 遷移先へのデータ受け渡し |
| サウンド | BGM クロスフェード / SE (プール・同時発音数制限・連打防止) / 環境音レイヤー / AudioMixer 対応 |
| プール / エフェクト | プレハブ単位の PoolManager、エフェクト + SE の同時再生と自動返却 |
| セーブ | File / PlayerPrefs 切り替え (WebGL は自動で PlayerPrefs)、バージョン管理とマイグレーション |
| イベントバス | 登録不要の `IEventBus` (R3)、MessagePipe の Zenject 連携 |
| Addressables | 参照カウント付きロード、シーン単位の自動解放、先読み |
| 画面設定 | 解像度 / 表示モード / 画質 / VSync / FPS の Model + 設定 UI |
| MVP | Presenter 基底クラス、シーンに置くだけで動く View |

技術スタック: Zenject (DI) + UniTask + R3 + uPools + LitMotion (+ MessagePipe / Addressables は任意)

## インストール

### 1. パッケージを追加

Package Manager → `+` → **Add package from git URL...**

```
https://github.com/chogeru/AbubuUnityBasePackage.git?path=AbubuUnityBasePackage/Packages/com.abubu.basepackage
```

### 2. 依存パッケージを入れる

追加直後に「依存パッケージが不足しています」ダイアログが出るので **インストール** を押す。
(出なかった場合は `Tools > Abubu > Install Dependencies > Required`)

| 区分 | パッケージ |
|---|---|
| 必須 | UniTask, R3 (+NuGetForUnity), Zenject, uPools, LitMotion |
| 任意 (`All`) | MessagePipe, StateVariable, UnityProcessManager, SerializableInterface, Addressables, SmartAddresser, LucidRandom, TweenPlayables, SceneSystem |

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
```

## ノーコード

| メニュー / コンポーネント | 用途 |
|---|---|
| `GameObject > Abubu > Volume Settings Panel` | 音量設定 UI を生成 (Master / BGM / SE / 環境音 / ミュート) |
| `GameObject > Abubu > Graphics Settings Panel` | 画面設定 UI を生成 (解像度 / 表示モード / 画質 / FPS / VSync) |
| `Play BGM On Start` | シーン開始時に BGM 再生 |
| `Play Ambient On Start` | シーン開始時に環境音レイヤー再生 |
| `Play SE On Click` | Button 押下で SE |
| `Load Scene On Click` | Button 押下でシーン遷移 |

## DI で使う (推奨)

| インターフェース | 内容 |
|---|---|
| `ISoundService` (`.Bgm` / `.Se` / `.Ambient` / `.Volume`) | サウンド |
| `ISceneService` | シーン遷移 (`IsLoading`, `Progress` を購読可能) |
| `IEffectService` / `IPoolService` | エフェクト / オブジェクトプール |
| `ISaveService` | セーブ |
| `IEventBus` / `IPublisher<T>` `ISubscriber<T>` (MessagePipe) | イベント |
| `IAssetService` | Addressables (導入時のみ) |
| `GraphicsSettingsModel` / `AudioVolumeModel` | 設定値 (ReactiveProperty) |
| `IBootService` | 起動処理の完了待ち |

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
- 音量は Master / BGM / SE / 環境音 / ミュートを `AudioVolumeModel` で管理し、自動保存されます。
- **AudioMixer**: `AbubuSettings > Sound > Mixer` を設定すると、音量をミキサーの Exposed Parameter (`MasterVolume` / `BgmVolume` / `SeVolume` / `AmbientVolume`、名前は変更可) に dB で反映します。グループ未指定なら Mixer 内の `BGM` / `SE` / `Ambient` グループを自動で使います。
- キー管理が面倒なら `Sound.PlaySe(audioClip)` のように AudioClip を直接渡しても鳴らせます。

### エフェクト / プール

- `EffectLibrary` に Key / Prefab / SE キー / 寿命 / 事前生成数を登録します。
- 寿命 0 ならパーティクルの長さから自動計算して返却します (ループするパーティクルは `Stop()` で返却)。
- プールされたオブジェクトの状態リセットは uPools の `IPoolCallbackReceiver` (`OnRent` / `OnReturn`) に書きます。
- シーン遷移時、貸し出し中のオブジェクトは自動で回収されます。

### セーブ

- 保存先は `AbubuSettings > Save > Storage` (Auto / File / PlayerPrefs)。Auto は WebGL で PlayerPrefs、それ以外は File。
- File 保存は一時ファイル経由で書き込むため、書き込み中に落ちてもデータが壊れません。
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

## サンプル

Package Manager → Abubu Base Package → Samples → **Basic Demo** の Import を押し、`Tools > Abubu > Create Sample Scenes` を実行すると、全機能を試せるシーンが生成されます。

## 開発者向け

### サンプルの正本

- 正本は `Samples~/BasicDemo` です。`Assets/Samples/` は Import で作られるコピーで、git の管理対象外です。
- サンプルを直すときは `Samples~` 側を編集し、Package Manager から再 Import して確認します。
- 将来 `Samples~` にシーンやプレハブを直接置く場合は、GUID を固定するため `.meta` も一緒にコミットしてください（Unity はチルダ付きフォルダの .meta を自動生成しません）。

### テスト

- EditMode テストは `Tests/Editor`（`Abubu.Tests.Editor`）にあります。`Window > General > Test Runner > EditMode` で実行します。
- git URL で導入したプロジェクトでテストを走らせる場合は、そのプロジェクトの `Packages/manifest.json` に `"testables": ["com.abubu.basepackage"]` を追加してください。

### CI

- `.github/workflows/unity-test.yml` が game-ci/unity-test-runner でコンパイルと EditMode テストを実行します。
- リポジトリの Settings > Secrets に `UNITY_LICENSE`、`UNITY_EMAIL`、`UNITY_PASSWORD` の登録が必要です（取得方法: https://game.ci/docs/github/activation ）。
