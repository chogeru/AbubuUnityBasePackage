using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Abubu.Setup
{
    /// <summary>
    /// 依存パッケージ (UniTask / R3 / Zenject / uPools / LitMotion など) を一括でインストールする。
    /// このアセンブリは他パッケージに依存しないため、依存が未導入の状態でもコンパイルされて動作する。
    /// </summary>
    [InitializeOnLoad]
    public static class AbubuDependencyInstaller
    {
        /// <summary>Abubu.Runtime の動作に必須</summary>
        private static readonly (string name, string url)[] Required =
        {
            ("com.cysharp.unitask", "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask#2.5.11"),
            ("com.github-glitchenzo.nugetforunity", "https://github.com/GlitchEnzo/NuGetForUnity.git?path=/src/NuGetForUnity#v4.5.0"),
            ("com.cysharp.r3", "https://github.com/Cysharp/R3.git?path=src/R3.Unity/Assets/R3.Unity#1.3.1"),
            ("com.svermeulen.extenject", "https://github.com/modesttree/Zenject.git?path=UnityProject/Assets/Plugins/Zenject#31f06cf81043f04301b1072ec81922b64149ea34"),
            ("com.annulusgames.u-pools", "https://github.com/AnnulusGames/uPools.git?path=/Assets/uPools#v1.0.1"),
            ("com.annulusgames.lit-motion", "https://github.com/annulusgames/LitMotion.git?path=src/LitMotion/Assets/LitMotion#v2.0.2"),
        };

        /// <summary>Base Package としてあると便利なもの (Install All で一緒に入る)</summary>
        private static readonly (string name, string url)[] Optional =
        {
            ("com.cysharp.messagepipe", "https://github.com/Cysharp/MessagePipe.git?path=src/MessagePipe.Unity/Assets/Plugins/MessagePipe#1.8.1"),
            ("com.cysharp.messagepipe.zenject", "https://github.com/Cysharp/MessagePipe.git?path=src/MessagePipe.Unity/Assets/Plugins/MessagePipe.Zenject#1.8.1"),
            ("com.tanitaka-tech.state-variable", "https://github.com/tanitaka-tech/StateVariable.git#v1.4.3"),
            ("com.tanitaka-tech.unity-process-manager", "https://github.com/tanitaka-tech/UnityProcessManager.git#v1.1.2"),
            ("net.tnrd.serializableinterface", "https://github.com/Thundernerd/Unity3D-SerializableInterface.git#v2.3.0"),
            ("com.unity.addressables", "2.10.3"),
            ("jp.co.cyberagent.smartaddresser", "https://github.com/CyberAgentGameEntertainment/SmartAddresser.git?path=/Assets/SmartAddresser#1.2.0"),
            ("com.annulusgames.lucid-random", "https://github.com/AnnulusGames/LucidRandom.git?path=/Assets/LucidRandom#v1.1.0"),
            ("com.annulusgames.tween-playables", "https://github.com/AnnulusGames/TweenPlayables.git?path=/Assets/TweenPlayables#v0.9.1"),
            ("com.annulusgames.scene-system", "https://github.com/AnnulusGames/SceneSystem.git?path=/Assets/SceneSystem#v1.0.0"),
        };

        /// <summary>
        /// R3 本体は NuGet 配布のため NuGetForUnity の packages.config に書き込む。
        /// NuGetForUnity v4 は依存を自動解決しないため、依存 DLL もすべて列挙している。
        /// ObservableCollections は StateVariable が使用する。
        /// </summary>
        private static readonly (string id, string version, bool manual)[] NuGetPackages =
        {
            ("Microsoft.Bcl.AsyncInterfaces", "8.0.0", false),
            ("Microsoft.Bcl.TimeProvider", "8.0.0", false),
            ("ObservableCollections", "3.3.4", false),
            ("ObservableCollections.R3", "3.3.4", true),
            ("R3", "1.3.1", true),
            ("System.ComponentModel.Annotations", "5.0.0", false),
            ("System.Runtime.CompilerServices.Unsafe", "6.0.0", false),
            ("System.Threading.Channels", "8.0.0", false),
        };

        private const string ManifestPath = "Packages/manifest.json";
        private const string PackagesConfigPath = "Assets/packages.config";
        private const string PromptedKey = "Abubu.DependencyPrompted";

        static AbubuDependencyInstaller()
        {
            EditorApplication.delayCall += () =>
            {
                if (SessionState.GetBool(PromptedKey, false)) return;
                SessionState.SetBool(PromptedKey, true);

                var missing = GetMissing(Required);
                if (missing.Length == 0 && HasAllNuGetPackages()) return;

                var lines = missing.Select(m => "・" + m.name).ToList();
                if (!HasAllNuGetPackages()) lines.Add("・R3 本体などの NuGet パッケージ (Assets/packages.config)");
                var names = string.Join("\n", lines);
                if (EditorUtility.DisplayDialog("Abubu Base Package",
                        $"必須の依存パッケージが不足しています。\n{names}\n\nインストールしますか?", "インストール", "あとで"))
                {
                    InstallRequired();
                }
            };
        }

        [MenuItem("Tools/Abubu/Install Dependencies/Required", priority = 100)]
        public static void InstallRequired() => Install(Required);

        [MenuItem("Tools/Abubu/Install Dependencies/All (Required + Optional)", priority = 101)]
        public static void InstallAll() => Install(Required.Concat(Optional).ToArray());

        private static void Install((string name, string url)[] packages)
        {
            WritePackagesConfig();

            var missing = GetMissing(packages);
            if (missing.Length == 0)
            {
                Debug.Log("[Abubu] 依存パッケージはすべて導入済みです。");
                AssetDatabase.Refresh();
                return;
            }

            // manifest.json を直接書き換えると一括で解決されるため、Client.Add を個別に呼ぶより速く確実
            var manifest = File.ReadAllText(ManifestPath);
            var insert = string.Join("", missing.Select(m => $"\n    \"{m.name}\": \"{m.url}\","));
            manifest = Regex.Replace(manifest, "\"dependencies\"\\s*:\\s*\\{", m => m.Value + insert);
            File.WriteAllText(ManifestPath, manifest);

            Debug.Log("[Abubu] 依存パッケージを追加しました:\n" + string.Join("\n", missing.Select(m => m.name)));
            Client.Resolve();
            AssetDatabase.Refresh();
        }

        private static (string name, string url)[] GetMissing((string name, string url)[] packages)
        {
            var manifest = File.Exists(ManifestPath) ? File.ReadAllText(ManifestPath) : string.Empty;
            return packages.Where(p => !manifest.Contains($"\"{p.name}\"")).ToArray();
        }

        private static bool HasAllNuGetPackages()
        {
            if (!File.Exists(PackagesConfigPath)) return false;
            var config = File.ReadAllText(PackagesConfigPath);
            return NuGetPackages.All(p => config.Contains($"id=\"{p.id}\""));
        }

        private static void WritePackagesConfig()
        {
            var lines = new List<string>();
            if (File.Exists(PackagesConfigPath))
            {
                var config = File.ReadAllText(PackagesConfigPath);
                lines.AddRange(Regex.Matches(config, "<package [^>]*/>").Select(m => m.Value));
            }

            foreach (var (id, version, manual) in NuGetPackages)
            {
                if (lines.Any(l => l.Contains($"id=\"{id}\""))) continue;
                lines.Add($"<package id=\"{id}\" version=\"{version}\"{(manual ? " manuallyInstalled=\"true\"" : "")} />");
            }

            var body = string.Join("\n", lines.OrderBy(l => l).Select(l => "  " + l));
            File.WriteAllText(PackagesConfigPath, $"<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<packages>\n{body}\n</packages>\n");
        }
    }
}
