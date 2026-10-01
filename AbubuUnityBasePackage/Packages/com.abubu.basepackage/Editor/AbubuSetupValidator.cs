using System.Collections.Generic;
using System.Linq;
using Abubu.Audio;
using Abubu.Effects;
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

            return issues;
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
            if (settings.Effect.Library == null) issues.Add(new Issue(Level.Warning, $"{name}: Effect > Library が未設定です。Fx.Play(\"key\") は動きません。"));

            var scenesInBuild = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToList();
            if (scenesInBuild.Count == 0)
            {
                issues.Add(new Issue(Level.Warning, "Build Profiles に有効なシーンがありません。ビルドしても起動するシーンがありません。"));
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
                    if (!entry.HasClip || entry.Clips.Any(c => c == null)) issues.Add(new Issue(Level.Warning, $"{name}: {category} \"{entry.Key}\" の Clip が未設定です。"));
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

        private static List<T> FindAssets<T>() where T : Object =>
            AssetDatabase.FindAssets($"t:{typeof(T).Name}", new[] { "Assets" })
                .Select(guid => AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(a => a != null)
                .ToList();
    }
}
