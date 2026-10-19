using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Abubu.UI
{
    /// <summary>Text と TextMeshPro のどちらにも文字列を設定できるようにする (プレハブ側でどちらを使ってもよいように)</summary>
    internal static class UiText
    {
        public static void Set(Graphic target, string text)
        {
            switch (target)
            {
                case TMP_Text tmp:
                    tmp.text = text ?? string.Empty;
                    break;
                case Text legacy:
                    legacy.text = text ?? string.Empty;
                    break;
            }
        }

        /// <summary>文字列が空なら表示自体を消す (タイトル無しのダイアログなど)</summary>
        public static void SetOrHide(Graphic target, string text)
        {
            if (target == null) return;
            Set(target, text);
            target.gameObject.SetActive(!string.IsNullOrEmpty(text));
        }
    }
}
