# Changelog

このファイルは [Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) 形式で、
バージョン番号は [Semantic Versioning](https://semver.org/lang/ja/) に従います。

導入側は git URL の末尾に `#v0.2.0` のようにタグを付けると、バージョンを固定できます。

## [Unreleased]

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
