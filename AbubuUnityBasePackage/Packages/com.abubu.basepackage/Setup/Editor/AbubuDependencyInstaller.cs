using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Compilation;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Abubu.Setup
{
    /// <summary>
    /// 依存ライブラリ (UniTask / R3 / Zenject / uPools / LitMotion など) の導入状況を確認し、不足分のインストールを提案する。
    /// このアセンブリは他パッケージに依存しないため、依存が未導入の状態でもコンパイルされて動作する。
    /// </summary>
    /// <remarks>
    /// 既存プロジェクトへの導入を考慮し、プロジェクトを黙って書き換えることはしない (必ずダイアログで確認する)。
    /// <list type="number">
    /// <item>依存の有無は「アセンブリが存在するか」で判定する。Asset Store / .unitypackage で Assets/Plugins などに
    /// 入っている Zenject や UniTask も導入済みとみなし、UPM 版を重ねてインストールしない (アセンブリの重複でコンパイルエラーになるため)。</item>
    /// <item>Abubu の各 asmdef は UPM パッケージ名の versionDefines で ABUBU_DEPS_* を定義している。
    /// UPM 以外で導入されたライブラリではこのシンボルが定義されず Abubu のアセンブリが丸ごと無効になるため、
    /// その場合は Scripting Define Symbols への追加を提案する (defineConstraints はグローバルなシンボルも受け付ける)。</item>
    /// <item>どちらも無い依存は Packages/manifest.json に git URL を追記し、Package Manager に一括で解決させる。
    /// R3 本体などの NuGet 配布物は、NuGetForUnity が読む Assets/packages.config に追記する。
    /// 既存の記述 (他のパッケージ・コメント・書式) はそのまま残し、足りない行だけを差し込む。</item>
    /// </list>
    /// 起動時の確認は「今後表示しない」で止められる (プロジェクトごとに EditorPrefs に保存)。
    /// Tools/Abubu/Install Dependencies/Check Dependencies からいつでも再確認できる。
    /// バージョンはタグやコミットハッシュで固定し、環境によって結果が変わらないようにしている。
    /// </remarks>
    [InitializeOnLoad]
    public static class AbubuDependencyInstaller
    {
        /// <summary>依存ライブラリ 1 件分の情報</summary>
        private sealed class Dependency
        {
            /// <summary>UPM パッケージ名</summary>
            public readonly string Package;
            /// <summary>manifest に書く git URL またはバージョン</summary>
            public readonly string Url;
            /// <summary>導入済みかを判定するアセンブリ名 (asmdef の name / DLL 名)。いずれかがあれば導入済み</summary>
            public readonly string[] Assemblies;
            /// <summary>Abubu の asmdef が versionDefines で定義するシンボル (無ければ null)</summary>
            public readonly string Define;

            public Dependency(string package, string url, string define, params string[] assemblies)
            {
                Package = package;
                Url = url;
                Define = define;
                Assemblies = assemblies;
            }
        }

        /// <summary>Abubu の動作に必須</summary>
        private static readonly Dependency[] Required =
        {
            new("com.cysharp.unitask", "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask#2.5.11",
                "ABUBU_DEPS_UNITASK", "UniTask"),
            // R3 本体 (NuGet) を入れるためだけに使う。R3 本体が既にあれば不要 (IsSatisfied で特別扱い)
            new("com.github-glitchenzo.nugetforunity", "https://github.com/GlitchEnzo/NuGetForUnity.git?path=/src/NuGetForUnity#v4.5.0",
                null, "NuGetForUnity"),
            new("com.cysharp.r3", "https://github.com/Cysharp/R3.git?path=src/R3.Unity/Assets/R3.Unity#1.3.1",
                "ABUBU_DEPS_R3", "R3.Unity"),
            new("com.svermeulen.extenject", "https://github.com/modesttree/Zenject.git?path=UnityProject/Assets/Plugins/Zenject#31f06cf81043f04301b1072ec81922b64149ea34",
                "ABUBU_DEPS_ZENJECT", "Zenject"),
            new("com.annulusgames.u-pools", "https://github.com/AnnulusGames/uPools.git?path=/Assets/uPools#v1.0.1",
                "ABUBU_DEPS_UPOOLS", "uPools"),
            new("com.annulusgames.lit-motion", "https://github.com/annulusgames/LitMotion.git?path=src/LitMotion/Assets/LitMotion#v2.0.2",
                "ABUBU_DEPS_LITMOTION", "LitMotion"),
        };

        /// <summary>Base Package としてあると便利なもの (Install All で一緒に入る)</summary>
        private static readonly Dependency[] Optional =
        {
            new("com.cysharp.messagepipe", "https://github.com/Cysharp/MessagePipe.git?path=src/MessagePipe.Unity/Assets/Plugins/MessagePipe#1.8.1",
                null, "MessagePipe"),
            new("com.cysharp.messagepipe.zenject", "https://github.com/Cysharp/MessagePipe.git?path=src/MessagePipe.Unity/Assets/Plugins/MessagePipe.Zenject#1.8.1",
                "ABUBU_MESSAGEPIPE", "MessagePipe.Zenject"),
            new("com.tanitaka-tech.state-variable", "https://github.com/tanitaka-tech/StateVariable.git#v1.4.3",
                null, "StateVariable"),
            new("com.tanitaka-tech.unity-process-manager", "https://github.com/tanitaka-tech/UnityProcessManager.git#v1.1.2",
                null, "UnityProcessManager"),
            new("net.tnrd.serializableinterface", "https://github.com/Thundernerd/Unity3D-SerializableInterface.git#v2.3.0",
                null, "TNRD.SerializableInterface"),
            // Addressables は UPM でしか配布されないため、シンボルは versionDefines だけで足りる
            new("com.unity.addressables", "2.10.3",
                null, "Unity.Addressables"),
            new("jp.co.cyberagent.smartaddresser", "https://github.com/CyberAgentGameEntertainment/SmartAddresser.git?path=/Assets/SmartAddresser#1.2.0",
                null, "SmartAddresser.Editor"),
            new("com.annulusgames.lucid-random", "https://github.com/AnnulusGames/LucidRandom.git?path=/Assets/LucidRandom#v1.1.0",
                null, "AnnulusGames.LucidRandom.Runtime"),
            new("com.annulusgames.tween-playables", "https://github.com/AnnulusGames/TweenPlayables.git?path=/Assets/TweenPlayables#v0.9.1",
                null, "AnnulusGames.TweenPlayables.Runtime"),
            new("com.annulusgames.scene-system", "https://github.com/AnnulusGames/SceneSystem.git?path=/Assets/SceneSystem#v1.0.0",
                null, "AnnulusGames.SceneSystem"),
        };

        /// <summary>
        /// R3 本体などの NuGet 配布物 (NuGetForUnity の packages.config に書き込む)。
        /// NuGetForUnity v4 は依存を自動解決しないため、依存 DLL もすべて列挙している。
        /// id はそのまま DLL のアセンブリ名でもあり、既に DLL がプロジェクトにあれば追記しない。
        /// optional = true のもの (ObservableCollections) は StateVariable 用なので Install All のときだけ入れる。
        /// </summary>
        private static readonly (string id, string version, bool manual, bool optional)[] NuGetPackages =
        {
            ("Microsoft.Bcl.AsyncInterfaces", "8.0.0", false, false),
            ("Microsoft.Bcl.TimeProvider", "8.0.0", false, false),
            ("ObservableCollections", "3.3.4", false, true),
            ("ObservableCollections.R3", "3.3.4", true, true),
            ("R3", "1.3.1", true, false),
            ("System.ComponentModel.Annotations", "5.0.0", false, false),
            ("System.Runtime.CompilerServices.Unsafe", "6.0.0", false, false),
            ("System.Threading.Channels", "8.0.0", false, false),
        };

        private const string ManifestPath = "Packages/manifest.json";
        private const string PackagesConfigPath = "Assets/packages.config";

        /// <summary>ダイアログ表示済みかを記録する SessionState キー (再コンパイルのたびに出さないため)</summary>
        private const string PromptedKey = "Abubu.DependencyPrompted";

        /// <summary>
        /// 起動時の確認を止めたかを記録する EditorPrefs キー。
        /// EditorPrefs はマシン全体で共有されるため、プロジェクトごとの GUID を付けて他のプロジェクトに影響しないようにする
        /// (ProjectSettings を書き換えないので、チームの他のメンバーにも影響しない)。
        /// </summary>
        private static string SuppressKey => "Abubu.DependencyCheck.Suppress." + PlayerSettings.productGUID;

        private const string MenuRoot = "Tools/Abubu/Install Dependencies/";
        private const string AskOnStartupMenu = MenuRoot + "Ask On Startup";

        /// <summary>
        /// エディタ起動時に依存の状況を確認し、必要なら対応を提案する。
        /// </summary>
        static AbubuDependencyInstaller()
        {
            if (Application.isBatchMode) return;

            // 静的コンストラクタ内ではダイアログを出せない (エディタの初期化中) ため、次のエディタ更新まで遅らせる
            EditorApplication.delayCall += () =>
            {
                if (SessionState.GetBool(PromptedKey, false)) return;
                SessionState.SetBool(PromptedKey, true);
                if (EditorPrefs.GetBool(SuppressKey, false)) return;
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;

                RunCheck(interactive: false);
            };
        }

        /// <summary>依存の状況を確認し直す (起動時の確認を止めていても実行する)。</summary>
        [MenuItem(MenuRoot + "Check Dependencies...", priority = 99)]
        public static void CheckDependencies() => RunCheck(interactive: true);

        /// <summary>起動時に依存の確認ダイアログを出すかどうかを切り替える。</summary>
        [MenuItem(AskOnStartupMenu, priority = 120)]
        private static void ToggleAskOnStartup() => EditorPrefs.SetBool(SuppressKey, !EditorPrefs.GetBool(SuppressKey, false));

        [MenuItem(AskOnStartupMenu, true)]
        private static bool ToggleAskOnStartupValidate()
        {
            Menu.SetChecked(AskOnStartupMenu, !EditorPrefs.GetBool(SuppressKey, false));
            return true;
        }

        /// <summary>必須パッケージのうち、まだ無いものだけをインストールする。</summary>
        [MenuItem(MenuRoot + "Required", priority = 100)]
        public static void InstallRequired() => ConfirmAndInstall(Required, includeOptionalNuGet: false);

        /// <summary>必須パッケージと任意パッケージのうち、まだ無いものをまとめてインストールする。</summary>
        [MenuItem(MenuRoot + "All (Required + Optional)", priority = 101)]
        public static void InstallAll() => ConfirmAndInstall(Required.Concat(Optional).ToArray(), includeOptionalNuGet: true);

        /// <summary>
        /// manifest.json に書かれたバージョンが、このパッケージが想定する版 (上の Required / Optional の表) と違わないか確認する。
        /// 導入済みのバージョンは書き換えない (確認して報告するだけ)。
        /// </summary>
        /// <remarks>
        /// 推奨バージョンの正本はこのファイルの表。README の表を更新するときもここに合わせること。
        /// </remarks>
        [MenuItem(MenuRoot + "Check Versions", priority = 102)]
        public static void CheckVersions()
        {
            var manifest = File.Exists(ManifestPath) ? File.ReadAllText(ManifestPath) : string.Empty;
            var mismatches = new List<string>();
            var checkedCount = 0;

            foreach (var dep in Required.Concat(Optional))
            {
                var match = Regex.Match(manifest, "\"" + Regex.Escape(dep.Package) + @"""\s*:\s*""([^""]*)""");
                if (!match.Success) continue;

                checkedCount++;
                if (match.Groups[1].Value != dep.Url)
                {
                    mismatches.Add($"・{dep.Package}\n    導入済み: {match.Groups[1].Value}\n    推奨: {dep.Url}");
                }
            }

            if (mismatches.Count == 0)
            {
                Debug.Log($"[Abubu] manifest.json に記載された依存 {checkedCount} 件はすべて推奨バージョンです。" +
                          "(Assets 以下などに直接入っているライブラリは確認対象外)");
                return;
            }

            Debug.LogWarning("[Abubu] 推奨と異なるバージョンの依存があります (動作確認は推奨バージョンで行っています):\n" + string.Join("\n", mismatches));
        }

        // ---------------------------------------------------------------------------------------------
        // 確認の本体
        // ---------------------------------------------------------------------------------------------

        /// <summary>プロジェクト内のアセンブリと UPM パッケージの一覧 (確認 1 回分のスナップショット)</summary>
        private sealed class ProjectState
        {
            public readonly HashSet<string> Assemblies = new(StringComparer.Ordinal);
            public readonly HashSet<string> Packages = new(StringComparer.Ordinal);
            public string Manifest = string.Empty;

            public static ProjectState Capture()
            {
                var state = new ProjectState();

                // asmdef から作られるアセンブリ (Editor 用には Runtime 用も含まれる)
                foreach (var assembly in CompilationPipeline.GetAssemblies(AssembliesType.Editor))
                {
                    state.Assemblies.Add(assembly.name);
                }

                // Assets/Plugins などに置かれた DLL (Unity / .NET 自体の DLL は含まない)
                try
                {
                    foreach (var path in CompilationPipeline.GetPrecompiledAssemblyPaths(CompilationPipeline.PrecompiledAssemblySources.UserAssembly))
                    {
                        state.Assemblies.Add(Path.GetFileNameWithoutExtension(path));
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[Abubu] DLL の一覧を取得できませんでした: " + e.Message);
                }

                // 依存として間接的に入っているものも含めて、登録済みの UPM パッケージ (versionDefines はこれを見る)
                foreach (var info in UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages())
                {
                    state.Packages.Add(info.name);
                }

                state.Manifest = File.Exists(ManifestPath) ? File.ReadAllText(ManifestPath) : string.Empty;
                return state;
            }

            public bool HasAssembly(Dependency dep) => dep.Assemblies.Any(Assemblies.Contains);

            /// <summary>UPM パッケージとして登録済み、または manifest.json に記載済み (解決待ち) か</summary>
            public bool HasPackage(Dependency dep) => Packages.Contains(dep.Package) || Manifest.Contains($"\"{dep.Package}\"");

            /// <summary>R3 本体 (NuGet 配布の DLL) がプロジェクトにあるか</summary>
            public bool HasR3Core => Assemblies.Contains("R3");

            /// <summary>この依存がすでに満たされているか (どこから入ったかは問わない)</summary>
            public bool IsSatisfied(Dependency dep)
            {
                if (HasPackage(dep) || HasAssembly(dep)) return true;
                // NuGetForUnity は R3 本体を入れるためだけに必要。R3 本体が別の方法で入っていれば不要
                return dep.Package == "com.github-glitchenzo.nugetforunity" && HasR3Core;
            }
        }

        /// <param name="interactive">メニューから実行された場合は true。問題が無いときもその旨を表示する</param>
        private static void RunCheck(bool interactive)
        {
            var state = ProjectState.Capture();
            var all = Required.Concat(Optional).ToArray();
            var globalDefines = new HashSet<string>(GetDefines(BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget)));

            // 1. UPM 以外から入っていて、versionDefines ではシンボルが定義されないもの
            var definesToAdd = all
                .Where(d => d.Define != null && !state.Packages.Contains(d.Package) && state.HasAssembly(d) && !globalDefines.Contains(d.Define))
                .ToArray();

            // 2. シンボルだけが残っていて、ライブラリ本体が無いもの (参照が解決できずコンパイルエラーになる)
            var staleDefines = all
                .Where(d => d.Define != null && globalDefines.Contains(d.Define) && !state.HasAssembly(d) && !state.HasPackage(d))
                .ToArray();

            // 3. どの方法でも入っていない必須の依存
            var missing = Required.Where(d => !state.IsSatisfied(d)).ToArray();
            var needsNuGet = !state.HasR3Core && !HasNuGetEntries(includeOptional: false);

            var shown = false;

            if (definesToAdd.Length > 0)
            {
                shown = true;
                var list = string.Join("\n", definesToAdd.Select(d => $"・{d.Define}  ({string.Join("/", d.Assemblies)} を検出)"));
                var choice = EditorUtility.DisplayDialogComplex("Abubu Base Package",
                    "次のライブラリは Package Manager 以外 (Assets/Plugins など) から導入されているため、" +
                    "Abubu のアセンブリが必要とするシンボルが定義されていません。\n\n" + list +
                    "\n\nPlayer Settings の Scripting Define Symbols (全ビルドターゲット) に追加しますか?",
                    "追加する", "あとで", "今後この確認を表示しない");
                if (choice == 0) AddDefines(definesToAdd.Select(d => d.Define));
                else if (choice == 2) { Suppress(); return; }
            }

            if (staleDefines.Length > 0)
            {
                shown = true;
                var list = string.Join("\n", staleDefines.Select(d => $"・{d.Define}  ({string.Join("/", d.Assemblies)} が見つかりません)"));
                var choice = EditorUtility.DisplayDialogComplex("Abubu Base Package",
                    "Scripting Define Symbols に次のシンボルがありますが、対応するライブラリが見つかりません。" +
                    "このままでは Abubu のアセンブリがコンパイルエラーになります。\n\n" + list +
                    "\n\nScripting Define Symbols (全ビルドターゲット) から削除しますか?",
                    "削除する", "あとで", "今後この確認を表示しない");
                if (choice == 0) RemoveDefines(staleDefines.Select(d => d.Define));
                else if (choice == 2) { Suppress(); return; }
            }

            if (missing.Length > 0 || needsNuGet)
            {
                shown = true;
                var lines = missing.Select(m => "・" + m.Package).ToList();
                if (needsNuGet) lines.Add("・R3 本体などの NuGet パッケージ (Assets/packages.config に追記)");
                var choice = EditorUtility.DisplayDialogComplex("Abubu Base Package",
                    $"必須の依存ライブラリが見つかりません。\n{string.Join("\n", lines)}\n\n" +
                    "Package Manager でインストールしますか?\n" +
                    "(Asset Store などから別の方法で導入する場合は「あとで」を選び、導入後に " +
                    "Tools/Abubu/Install Dependencies/Check Dependencies を実行してください)",
                    "インストール", "あとで", "今後この確認を表示しない");
                if (choice == 0) Install(missing, state, includeOptionalNuGet: false);
                else if (choice == 2) { Suppress(); return; }
            }

            if (!shown && interactive)
            {
                EditorUtility.DisplayDialog("Abubu Base Package", "必須の依存ライブラリはすべて揃っています。", "OK");
            }
        }

        private static void Suppress()
        {
            EditorPrefs.SetBool(SuppressKey, true);
            Debug.Log("[Abubu] 起動時の依存確認を止めました。再開するには Tools/Abubu/Install Dependencies/Ask On Startup をオンにしてください。");
        }

        // ---------------------------------------------------------------------------------------------
        // インストール
        // ---------------------------------------------------------------------------------------------

        /// <summary>インストール対象を確認してから実行する (メニューから呼ばれる)。</summary>
        private static void ConfirmAndInstall(Dependency[] packages, bool includeOptionalNuGet)
        {
            var state = ProjectState.Capture();
            var targets = packages.Where(d => !state.IsSatisfied(d)).ToArray();
            var skipped = packages.Where(d => !state.HasPackage(d) && state.HasAssembly(d)).ToArray();
            var nuGet = MissingNuGetPackages(state, includeOptionalNuGet);

            if (targets.Length == 0 && nuGet.Length == 0)
            {
                var message = "インストールが必要な依存はありません。";
                if (skipped.Length > 0)
                {
                    message += "\n\n次のライブラリは Package Manager 以外から導入済みのため、UPM 版はインストールしません:\n" +
                               string.Join("\n", skipped.Select(d => "・" + d.Package));
                }
                EditorUtility.DisplayDialog("Abubu Base Package", message, "OK");
                return;
            }

            var lines = targets.Select(d => "・" + d.Package).Concat(nuGet.Select(n => $"・{n.id} (NuGet)"));
            var body = "次の依存をインストールします。\n" + string.Join("\n", lines);
            if (skipped.Length > 0)
            {
                body += "\n\n導入済み (UPM 以外) のためスキップ:\n" + string.Join("\n", skipped.Select(d => "・" + d.Package));
            }
            body += "\n\nPackages/manifest.json" + (nuGet.Length > 0 ? " と Assets/packages.config" : "") + " に追記します。";

            if (!EditorUtility.DisplayDialog("Abubu Base Package", body, "インストール", "キャンセル")) return;
            Install(targets, state, includeOptionalNuGet);
        }

        /// <summary>
        /// packages.config と manifest.json に、足りないものだけを追記して解決させる。
        /// 既に登録済みのパッケージはバージョンを含めて変更しない。
        /// </summary>
        private static void Install(Dependency[] missing, ProjectState state, bool includeOptionalNuGet)
        {
            var nuGet = MissingNuGetPackages(state, includeOptionalNuGet);
            if (nuGet.Length > 0 && !WritePackagesConfig(nuGet)) return;

            var packages = missing.Where(d => !state.IsSatisfied(d)).ToArray();
            if (packages.Length == 0)
            {
                Debug.Log("[Abubu] 依存パッケージはすべて導入済みです。");
                AssetDatabase.Refresh();
                return;
            }

            if (!File.Exists(ManifestPath))
            {
                Debug.LogError("[Abubu] Packages/manifest.json が見つかりません。");
                return;
            }

            // manifest.json を直接書き換えると一括で解決されるため、Client.Add を個別に呼ぶより速く確実
            var manifest = File.ReadAllText(ManifestPath);
            if (!TryInsertDependencies(manifest, packages.Select(p => (p.Package, p.Url)).ToArray(), out var updated))
            {
                Debug.LogError("[Abubu] manifest.json に \"dependencies\" が見つからないため、依存を追加できませんでした。手動で追加してください:\n" +
                               string.Join("\n", packages.Select(p => $"\"{p.Package}\": \"{p.Url}\"")));
                return;
            }
            File.WriteAllText(ManifestPath, updated);

            Debug.Log("[Abubu] 依存パッケージを追加しました:\n" + string.Join("\n", packages.Select(p => p.Package)));
            Client.Resolve();
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// "dependencies": { の直後に項目を差し込む。既存の項目がある場合は各項目の後ろにカンマを付け、
        /// 空のオブジェクト ({}) の場合は最後の項目にカンマを付けない (末尾カンマは JSON として不正)。
        /// 改行コードは元のファイルに合わせ、それ以外の部分には触れない。
        /// </summary>
        private static bool TryInsertDependencies(string manifest, (string name, string url)[] packages, out string result)
        {
            result = manifest;
            var match = Regex.Match(manifest, "\"dependencies\"\\s*:\\s*\\{");
            if (!match.Success) return false;

            var newline = manifest.Contains("\r\n") ? "\r\n" : "\n";
            var rest = manifest.Substring(match.Index + match.Length);
            var isEmpty = rest.TrimStart().StartsWith("}", StringComparison.Ordinal);

            var entries = packages.Select(p => $"{newline}    \"{p.name}\": \"{p.url}\"").ToArray();
            var insert = isEmpty
                ? string.Join(",", entries) + newline + "  "
                : string.Concat(entries.Select(e => e + ","));

            // 空の場合は既存の空白 ("{ }" の間など) を詰めてから差し込む
            if (isEmpty) rest = rest.TrimStart();
            result = manifest.Substring(0, match.Index + match.Length) + insert + rest;
            return true;
        }

        // ---------------------------------------------------------------------------------------------
        // NuGet (packages.config)
        // ---------------------------------------------------------------------------------------------

        /// <summary>
        /// 追記が必要な NuGet パッケージ。R3 本体が既にあれば R3 一式は不要。
        /// DLL がプロジェクトにあるもの、packages.config に記載済みのものは除く。
        /// </summary>
        private static (string id, string version, bool manual, bool optional)[] MissingNuGetPackages(ProjectState state, bool includeOptional)
        {
            var config = File.Exists(PackagesConfigPath) ? File.ReadAllText(PackagesConfigPath) : string.Empty;
            return NuGetPackages
                .Where(p => includeOptional || !p.optional)
                .Where(p => p.optional || !state.HasR3Core)
                .Where(p => !state.Assemblies.Contains(p.id))
                .Where(p => !config.Contains($"id=\"{p.id}\""))
                .ToArray();
        }

        /// <summary>packages.config に R3 一式 (と任意で ObservableCollections) が記載されているか。</summary>
        private static bool HasNuGetEntries(bool includeOptional)
        {
            if (!File.Exists(PackagesConfigPath)) return false;
            var config = File.ReadAllText(PackagesConfigPath);
            return NuGetPackages.Where(p => includeOptional || !p.optional).All(p => config.Contains($"id=\"{p.id}\""));
        }

        /// <summary>
        /// packages.config に足りない &lt;package&gt; 要素だけを &lt;/packages&gt; の直前に差し込む。
        /// 既存の要素・コメント・書式はそのまま残す (ファイル全体を書き直さない)。
        /// NuGetForUnity は次回の再読み込み時にこのファイルを見て DLL を復元する。
        /// </summary>
        private static bool WritePackagesConfig((string id, string version, bool manual, bool optional)[] packages)
        {
            string Element((string id, string version, bool manual, bool optional) p) =>
                $"<package id=\"{p.id}\" version=\"{p.version}\"{(p.manual ? " manuallyInstalled=\"true\"" : "")} />";

            if (!File.Exists(PackagesConfigPath))
            {
                var body = string.Join("\n", packages.Select(p => "  " + Element(p)));
                File.WriteAllText(PackagesConfigPath, $"<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<packages>\n{body}\n</packages>\n");
                return true;
            }

            var config = File.ReadAllText(PackagesConfigPath);
            var newline = config.Contains("\r\n") ? "\r\n" : "\n";
            var lines = string.Concat(packages.Select(p => "  " + Element(p) + newline));

            var close = config.LastIndexOf("</packages>", StringComparison.Ordinal);
            if (close >= 0)
            {
                // </packages> の行頭に差し込む (直前が改行でなければ改行を補う)
                var lineStart = config.LastIndexOf('\n', Math.Max(0, close - 1)) + 1;
                var beforeClose = config.Substring(lineStart, close - lineStart);
                var insertAt = string.IsNullOrWhiteSpace(beforeClose) ? lineStart : close;
                var prefix = insertAt == close ? newline : string.Empty;
                File.WriteAllText(PackagesConfigPath, config.Insert(insertAt, prefix + lines));
                return true;
            }

            // <packages /> のように空要素で書かれている場合は開き・閉じタグに展開する
            var selfClosing = Regex.Match(config, @"<packages(\s[^>]*)?/>");
            if (selfClosing.Success)
            {
                var attrs = selfClosing.Groups[1].Value.TrimEnd();
                var replacement = $"<packages{attrs}>{newline}{lines}</packages>";
                File.WriteAllText(PackagesConfigPath, config.Substring(0, selfClosing.Index) + replacement +
                                                      config.Substring(selfClosing.Index + selfClosing.Length));
                return true;
            }

            Debug.LogError("[Abubu] Assets/packages.config の形式を解釈できないため変更しませんでした。次の行を手動で追加してください:\n" +
                           string.Join("\n", packages.Select(Element)));
            return false;
        }

        // ---------------------------------------------------------------------------------------------
        // Scripting Define Symbols
        // ---------------------------------------------------------------------------------------------

        /// <summary>
        /// シンボルを設定する対象のビルドターゲット。廃止済みのものと Unknown を除いたすべての BuildTargetGroup と、
        /// Dedicated Server (BuildTargetGroup では表せない) を対象にする。
        /// </summary>
        private static IEnumerable<NamedBuildTarget> AllNamedBuildTargets()
        {
            var seen = new HashSet<string>();
            foreach (var field in typeof(BuildTargetGroup).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            {
                if (field.IsDefined(typeof(ObsoleteAttribute), false)) continue;
                var group = (BuildTargetGroup)field.GetValue(null);
                if (group == BuildTargetGroup.Unknown) continue;

                NamedBuildTarget target;
                try
                {
                    target = NamedBuildTarget.FromBuildTargetGroup(group);
                }
                catch (Exception)
                {
                    continue;
                }
                if (seen.Add(target.TargetName)) yield return target;
            }

            if (seen.Add(NamedBuildTarget.Server.TargetName)) yield return NamedBuildTarget.Server;
        }

        private static string[] GetDefines(BuildTargetGroup group)
        {
            try
            {
                PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.FromBuildTargetGroup(group), out var defines);
                return defines;
            }
            catch (Exception)
            {
                return Array.Empty<string>();
            }
        }

        private static void AddDefines(IEnumerable<string> symbols) => ModifyDefines(symbols.ToArray(), add: true);

        private static void RemoveDefines(IEnumerable<string> symbols) => ModifyDefines(symbols.ToArray(), add: false);

        /// <summary>すべてのビルドターゲットの Scripting Define Symbols に対してシンボルを追加・削除する。既存のシンボルの並びは保つ。</summary>
        private static void ModifyDefines(string[] symbols, bool add)
        {
            var changed = new List<string>();
            foreach (var target in AllNamedBuildTargets())
            {
                try
                {
                    PlayerSettings.GetScriptingDefineSymbols(target, out var current);
                    var list = current.ToList();
                    var before = list.Count;
                    if (add) list.AddRange(symbols.Where(s => !list.Contains(s)));
                    else list.RemoveAll(symbols.Contains);
                    if (list.Count == before) continue;

                    PlayerSettings.SetScriptingDefineSymbols(target, list.ToArray());
                    changed.Add(target.TargetName);
                }
                catch (Exception e)
                {
                    // 一部のターゲットで失敗しても残りは続ける (サポートモジュールが無いターゲットなど)
                    Debug.LogWarning($"[Abubu] {target.TargetName} の Scripting Define Symbols を変更できませんでした: {e.Message}");
                }
            }

            if (changed.Count == 0) return;
            Debug.Log($"[Abubu] Scripting Define Symbols を{(add ? "追加" : "削除")}しました: {string.Join(", ", symbols)}\n" +
                      $"対象: {string.Join(", ", changed)}");
        }
    }
}
