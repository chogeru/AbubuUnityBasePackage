# Changelog

このファイルは [Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) 形式で、
バージョン番号は [Semantic Versioning](https://semver.org/lang/ja/) に従います。

導入側は git URL の末尾に `#v0.2.0` のようにタグを付けると、バージョンを固定できます。

## [Unreleased]

### Added
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
- ポップアップのプレハブを DI コンテナ経由で生成するように変更 (ポップアップのコンポーネントで `[Inject]` が使える)
- static ショートカット (`Sound` / `GameInput` など) が取得したサービスをキャッシュし、呼び出しごとにコンテナを引かないように変更
- プールの `IPoolCallbackReceiver` をインスタンス生成時に 1 回だけ集めるように変更 (Rent / Return ごとの GC Alloc を削減)。生成後に追加したコンポーネントには通知されない

### Fixed
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
