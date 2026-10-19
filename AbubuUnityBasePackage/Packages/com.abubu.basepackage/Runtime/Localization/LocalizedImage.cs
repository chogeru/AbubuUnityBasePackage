using UnityEngine;
using UnityEngine.UI;

namespace Abubu.Localization
{
    /// <summary>
    /// Image / SpriteRenderer に付けると、Key に対応する言語ごとの Sprite (LocalizationTable の Assets) を表示する。
    /// 文字入りのロゴやボタン画像の差し替えに使う。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Abubu/Localized Image")]
    public sealed class LocalizedImage : LocalizedBehaviour
    {
        [SerializeField] private string key;

        [Tooltip("差し替えたあと Image を画像の元のサイズに合わせる")]
        [SerializeField] private bool setNativeSize;

        public string Key
        {
            get => key;
            set
            {
                key = value;
                Refresh();
            }
        }

        public override void Refresh()
        {
            var service = Service;
            if (service == null || string.IsNullOrEmpty(key)) return;

            var sprite = service.GetAsset<Sprite>(key);
            if (sprite == null) return;

            if (TryGetComponent<Image>(out var image))
            {
                image.sprite = sprite;
                if (setNativeSize) image.SetNativeSize();
            }
            else if (TryGetComponent<SpriteRenderer>(out var spriteRenderer))
            {
                spriteRenderer.sprite = sprite;
            }
        }
    }
}
