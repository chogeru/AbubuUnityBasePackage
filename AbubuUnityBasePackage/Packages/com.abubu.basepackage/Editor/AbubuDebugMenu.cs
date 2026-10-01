using Abubu.Debugging;
using UnityEditor;
using UnityEngine;

namespace Abubu.Editor
{
    /// <summary>GameObject > Abubu > Debug Overlay : FPS / メモリ表示をシーンに追加する</summary>
    public static class AbubuDebugMenu
    {
        [MenuItem("GameObject/Abubu/Debug Overlay", priority = 30)]
        private static void CreateDebugOverlay(MenuCommand command)
        {
            var existing = Object.FindFirstObjectByType<DebugOverlay>();
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                Debug.Log("[Abubu] DebugOverlay は既にシーンにあります。");
                return;
            }

            var go = new GameObject("DebugOverlay", typeof(DebugOverlay));
            Undo.RegisterCreatedObjectUndo(go, "Create Debug Overlay");
            Selection.activeGameObject = go;
        }
    }
}
