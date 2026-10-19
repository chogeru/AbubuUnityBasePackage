using System.Linq;
using Abubu.Input;
using UnityEditor;
using UnityEngine;

namespace Abubu.Editor
{
    /// <summary>
    /// Setup Project で AbubuInputSettings を作り、AbubuSettings > Module Settings に入れる (Input System 導入時のみ)。
    /// すでに Module Settings か Resources にあれば、それを使う。
    /// </summary>
    internal sealed class InputSettingsSetupStep : IAbubuSetupStep
    {
        public int Order => 0;

        public void Run(AbubuSetupContext context)
        {
            var existing = context.Settings.ModuleSettings.OfType<AbubuInputSettings>().FirstOrDefault();
            if (existing != null) return;

            var settings = AbubuInputSettings.FindDefault();
            if (settings == null)
            {
                var path = context.Folder + "/AbubuInputSettings.asset";
                settings = AssetDatabase.LoadAssetAtPath<AbubuInputSettings>(path);
                if (settings == null)
                {
                    settings = ScriptableObject.CreateInstance<AbubuInputSettings>();
                    AssetDatabase.CreateAsset(settings, path);
                }
            }
            context.AddModuleSettings(settings);
        }
    }
}
