using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Abubu.Save
{
    /// <summary>
    /// <see cref="ISaveService"/> の標準実装。
    /// </summary>
    /// <remarks>
    /// 保存形式: ユーザーデータを <see cref="ISaveSerializer"/> で文字列化し、
    /// それをバージョンや型名と一緒に Envelope (JSON) に包んで <see cref="ISaveStorage"/> に書き込む。
    /// 読み込み時に保存時のバージョンが古ければ、マイグレーションを順に適用してから復元する。
    /// </remarks>
    public sealed class SaveService : ISaveService
    {
        /// <summary>保存時にデータを包む封筒。バージョンと型名を持ち、ロード時のマイグレーション判定に使う</summary>
        [Serializable]
        private sealed class Envelope
        {
            /// <summary>保存時のデータ形式バージョン (<see cref="SaveVersionAttribute"/>)</summary>
            public int version;
            /// <summary>保存したデータ型の完全名。読み込み時の型違いの警告に使う</summary>
            public string type;
            /// <summary>保存日時 (UTC, ISO 8601)。デバッグ用</summary>
            public string savedAt;
            /// <summary>ISaveSerializer で文字列化したユーザーデータ本体</summary>
            public string data;
        }

        private readonly ISaveStorage _storage;
        private readonly ISaveSerializer _serializer;

        /// <summary>(データ型, 変換元バージョン) からマイグレーションを引く表</summary>
        private readonly Dictionary<(Type, int), ISaveMigration> _migrations;

        /// <param name="storage">保存先</param>
        /// <param name="serializer">データ ⇔ 文字列の変換方法</param>
        /// <param name="migrations">
        /// DI コンテナに登録されたマイグレーション一覧 (無ければ null)。
        /// 同じ (型, バージョン) が重複して登録されていると ToDictionary が例外を投げる。
        /// </param>
        public SaveService(ISaveStorage storage, ISaveSerializer serializer, List<ISaveMigration> migrations = null)
        {
            _storage = storage;
            _serializer = serializer;
            _migrations = (migrations ?? Enumerable.Empty<ISaveMigration>())
                .ToDictionary(m => (m.DataType, m.FromVersion));
        }

        /// <inheritdoc />
        public void Save<T>(string key, T data)
        {
            var envelope = new Envelope
            {
                version = GetVersion<T>(),
                type = typeof(T).FullName,
                savedAt = DateTime.UtcNow.ToString("o"),
                data = _serializer.Serialize(data),
            };
            try
            {
                _storage.Write(key, JsonUtility.ToJson(envelope));
            }
            catch (Exception e)
            {
                // ディスクフル・権限エラーなどでゲームを止めない (自動保存の購読内から呼ばれることもある)
                Debug.LogError($"[Abubu.Save] \"{key}\" の保存に失敗しました: {e}");
            }
        }

        /// <inheritdoc />
        public bool TryLoad<T>(string key, out T data)
        {
            data = default;
            if (!_storage.TryRead(key, out var text) || string.IsNullOrEmpty(text)) return false;

            try
            {
                var envelope = JsonUtility.FromJson<Envelope>(text);
                if (!string.IsNullOrEmpty(envelope.type) && envelope.type != typeof(T).FullName)
                {
                    Debug.LogWarning($"[Abubu.Save] \"{key}\" は {envelope.type} として保存されていますが {typeof(T).FullName} で読み込もうとしています。");
                }
                var serialized = envelope.data;
                var current = GetVersion<T>();

                // 保存時のバージョンから現在のバージョンまで 1 段ずつ変換する。
                // version が 0 (古い形式で Envelope に version が無かった場合など) は v1 として扱う
                for (var v = Math.Max(1, envelope.version); v < current; v++)
                {
                    if (!_migrations.TryGetValue((typeof(T), v), out var migration))
                    {
                        Debug.LogError($"[Abubu.Save] {typeof(T).Name} の v{v} → v{v + 1} のマイグレーションが登録されていません (key: {key})");
                        return false;
                    }
                    serialized = migration.Migrate(serialized);
                }

                data = _serializer.Deserialize<T>(serialized);
                // 変換したら新しい形式で保存し直す
                if (envelope.version < current) Save(key, data);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Abubu.Save] \"{key}\" の読み込みに失敗しました: {e}");
                return false;
            }
        }

        /// <inheritdoc />
        public T Load<T>(string key, T defaultValue = default) => TryLoad<T>(key, out var data) ? data : defaultValue;

        /// <inheritdoc />
        public bool Exists(string key) => _storage.Exists(key);

        /// <inheritdoc />
        public void Delete(string key) => _storage.Delete(key);

        /// <summary>型に付いた <see cref="SaveVersionAttribute"/> のバージョン。未指定なら 1。</summary>
        private static int GetVersion<T>() => typeof(T).GetCustomAttribute<SaveVersionAttribute>()?.Version ?? 1;

        /// <summary>
        /// 設定に応じた保存先を作る。
        /// Auto の場合、WebGL ではファイルに書き込めないため PlayerPrefs を、それ以外ではファイルを使う。
        /// </summary>
        public static ISaveStorage CreateStorage(SaveSettings settings)
        {
            var kind = settings.Storage;
            if (kind == SaveStorageKind.Auto)
            {
                kind = Application.platform == RuntimePlatform.WebGLPlayer ? SaveStorageKind.PlayerPrefs : SaveStorageKind.File;
            }

            return kind == SaveStorageKind.PlayerPrefs
                ? new PlayerPrefsSaveStorage(settings.PlayerPrefsPrefix)
                : new FileSaveStorage(settings.FolderName);
        }
    }
}
