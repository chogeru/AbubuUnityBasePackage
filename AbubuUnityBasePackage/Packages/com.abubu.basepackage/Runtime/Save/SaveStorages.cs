using System;
using System.Collections.Concurrent;
using System.Globalization;
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
    /// <code>
    /// SaveInstaller.Install(Container, new SaveSettings { Storage = SaveStorageKind.File, Compress = true });
    /// </code>
    /// </summary>
    [Serializable]
    public sealed class SaveSettings
    {
        /// <summary>保存先の種類</summary>
        public SaveStorageKind Storage = SaveStorageKind.Auto;
        /// <summary>
        /// FolderName の既定値。他のゲームやアセットが使いがちな "Save" と衝突しないよう、Abubu 専用の名前にしている
        /// (以前の既定値 "Save" で作られた設定アセットは、シリアライズ済みの値がそのまま使われる)
        /// </summary>
        public const string DefaultFolderName = "AbubuSave";

        [Tooltip("File 保存時の persistentDataPath 以下のフォルダ名")]
        public string FolderName = DefaultFolderName;
        [Tooltip("PlayerPrefs 保存時のキー接頭辞 (同一ドメインの他ゲームとの衝突防止。unityroom 等)")]
        public string PlayerPrefsPrefix = "Abubu.Save.";

        /// <summary>JSON を改行・インデント付きで出力するか (中身を目で確認したいデバッグ時向け)</summary>
        public bool PrettyPrint;

        /// <summary>
        /// セーブデータを GZip で圧縮するか (<see cref="GZipSaveTransform"/>)。既定は false。
        /// 途中で有効/無効を切り替えても、以前のデータはそのまま読める (GZip の展開は無効でも常に行うため)
        /// </summary>
        [Tooltip("セーブデータを GZip で圧縮する。途中で切り替えても以前のデータは読める")]
        public bool Compress;

        /// <summary>セーブスロット (<see cref="ISaveSlotService"/>) の設定</summary>
        public SaveSlotOptions Slots = new();
    }

    /// <summary>
    /// PlayerPrefs に保存する。WebGL (unityroom など) では IndexedDB に保存されるため最も確実。
    /// <code>
    /// Container.Bind&lt;ISaveStorage&gt;().FromInstance(new PlayerPrefsSaveStorage("MyGame.")).AsSingle();
    /// </code>
    /// </summary>
    /// <remarks>Unity の API を使うためメインスレッド専用 (非同期版の保存も同期で行われる)。</remarks>
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
    /// <code>
    /// var storage = new FileSaveStorage("MySave"); // persistentDataPath/MySave/*.json
    /// </code>
    /// </summary>
    /// <remarks>
    /// ファイル構成 (キー "progress" の場合):
    /// progress.json      … 現在のデータ
    /// progress.json.bak  … 1 つ前のデータ (上書き時に File.Replace が作る)
    /// progress.json.tmp  … 書き込み途中の一時ファイル
    /// <para>
    /// キーはほぼそのままファイル名になる。ファイル名に使えない文字 (どの OS でも同じになるよう Windows の基準で
    /// &lt; &gt; : " / \ | ? * と制御文字) と '%' は "%XX" (文字コードの 16 進) に置き換える ("slot/1/progress" → "slot%2F1%2Fprogress.json")。
    /// 置き換えは 1 対 1 なので、別のキーが同じファイルを指すことはない。
    /// </para>
    /// <para>
    /// 互換: 以前のバージョンは使えない文字を '_' に置き換えていた ("slot/1/progress" → "slot_1_progress.json")。
    /// 新しい名前のファイルが無く以前の名前のファイルがある場合は、以前の名前のファイルを読む (書き込みは常に新しい名前)。
    /// 以前の形式では "slot/1/x" と "slot_1_x" が同じファイルを指していたため、そのデータはどちらのキーからも読めてしまう点は以前と同じ
    /// (新しく書き込んだ時点で分かれる)。Delete は両方の名前のファイルを消す。
    /// </para>
    /// <para>
    /// Windows / macOS の標準のファイルシステムは大文字・小文字を区別しないため、
    /// "Slot1" と "slot1" は同じファイルを指す (Linux / Android では別ファイル)。プラットフォーム間で挙動を揃えるため、
    /// キーは小文字だけで書くことを推奨する (既存データの互換のため、ここでは自動で変換しない)。
    /// また "CON" "NUL" などの Windows の予約名はキーに使わないこと。
    /// </para>
    /// <para>
    /// どのスレッドから呼んでもよい (<see cref="IThreadSafeSaveStorage"/>)。同じファイルへの操作はファイルごとのロックで 1 つずつ行う。
    /// </para>
    /// </remarks>
    public sealed class FileSaveStorage : IThreadSafeSaveStorage, ISaveBackupStorage
    {
        private readonly string _directory;

        /// <summary>ファイルパスごとのロック (別スレッドからの同時書き込みで .tmp / .bak を壊さないため)</summary>
        private readonly ConcurrentDictionary<string, object> _locks = new(StringComparer.OrdinalIgnoreCase);

        /// <param name="folderName">persistentDataPath 以下に作るフォルダ名</param>
        public FileSaveStorage(string folderName = SaveSettings.DefaultFolderName)
        {
            _directory = Path.Combine(Application.persistentDataPath, folderName);
        }

        /// <summary>保存先フォルダの絶対パス (デバッグ時にフォルダを開く用途など)</summary>
        public string DirectoryPath => _directory;

        /// <inheritdoc />
        /// <remarks>本体ファイルが無い場合は .bak から読み込む。</remarks>
        public bool TryRead(string key, out string text)
        {
            var paths = GetPaths(key);
            lock (paths.FirstLock)
            lock (paths.SecondLock)
            {
                var path = ResolveExisting(paths);
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
        }

        /// <inheritdoc />
        public bool TryReadBackup(string key, out string text)
        {
            var paths = GetPaths(key);
            lock (paths.FirstLock)
            lock (paths.SecondLock)
            {
                var backup = ResolveExisting(paths) + ".bak";
                text = File.Exists(backup) ? File.ReadAllText(backup, Encoding.UTF8) : null;
                return text != null;
            }
        }

        /// <inheritdoc />
        /// <remarks>.bak は残したまま、.bak の内容で本体を置き換える。途中で落ちても .bak が残るので TryRead で読める。</remarks>
        public void RestoreFromBackup(string key)
        {
            var paths = GetPaths(key);
            lock (paths.FirstLock)
            lock (paths.SecondLock)
            {
                var path = ResolveExisting(paths);
                var backup = path + ".bak";
                if (!File.Exists(backup)) throw new FileNotFoundException("バックアップがありません", backup);

                var temp = path + ".tmp";
                File.Copy(backup, temp, true);
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
            }
        }

        /// <inheritdoc />
        /// <remarks>
        /// 一時ファイルに書き切ってから本体と入れ替えるので、書き込み中に落ちても本体は壊れない。
        /// 常に新しい形式のファイル名に書く (以前の形式のファイルには触らない)。
        /// </remarks>
        public void Write(string key, string text)
        {
            Directory.CreateDirectory(_directory);
            var path = GetPath(key);
            lock (LockFor(path))
            {
                var temp = path + ".tmp";
                if (File.Exists(temp)) File.Delete(temp); // 前回落ちたときの残骸
                File.WriteAllText(temp, text, Encoding.UTF8);

                // 既存ファイルがあれば .bak に退避しつつ入れ替える。初回は単純に移動するだけ
                if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
                else File.Move(temp, path);
            }
        }

        /// <inheritdoc />
        /// <remarks>.bak だけが残っている場合も「存在する」とみなす (TryRead で復旧できるため)。以前の形式のファイル名も見る。</remarks>
        public bool Exists(string key)
        {
            var paths = GetPaths(key);
            lock (paths.FirstLock)
            lock (paths.SecondLock)
            {
                return HasAny(paths.Path) || (paths.LegacyPath != null && HasAny(paths.LegacyPath));
            }
        }

        /// <inheritdoc />
        /// <remarks>本体・.bak・.tmp をすべて削除する。以前の形式のファイル名のものも削除する。</remarks>
        public void Delete(string key)
        {
            var paths = GetPaths(key);
            lock (paths.FirstLock)
            lock (paths.SecondLock)
            {
                DeleteFiles(paths.Path);
                if (paths.LegacyPath != null) DeleteFiles(paths.LegacyPath);
            }
        }

        private static void DeleteFiles(string path)
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
            if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
        }

        /// <summary>本体か .bak があるか</summary>
        private static bool HasAny(string path) => File.Exists(path) || File.Exists(path + ".bak");

        /// <summary>読む側のファイル: 新しい形式のファイルが無く、以前の形式のファイルだけがあればそちら</summary>
        private static string ResolveExisting(in KeyPaths paths) =>
            paths.LegacyPath != null && !HasAny(paths.Path) && HasAny(paths.LegacyPath) ? paths.LegacyPath : paths.Path;

        private object LockFor(string path) => _locks.GetOrAdd(path, _ => new object());

        /// <summary>キーに対応する新旧のファイルパスと、それぞれのロック (デッドロックしないよう常に同じ順で取る)</summary>
        private readonly struct KeyPaths
        {
            public readonly string Path;
            /// <summary>以前の形式のファイルパス。新しい形式と同じなら null</summary>
            public readonly string LegacyPath;
            public readonly object FirstLock;
            public readonly object SecondLock;

            public KeyPaths(string path, string legacyPath, object firstLock, object secondLock)
            {
                Path = path;
                LegacyPath = legacyPath;
                FirstLock = firstLock;
                SecondLock = secondLock;
            }
        }

        private KeyPaths GetPaths(string key)
        {
            var path = GetPath(key);
            var legacy = GetLegacyPath(key);
            if (legacy == null || string.Equals(legacy, path, StringComparison.OrdinalIgnoreCase))
            {
                var single = LockFor(path);
                return new KeyPaths(path, null, single, single);
            }
            // 以前の形式のファイルは別のキーの新しい形式のファイルと同じことがあるため、両方をロックする
            var pathFirst = string.Compare(path, legacy, StringComparison.OrdinalIgnoreCase) < 0;
            var a = LockFor(pathFirst ? path : legacy);
            var b = LockFor(pathFirst ? legacy : path);
            return new KeyPaths(path, legacy, a, b);
        }

        /// <summary>キーから保存ファイルのパスを作る (使えない文字と '%' を "%XX" にする。1 対 1 の置き換え)</summary>
        private string GetPath(string key) => Path.Combine(_directory, EncodeFileName(key) + ".json");

        /// <summary>以前のバージョンのファイルパス (使えない文字を '_' にしていた)。新しい形式と同じなら null</summary>
        private string GetLegacyPath(string key)
        {
            var legacy = key;
            foreach (var c in Path.GetInvalidFileNameChars()) legacy = legacy.Replace(c, '_');
            var encoded = EncodeFileName(key);
            return legacy == encoded ? null : Path.Combine(_directory, legacy + ".json");
        }

        /// <summary>
        /// キーをファイル名にする。どの OS でも同じファイル名になるよう、Windows で使えない文字 (制御文字と &lt; &gt; : " / \ | ? *) と、
        /// 置き換えの目印の '%' を "%XX" (文字コードの 16 進、大文字) にする
        /// </summary>
        internal static string EncodeFileName(string key)
        {
            StringBuilder builder = null;
            for (var i = 0; i < key.Length; i++)
            {
                var c = key[i];
                if (NeedsEscape(c))
                {
                    builder ??= new StringBuilder(key.Length + 8).Append(key, 0, i);
                    builder.Append('%').Append(((int)c).ToString("X2", CultureInfo.InvariantCulture));
                }
                else
                {
                    builder?.Append(c);
                }
            }
            return builder?.ToString() ?? key;
        }

        private static bool NeedsEscape(char c) =>
            c < (char)0x20 || c == '%' || c == '<' || c == '>' || c == ':' || c == '"' || c == '/' || c == (char)0x5C || c == '|' || c == '?' || c == '*';
    }

    /// <summary>
    /// JsonUtility によるシリアライザ。[Serializable] なクラス / 構造体 / プリミティブに対応
    /// <code>
    /// var save = new SaveService(new FileSaveStorage(), new JsonUtilitySaveSerializer(prettyPrint: true));
    /// </code>
    /// </summary>
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
