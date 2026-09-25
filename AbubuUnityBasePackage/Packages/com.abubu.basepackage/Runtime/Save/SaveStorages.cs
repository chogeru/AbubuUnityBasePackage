using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Abubu.Save
{
    /// <summary>セーブデータの保存先の種類</summary>
    public enum SaveStorageKind
    {
        /// <summary>WebGL は PlayerPrefs、それ以外は File</summary>
        Auto,
        /// <summary>persistentDataPath 以下の JSON ファイル (<see cref="FileSaveStorage"/>)</summary>
        File,
        /// <summary>PlayerPrefs (<see cref="PlayerPrefsSaveStorage"/>)</summary>
        PlayerPrefs,
    }

    /// <summary>
    /// セーブ機能の設定。AbubuSettings の一部として Inspector から編集する。
    /// </summary>
    [Serializable]
    public sealed class SaveSettings
    {
        /// <summary>保存先の種類</summary>
        public SaveStorageKind Storage = SaveStorageKind.Auto;
        [Tooltip("File 保存時の persistentDataPath 以下のフォルダ名")]
        public string FolderName = "Save";
        [Tooltip("PlayerPrefs 保存時のキー接頭辞 (同一ドメインの他ゲームとの衝突防止。unityroom 等)")]
        public string PlayerPrefsPrefix = "Abubu.Save.";

        /// <summary>JSON を改行・インデント付きで出力するか (中身を目で確認したいデバッグ時向け)</summary>
        public bool PrettyPrint;
    }

    /// <summary>
    /// PlayerPrefs に保存する。WebGL (unityroom など) では IndexedDB に保存されるため最も確実。
    /// </summary>
    public sealed class PlayerPrefsSaveStorage : ISaveStorage
    {
        /// <summary>全キーの先頭に付ける接頭辞</summary>
        private readonly string _prefix;

        /// <param name="prefix">キーの接頭辞。同じドメインで動くほかのゲームとキーが衝突しないようにする</param>
        public PlayerPrefsSaveStorage(string prefix = "Abubu.Save.") => _prefix = prefix;

        /// <inheritdoc />
        public bool TryRead(string key, out string text)
        {
            var k = _prefix + key;
            text = PlayerPrefs.HasKey(k) ? PlayerPrefs.GetString(k) : null;
            return text != null;
        }

        /// <inheritdoc />
        public void Write(string key, string text)
        {
            PlayerPrefs.SetString(_prefix + key, text);
            // WebGL はここで保存しないとタブを閉じた時に失われる
            PlayerPrefs.Save();
        }

        /// <inheritdoc />
        public bool Exists(string key) => PlayerPrefs.HasKey(_prefix + key);

        /// <inheritdoc />
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
    /// <remarks>
    /// ファイル構成 (キー "progress" の場合):
    /// progress.json      … 現在のデータ
    /// progress.json.bak  … 1 つ前のデータ (上書き時に File.Replace が作る)
    /// progress.json.tmp  … 書き込み途中の一時ファイル
    /// </remarks>
    public sealed class FileSaveStorage : ISaveStorage
    {
        private readonly string _directory;

        /// <param name="folderName">persistentDataPath 以下に作るフォルダ名</param>
        public FileSaveStorage(string folderName = "Save")
        {
            _directory = Path.Combine(Application.persistentDataPath, folderName);
        }

        /// <summary>保存先フォルダの絶対パス (デバッグ時にフォルダを開く用途など)</summary>
        public string DirectoryPath => _directory;

        /// <inheritdoc />
        /// <remarks>本体ファイルが無い場合は .bak から読み込む。</remarks>
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

        /// <inheritdoc />
        /// <remarks>一時ファイルに書き切ってから本体と入れ替えるので、書き込み中に落ちても本体は壊れない。</remarks>
        public void Write(string key, string text)
        {
            Directory.CreateDirectory(_directory);
            var path = GetPath(key);
            var temp = path + ".tmp";
            File.WriteAllText(temp, text, Encoding.UTF8);

            // 既存ファイルがあれば .bak に退避しつつ入れ替える。初回は単純に移動するだけ
            if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
            else File.Move(temp, path);
        }

        /// <inheritdoc />
        /// <remarks>.bak だけが残っている場合も「存在する」とみなす (TryRead で復旧できるため)。</remarks>
        public bool Exists(string key) => File.Exists(GetPath(key)) || File.Exists(GetPath(key) + ".bak");

        /// <inheritdoc />
        /// <remarks>本体と .bak の両方を削除する。</remarks>
        public void Delete(string key)
        {
            var path = GetPath(key);
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
        }

        /// <summary>
        /// キーから保存ファイルのパスを作る。ファイル名に使えない文字 (/ や : など) は '_' に置き換える。
        /// </summary>
        /// <remarks>置き換えの結果、"a/b" と "a_b" のように別のキーが同じファイルを指すことがある点に注意。</remarks>
        private string GetPath(string key)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) key = key.Replace(c, '_');
            return Path.Combine(_directory, key + ".json");
        }
    }

    /// <summary>JsonUtility によるシリアライザ。[Serializable] なクラス / 構造体 / プリミティブに対応</summary>
    public sealed class JsonUtilitySaveSerializer : ISaveSerializer
    {
        /// <summary>JsonUtility が直接扱えない型を { "value": ... } の形で包むための入れ物</summary>
        [Serializable]
        private sealed class Wrapper<T>
        {
            public T value;
        }

        private readonly bool _prettyPrint;

        /// <param name="prettyPrint">JSON を改行・インデント付きで出力するか</param>
        public JsonUtilitySaveSerializer(bool prettyPrint = false) => _prettyPrint = prettyPrint;

        /// <inheritdoc />
        public string Serialize<T>(T data) => NeedsWrapper<T>()
            ? JsonUtility.ToJson(new Wrapper<T> { value = data }, _prettyPrint)
            : JsonUtility.ToJson(data, _prettyPrint);

        /// <inheritdoc />
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
