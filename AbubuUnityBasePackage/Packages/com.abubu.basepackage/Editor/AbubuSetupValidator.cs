using System.Collections.Generic;
using System.Linq;
using Abubu.Audio;
using Abubu.Effects;
using Abubu.Localization;
using UnityEditor;
using UnityEngine;
using Zenject;

namespace Abubu.Editor
{
    /// <summary>
    /// Tools > Abubu > Validate Setup
    /// 「動かない」ときに原因を探すための診断。結果は Console にまとめて出力する。
    /// </summary>
    /// <remarks>
    /// 何も変更しない (読み取り専用)。問題が見つかったときの直し方も一緒に表示する。
    /// </remarks>
    public static class AbubuSetupValidator
    {
        public enum Level
        {
            Info,
            Warning,
            Error,
        }

        public readonly struct Issue
        {
            public Level Level { get; }
            public string Message { get; }

            public Issue(Level level, string message)
            {
                Level = level;
                Message = message;
            }
        }

        [MenuItem("Tools/Abubu/Validate Setup", priority = 1)]
        public static void ValidateAndLog()
        {
            var issues = Validate();
            var errors = issues.Count(i => i.Level == Level.Error);
            var warnings = issues.Count(i => i.Level == Level.Warning);

            foreach (var issue in issues.Where(i => i.Level == Level.Error)) Debug.LogError("[Abubu.Validate] " + issue.Message);
            foreach (var issue in issues.Where(i => i.Level == Level.Warning)) Debug.LogWarning("[Abubu.Validate] " + issue.Message);
            foreach (var issue in issues.Where(i => i.Level == Level.Info)) Debug.Log("[Abubu.Validate] " + issue.Message);

            if (errors == 0 && warnings == 0)
            {
                Debug.Log("[Abubu.Validate] 問題は見つかりませんでした。");
            }
            else
            {
                Debug.Log($"[Abubu.Validate] エラー {errors} 件 / 警告 {warnings} 件");
            }
        }

        /// <summary>プロジェクトを調べて、見つかった問題の一覧を返す</summary>
        public static List<Issue> Validate()
        {
            var issues = new List<Issue>();
            CheckProjectContext(issues);

            var settingsAssets = FindAssets<AbubuSettings>();
            if (settingsAssets.Count == 0)
            {
                issues.Add(new Issue(Level.Error, "AbubuSettings がありません。Tools > Abubu > Setup Project を実行してください。"));
            }
            else
            {
                if (settingsAssets.Count > 1) issues.Add(new Issue(Level.Warning, $"AbubuSettings が {settingsAssets.Count} 個あります。ProjectContext で使われるのは AbubuInstaller に割り当てたものだけです。"));
                foreach (var settings in settingsAssets) CheckSettings(settings, issues);
            }

            foreach (var library in FindAssets<SoundLibrary>()) CheckSoundLibrary(library, issues);
            foreach (var library in FindAssets<EffectLibrary>()) CheckEffectLibrary(library, issues);
            var tables = FindAssets<LocalizationTable>();
            foreach (var table in tables) CheckLocalizationTable(table, issues);
            CheckLocalizedTextKeys(tables, issues);
            CheckInputAndEventSystems(issues);

            foreach (var type in FindUnboundScriptTypes())
            {
                issues.Add(new Issue(Level.Error, $"{type.FullName} は同名のファイルに定義されていないため、アセットにアタッチすると Missing Script になります。クラス名と同じ名前の .cs に移してください。"));
            }

            return issues;
        }

        /// <summary>
        /// MonoBehaviour / ScriptableObject を継承しているのに、ファイル名と一致せず Unity のスクリプトとして認識されない型を探す。
        /// (複数のクラスを 1 ファイルに置くと起きる。Abubu 系のアセンブリのみ対象)
        /// </summary>
        public static List<System.Type> FindUnboundScriptTypes() =>
            TypeCache.GetTypesDerivedFrom<MonoBehaviour>()
                .Concat(TypeCache.GetTypesDerivedFrom<ScriptableObject>())
                .Where(t => !t.IsAbstract && !t.IsGenericType && !t.IsNested && t.Assembly.GetName().Name.StartsWith("Abubu"))
                .Where(t => FindScript(t) == null)
                .OrderBy(t => t.FullName)
                .ToList();

        /// <summary>
        /// 型に対応する MonoScript (クラス名と同じ名前のファイル)。無ければ null。
        /// プロジェクトの全スクリプトを読み込むと重いので、型の名前で絞り込んでから読み込む
        /// </summary>
        private static MonoScript FindScript(System.Type type)
        {
            foreach (var guid in AssetDatabase.FindAssets($"{type.Name} t:MonoScript"))
            {
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid));
                if (script != null && script.GetClass() == type) return script;
            }
            return null;
        }

        /// <summary>
        /// シーン・プレハブに置かれた LocalizedText のキーのうち、どの翻訳テーブルにも無いものを探す。
        /// アセットを開かずにテキスト (YAML) を読んで調べる (Force Text シリアライズのとき)
        /// </summary>
        private static void CheckLocalizedTextKeys(List<LocalizationTable> tables, List<Issue> issues)
        {
            if (tables.Count == 0 || EditorSettings.serializationMode != SerializationMode.ForceText) return;
            var script = FindScript(typeof(LocalizedText));
            if (script == null || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(script, out var scriptGuid, out long _)) return;

            var known = new HashSet<string>(tables.SelectMany(t => t.GetAllKeys()));
            var reported = new HashSet<string>();
            foreach (var path in AssetDatabase.FindAssets("t:Prefab t:Scene", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath).Distinct())
            {
                foreach (var key in ReadLocalizedTextKeys(path, scriptGuid))
                {
                    if (known.Contains(key) || !reported.Add(key)) continue;
                    issues.Add(new Issue(Level.Warning, $"{path}: LocalizedText のキー \"{key}\" がどの翻訳テーブルにもありません。{KeySuggestion.Hint(key, known)}"));
                }
            }
        }

        private const string InputSystemModuleType = "UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem";

        /// <summary>
        /// 入力と EventSystem の設定。Active Input Handling と EventSystem の Input Module の組み合わせ、
        /// InputSystemUIInputModule のアクション未設定、UI/Navigate・UI/Cancel の有無を調べる (Input System は型名で調べるので必須にしない)
        /// </summary>
        private static void CheckInputAndEventSystems(List<Issue> issues)
        {
            var handler = ReadActiveInputHandler();
            var inputSystemType = System.Type.GetType(InputSystemModuleType);
            if (inputSystemType != null && handler == 0)
            {
                issues.Add(new Issue(Level.Warning, "Input System パッケージが入っていますが、Project Settings > Player > Active Input Handling が Input Manager (Old) です。Input System Package (New) か Both にしてください。"));
            }

            var standalone = FindScriptId(typeof(UnityEngine.EventSystems.StandaloneInputModule));
            var inputSystem = inputSystemType != null ? FindScriptId(inputSystemType) : null;
            if (standalone == null && inputSystem == null) return;

            var paths = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path)
                .Concat(AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath))
                .Distinct();
            foreach (var path in paths)
            {
                string text;
                try
                {
                    text = System.IO.File.ReadAllText(path);
                }
                catch (System.IO.IOException)
                {
                    continue;
                }

                if (standalone != null && handler == 1 && text.Contains(standalone))
                {
                    issues.Add(new Issue(Level.Error, $"{path}: StandaloneInputModule は Active Input Handling = Input System のみでは動きません (実行時に例外)。EventSystem の \"Replace with InputSystemUIInputModule\" を押してください。"));
                }
                if (inputSystem != null && text.Contains(inputSystem) && HasModuleWithoutActions(text, inputSystem))
                {
                    issues.Add(new Issue(Level.Warning, $"{path}: InputSystemUIInputModule の Actions Asset が未設定です。ゲームパッド・キーボードで UI を操作できません。"));
                }
            }

            if (inputSystemType != null) CheckUiActions(issues);
        }

        /// <summary>ProjectSettings の activeInputHandler (0 = 旧 Input Manager, 1 = Input System, 2 = 両方)。読めなければ -1</summary>
        private static int ReadActiveInputHandler()
        {
            try
            {
                var match = System.Text.RegularExpressions.Regex.Match(System.IO.File.ReadAllText("ProjectSettings/ProjectSettings.asset"), @"activeInputHandler:\s*(\d+)");
                return match.Success ? int.Parse(match.Groups[1].Value) : -1;
            }
            catch (System.IO.IOException)
            {
                return -1;
            }
        }

        /// <summary>シーン・プレハブの YAML でそのスクリプトを指す文字列 ("fileID: …, guid: …")。一時的にコンポーネントを付けて調べる</summary>
        private static string FindScriptId(System.Type type)
        {
            var go = new GameObject("[Abubu.Validate]") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var component = go.AddComponent(type) as MonoBehaviour;
                var script = component != null ? MonoScript.FromMonoBehaviour(component) : null;
                if (script == null || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(script, out var guid, out long fileId)) return null;
                return $"fileID: {fileId}, guid: {guid}";
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private static bool HasModuleWithoutActions(string text, string scriptId)
        {
            foreach (var block in text.Split(new[] { "\n--- " }, System.StringSplitOptions.None))
            {
                if (block.Contains(scriptId) && block.Contains("m_ActionsAsset: {fileID: 0}")) return true;
            }
            return false;
        }

        /// <summary>Input Actions (.inputactions) のどれかに、UI 操作で使う Navigate / Cancel があるか</summary>
        private static void CheckUiActions(List<Issue> issues)
        {
            var assets = AssetDatabase.FindAssets("t:InputActionAsset", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath).ToList();
            if (assets.Count == 0) return;

            foreach (var path in assets)
            {
                string json;
                try
                {
                    json = System.IO.File.ReadAllText(path);
                }
                catch (System.IO.IOException)
                {
                    continue;
                }
                if (json.Contains("\"Navigate\"") && json.Contains("\"Cancel\"")) return;
            }
            issues.Add(new Issue(Level.Warning, "どの Input Actions にも UI/Navigate・UI/Cancel がありません。ゲームパッドでの UI 操作と「戻る」が動きません (Unity 6 の InputSystem_Actions には含まれています)。"));
        }

        /// <summary>シーン・プレハブの YAML から、指定したスクリプトのコンポーネントの key フィールドを読む</summary>
        internal static IEnumerable<string> ReadLocalizedTextKeys(string path, string scriptGuid)
        {
            string text;
            try
            {
                text = System.IO.File.ReadAllText(path);
            }
            catch (System.IO.IOException)
            {
                yield break;
            }
            if (!text.Contains(scriptGuid)) yield break;

            foreach (var block in text.Split(new[] { "\n--- " }, System.StringSplitOptions.None))
            {
                if (!block.Contains("guid: " + scriptGuid)) continue;
                foreach (var rawLine in block.Split('\n'))
                {
                    var line = rawLine.TrimEnd('\r');
                    if (!line.StartsWith("  key: ", System.StringComparison.Ordinal)) continue;
                    var value = line.Substring("  key: ".Length).Trim();
                    if (value.Length >= 2 && (value[0] == '\'' || value[0] == '"')) value = value.Substring(1, value.Length - 2);
                    if (value.Length > 0) yield return value;
                }
            }
        }

        private static void CheckProjectContext(List<Issue> issues)
        {
            var path = AssetDatabase.FindAssets("ProjectContext t:Prefab")
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => p.EndsWith("/Resources/" + ProjectContext.ProjectContextResourcePath + ".prefab"));
            if (path == null)
            {
                issues.Add(new Issue(Level.Error, "Resources/ProjectContext.prefab がありません。Tools > Abubu > Setup Project を実行してください。"));
                return;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var context = prefab != null ? prefab.GetComponent<ProjectContext>() : null;
            if (context == null)
            {
                issues.Add(new Issue(Level.Error, $"{path} に ProjectContext コンポーネントがありません。"));
                return;
            }

            var installer = prefab.GetComponent<AbubuInstaller>();
            if (installer == null)
            {
                issues.Add(new Issue(Level.Error, $"{path} に AbubuInstaller がありません。Tools > Abubu > Setup Project を実行してください。"));
            }
            else if (!context.Installers.Contains(installer))
            {
                issues.Add(new Issue(Level.Error, $"{path}: AbubuInstaller が ProjectContext の Installers に登録されていません。"));
            }
        }

        private static void CheckSettings(AbubuSettings settings, List<Issue> issues)
        {
            var name = AssetDatabase.GetAssetPath(settings);

            if (settings.Sound.Library == null) issues.Add(new Issue(Level.Warning, $"{name}: Sound > Library が未設定です。Sound.PlaySe(\"key\") は鳴りません。"));
            if (settings.Effects.Library == null) issues.Add(new Issue(Level.Warning, $"{name}: Effects > Library が未設定です。Fx.Play(\"key\") は動きません。"));

            var scenesInBuild = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToList();
            if (scenesInBuild.Count == 0)
            {
                issues.Add(new Issue(Level.Warning, "Build Profiles に有効なシーンがありません。ビルドしても起動するシーンがありません。"));
            }

            if (settings.Save.FolderName == "Save")
            {
                issues.Add(new Issue(Level.Info, $"{name}: Save > Folder Name が \"Save\" です。同じ persistentDataPath を使う他のセーブデータと衝突する可能性があります (既定値は \"AbubuSave\")。"));
            }

            CheckSceneRegistered(name, "Boot > Boot Scene Name", settings.Boot.BootSceneName, scenesInBuild, issues);
            CheckSceneRegistered(name, "Boot > First Scene Name", settings.Boot.FirstSceneName, scenesInBuild, issues);
        }

        private static void CheckSceneRegistered(string owner, string label, string sceneName, List<string> scenesInBuild, List<Issue> issues)
        {
            if (string.IsNullOrEmpty(sceneName)) return;
            var found = scenesInBuild.Any(p => System.IO.Path.GetFileNameWithoutExtension(p) == sceneName || p == sceneName);
            if (!found)
            {
                issues.Add(new Issue(Level.Error, $"{owner}: {label} の \"{sceneName}\" が Build Profiles のシーン一覧にありません (有効にして追加してください)。"));
            }
        }

        private static void CheckSoundLibrary(SoundLibrary library, List<Issue> issues)
        {
            var name = AssetDatabase.GetAssetPath(library);
            foreach (SoundCategory category in System.Enum.GetValues(typeof(SoundCategory)))
            {
                var seen = new HashSet<string>();
                foreach (var entry in library.GetEntries(category))
                {
                    if (entry == null) continue;
                    if (string.IsNullOrEmpty(entry.Key))
                    {
                        issues.Add(new Issue(Level.Warning, $"{name}: {category} にキーが空のエントリがあります。"));
                        continue;
                    }

                    if (!seen.Add(entry.Key)) issues.Add(new Issue(Level.Warning, $"{name}: {category} のキー \"{entry.Key}\" が重複しています (先に登録されたものだけが使われます)。"));
                    if (!entry.HasSource || (entry.HasClip && entry.Clips.Any(c => c == null))) issues.Add(new Issue(Level.Warning, $"{name}: {category} \"{entry.Key}\" の Clip が未設定です。"));
                }
            }
        }

        private static void CheckEffectLibrary(EffectLibrary library, List<Issue> issues)
        {
            var name = AssetDatabase.GetAssetPath(library);
            var seen = new HashSet<string>();
            var seKeys = new HashSet<string>(FindAssets<SoundLibrary>().SelectMany(l => l.GetKeys(SoundCategory.Se)));

            foreach (var entry in library.Effects)
            {
                if (entry == null) continue;
                if (string.IsNullOrEmpty(entry.Key))
                {
                    issues.Add(new Issue(Level.Warning, $"{name}: キーが空のエントリがあります。"));
                    continue;
                }

                if (!seen.Add(entry.Key)) issues.Add(new Issue(Level.Warning, $"{name}: キー \"{entry.Key}\" が重複しています (先に登録されたものだけが使われます)。"));
                if (entry.Prefab == null) issues.Add(new Issue(Level.Warning, $"{name}: \"{entry.Key}\" の Prefab が未設定です。"));
                if (!string.IsNullOrEmpty(entry.SeKey) && !seKeys.Contains(entry.SeKey))
                {
                    issues.Add(new Issue(Level.Warning, $"{name}: \"{entry.Key}\" の SeKey \"{entry.SeKey}\" が SoundLibrary の SE に見つかりません。{KeySuggestion.Hint(entry.SeKey, seKeys)}"));
                }
            }
        }

        private static void CheckLocalizationTable(LocalizationTable table, List<Issue> issues)
        {
            var name = AssetDatabase.GetAssetPath(table);
            var seen = new HashSet<string>();
            foreach (var entry in table.Entries)
            {
                if (entry == null) continue;
                if (string.IsNullOrWhiteSpace(entry.Key))
                {
                    issues.Add(new Issue(Level.Warning, $"{name}: キーが空のエントリがあります。"));
                    continue;
                }
                if (!seen.Add(entry.Key.Trim())) issues.Add(new Issue(Level.Warning, $"{name}: キー \"{entry.Key}\" が重複しています (後に登録されたものが使われます)。"));
            }

            if (table.CsvSources.Any(c => c == null)) issues.Add(new Issue(Level.Warning, $"{name}: Csv Sources に未設定の項目があります。"));

            // 言語ごとの未翻訳キー。数が多くなりがちなので件数と先頭のいくつかだけを出す
            var built = table.Build();
            var keys = built.Values.SelectMany(v => v.Keys).Distinct().ToList();
            foreach (var language in table.GetAllLanguages())
            {
                var values = built.TryGetValue(language, out var v) ? v : new Dictionary<string, string>();
                var missing = keys.Where(k => !values.ContainsKey(k)).OrderBy(k => k, System.StringComparer.Ordinal).ToList();
                if (missing.Count == 0) continue;
                var sample = string.Join(", ", missing.Take(5).Select(k => $"\"{k}\""));
                issues.Add(new Issue(Level.Warning, $"{name}: {language} に未翻訳のキーが {missing.Count} 件あります ({sample}{(missing.Count > 5 ? " ..." : "")})。フォールバック言語の文字列が使われます。"));
            }
        }

        private static List<T> FindAssets<T>() where T : Object =>
            AssetDatabase.FindAssets($"t:{typeof(T).Name}", new[] { "Assets" })
                .Select(guid => AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(a => a != null)
                .ToList();
    }
}
