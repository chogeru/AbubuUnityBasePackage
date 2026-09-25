using System;
using UnityEngine;

namespace Abubu.Scene
{
    [Serializable]
    public sealed class SceneSettings
    {
        public Color FadeColor = Color.black;
        [Min(0f)] public float FadeOutDuration = 0.3f;
        [Min(0f)] public float FadeInDuration = 0.3f;

        [Tooltip("ロード画面を最低この秒数は表示する (一瞬だけチラつくのを防ぐ)")]
        [Min(0f)] public float MinimumLoadingTime;

        [Tooltip("任意。ロード中に表示するプレハブ (SceneLoadingView を付ける)。未設定ならフェードのみ")]
        public GameObject LoadingViewPrefab;

        [Tooltip("フェード用 Canvas の sortingOrder")]
        public int SortingOrder = 30000;
    }

    /// <summary>LoadAsync 呼び出しごとに SceneSettings の値を上書きしたい時に使う</summary>
    public sealed class SceneLoadOptions
    {
        public float? FadeOutDuration;
        public float? FadeInDuration;
        public Color? FadeColor;
        public float? MinimumLoadingTime;
        /// <summary>false にするとロード画面 (LoadingView) を出さない</summary>
        public bool ShowLoadingView = true;

        public static SceneLoadOptions Instant => new() { FadeOutDuration = 0f, FadeInDuration = 0f, ShowLoadingView = false };

        /// <summary>未指定の項目を SceneSettings の値で埋めた、確定済みの値を返す (options が null でも可)</summary>
        public static ResolvedSceneLoadOptions Resolve(SceneLoadOptions options, SceneSettings settings) => new(
            options?.FadeOutDuration ?? settings.FadeOutDuration,
            options?.FadeInDuration ?? settings.FadeInDuration,
            options?.FadeColor ?? settings.FadeColor,
            options?.MinimumLoadingTime ?? settings.MinimumLoadingTime,
            options?.ShowLoadingView ?? true);
    }

    /// <summary>SceneLoadOptions と SceneSettings をマージした結果</summary>
    public readonly struct ResolvedSceneLoadOptions
    {
        public readonly float FadeOutDuration;
        public readonly float FadeInDuration;
        public readonly Color FadeColor;
        public readonly float MinimumLoadingTime;
        public readonly bool ShowLoadingView;

        public ResolvedSceneLoadOptions(float fadeOut, float fadeIn, Color fadeColor, float minimumLoadingTime, bool showLoadingView)
        {
            FadeOutDuration = fadeOut;
            FadeInDuration = fadeIn;
            FadeColor = fadeColor;
            MinimumLoadingTime = minimumLoadingTime;
            ShowLoadingView = showLoadingView;
        }
    }
}
