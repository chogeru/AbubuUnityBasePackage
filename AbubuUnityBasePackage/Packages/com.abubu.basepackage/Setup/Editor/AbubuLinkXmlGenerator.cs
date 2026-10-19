using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Compilation;
using UnityEditor.UnityLinker;
using UnityEngine;
#if UNITY_6000_5_OR_NEWER
using UnityEngine.Assemblies;
#endif

namespace Abubu.Setup
{
    /// <summary>
    /// IL2CPP の Managed Stripping で、リフレクションでしか使われない Abubu のモジュール Installer が削除されないよう、
    /// ビルド時に link.xml を生成して UnityLinker に渡す。
    /// </summary>
    /// <remarks>
    /// AbubuInstaller は [assembly: AbubuModuleInstaller(typeof(X))] をリフレクションで探して Activator.CreateInstance するため、
    /// Abubu.MessagePipe / Abubu.Addressables などはどこからも静的に参照されない。
    /// 各アセンブリには [assembly: AlwaysLinkAssembly] と [Preserve] も付けているが、念のためここでも保持を指示する。
    /// UPM パッケージ内に置いた link.xml は UnityLinker に読まれないため、IUnityLinkerProcessor で渡している。
    /// 型は名前で照合するので、このアセンブリは Abubu の他のアセンブリに依存しない (依存が未導入でもコンパイルできる)。
    /// </remarks>
    internal sealed class AbubuLinkXmlGenerator : IUnityLinkerProcessor
    {
        private const string ModuleAttributeName = "Abubu.AbubuModuleInstallerAttribute";

        public int callbackOrder => 0;

        public string GenerateAdditionalLinkXmlFile(BuildReport report, UnityLinkerBuildPipelineData data)
        {
            // プレイヤーに含まれる Abubu のアセンブリだけを対象にする (defineConstraints で外れたものは書かない)
            var playerAssemblies = new HashSet<string>(
                CompilationPipeline.GetAssemblies(AssembliesType.PlayerWithoutTestAssemblies).Select(a => a.name));

            var preserve = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
#if UNITY_6000_5_OR_NEWER
            var loaded = CurrentAssemblies.GetLoadedAssemblies();
#else
            var loaded = AppDomain.CurrentDomain.GetAssemblies();
#endif
            foreach (var assembly in loaded)
            {
                string name;
                IList<CustomAttributeData> attributes;
                try
                {
                    name = assembly.GetName().Name;
                    if (!name.StartsWith("Abubu.", StringComparison.Ordinal) || !playerAssemblies.Contains(name)) continue;
                    attributes = assembly.GetCustomAttributesData();
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (var attribute in attributes)
                {
                    if (attribute.AttributeType.FullName != ModuleAttributeName) continue;
                    if (attribute.ConstructorArguments.Count == 0 || attribute.ConstructorArguments[0].Value is not Type installer) continue;

                    if (!preserve.TryGetValue(name, out var types)) preserve[name] = types = new SortedSet<string>(StringComparer.Ordinal);
                    types.Add(installer.FullName);
                }
            }

            if (preserve.Count == 0) return null;

            var sb = new StringBuilder();
            sb.AppendLine("<linker>");
            foreach (var pair in preserve)
            {
                sb.AppendLine($"  <assembly fullname=\"{Escape(pair.Key)}\">");
                foreach (var type in pair.Value)
                {
                    sb.AppendLine($"    <type fullname=\"{Escape(type)}\" preserve=\"all\" />");
                }
                sb.AppendLine("  </assembly>");
            }
            sb.AppendLine("</linker>");

            var directory = Path.Combine(Application.dataPath, "..", "Library", "Abubu");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"{data.target}Link.xml");
            File.WriteAllText(path, sb.ToString());
            return path;
        }

        // Unity のバージョンによってはインターフェイスのメンバーとして要求されるため、空で実装しておく
        public void OnBeforeRun(BuildReport report, UnityLinkerBuildPipelineData data)
        {
        }

        public void OnAfterRun(BuildReport report, UnityLinkerBuildPipelineData data)
        {
        }

        private static string Escape(string value) =>
            value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }
}
