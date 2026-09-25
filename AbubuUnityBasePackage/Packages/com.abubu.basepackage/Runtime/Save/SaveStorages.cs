using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Abubu.Save
{
    public enum SaveStorageKind
    {
        /// <summary>WebGL は PlayerPrefs、それ以外は File</summary>
        Auto,
        File,
        PlayerPrefs,
    }

    [Serializable]
    public sealed class SaveSettings
    {
        public SaveStorageKind Storage = SaveStorageKind.Auto;
        [Tooltip("File 保存時の persistentDataPath 以下のフォルダ名")]
        public string FolderName = "Save";
        [Tooltip("PlayerPrefs 保存時のキー接頭辞 (同一ドメインの他ゲームとの衝突防止。unityroom 等)")]
        public string PlayerPrefsPrefix = "Abubu.Save.";
        public bool PrettyPrint;
    }

    /// <summary>
    /// PlayerPrefs に保存する。WebGL (unityroom など) では IndexedDB に保存されるため最も確実。
    /// </summary>
    public sealed class PlayerPrefsSaveStorage : ISaveStorage
    {
        private readonly string _prefix;

        public PlayerPrefsSaveStorage(string prefix = "Abubu.Save.") => _prefix = prefix;

        public bool TryRead(string key, out string text)
        {
            var k = _prefix + key;
            text = PlayerPrefs.HasKey(k) ? PlayerPrefs.GetString(k) : null;
            return text != null;
        }

        public void Write(string key, string text)
        {
            PlayerPrefs.SetString(_prefix + key, text);
            // WebGL はここで保存しないとタブを閉じた時に失われる
            PlayerPrefs.Save();
        }

        public bool Exists(string key) => PlayerPrefs.HasKey(_prefix + key);

        public void Delete(string key)
        {
            PlayerPrefs.DeleteKey(_prefix + key);
            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// persistentDataPath 以下に 1 キー 1 ファイルで保存する。
    /// 一時ファイルに書いてから置き換えるため、書き込み中に落ちてもデータが壊れない。
    /// </summary>
    public sealed class FileSaveStorage : ISaveStorage
    {
        private readonly string _directory;

        public FileSaveStorage(string folderName = "Save")
        {
            _directory = Path.Combine(Application.persistentDataPath, folderName);
        }

        public string DirectoryPath => _directory;

        public bool TryRead(string key, out string text)
        {
            var path = GetPath(key);
            if (!File.Exists(path))
            {
                // 置き換え途中で落ちた場合のバックアップから復旧
                var backup = path + ".bak";
                if (!File.Exists(backup))
                {
                    text = null;
                    return false;
                }
                path = backup;
            }

            text = File.ReadAllText(path, Encoding.UTF8);
            return true;
        }

        public void Write(string key, string text)
        {
            Directory.CreateDirectory(_directory);
            var path = GetPath(key);
            var temp = path + ".tmp";
            File.WriteAllText(temp, text, Encoding.UTF8);

            if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
            else File.Move(temp, path);
        }

        public bool Exists(string key) => File.Exists(GetPath(key)) || File.Exists(GetPath(key) + ".bak");

        public void Delete(string key)
        {
            var path = GetPath(key);
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
        }

        private string GetPath(string key)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) key = key.Replace(c, '_');
            return Path.Combine(_directory, key + ".json");
        }
    }

    /// <summary>JsonUtility によるシリアライザ。[Serializable] なクラス / 構造体 / プリミティブに対応</summary>
    public sealed class JsonUtilitySaveSerializer : ISaveSerializer
    {
        [Serializable]
        private sealed class Wrapper<T>
        {
            public T value;
        }

        private readonly bool _prettyPrint;

        public JsonUtilitySaveSerializer(bool prettyPrint = false) => _prettyPrint = prettyPrint;

        public string Serialize<T>(T data) => NeedsWrapper<T>()
            ? JsonUtility.ToJson(new Wrapper<T> { value = data }, _prettyPrint)
            : JsonUtility.ToJson(data, _prettyPrint);

        public T Deserialize<T>(string text) => NeedsWrapper<T>()
            ? JsonUtility.FromJson<Wrapper<T>>(text).value
            : JsonUtility.FromJson<T>(text);

        // JsonUtility はトップレベルのプリミティブ・文字列・配列を直接扱えないため包む
        private static bool NeedsWrapper<T>()
        {
            var type = typeof(T);
            return type.IsPrimitive || type.IsEnum || type == typeof(string) || type.IsArray ||
                   (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(System.Collections.Generic.List<>));
        }
    }
}
