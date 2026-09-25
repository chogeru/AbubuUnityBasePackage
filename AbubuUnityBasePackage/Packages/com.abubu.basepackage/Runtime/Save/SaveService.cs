using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Abubu.Save
{
    public sealed class SaveService : ISaveService
    {
        /// <summary>保存時にデータを包む封筒。バージョンと型名を持ち、ロード時のマイグレーション判定に使う</summary>
        [Serializable]
        private sealed class Envelope
        {
            public int version;
            public string type;
            public string savedAt;
            public string data;
        }

        private readonly ISaveStorage _storage;
        private readonly ISaveSerializer _serializer;
        private readonly Dictionary<(Type, int), ISaveMigration> _migrations;

        public SaveService(ISaveStorage storage, ISaveSerializer serializer, List<ISaveMigration> migrations = null)
        {
            _storage = storage;
            _serializer = serializer;
            _migrations = (migrations ?? Enumerable.Empty<ISaveMigration>())
                .ToDictionary(m => (m.DataType, m.FromVersion));
        }

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

        public T Load<T>(string key, T defaultValue = default) => TryLoad<T>(key, out var data) ? data : defaultValue;

        public bool Exists(string key) => _storage.Exists(key);

        public void Delete(string key) => _storage.Delete(key);

        private static int GetVersion<T>() => typeof(T).GetCustomAttribute<SaveVersionAttribute>()?.Version ?? 1;

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
