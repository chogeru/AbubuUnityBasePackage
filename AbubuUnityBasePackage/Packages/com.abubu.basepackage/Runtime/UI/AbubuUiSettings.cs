using System;
using System.Collections.Generic;
using Abubu.Pause;
using R3;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Scripting;
using UnityEngine.UI;
using Zenject;
using Object = UnityEngine.Object;

[assembly: Abubu.AbubuModuleInstaller(typeof(Abubu.UI.UiModuleInstaller))]

namespace Abubu.UI
{
    /// <summary>
    /// 任意。Resources/AbubuUiSettings として置くと読み込まれる。無ければ既定値を使う。
    /// </summary>
    [CreateAssetMenu(menuName = "Abubu/UI Settings", fileName = "AbubuUiSettings")]
    public sealed class AbubuUiSettings : ScriptableObject
    {
        public const string ResourcePath = "AbubuUiSettings";

        [Tooltip("ポップアップが 1 つでも開いている間、IPauseService でポーズする")]
        public bool PauseWhileOpen;

        [Tooltip("ポップアップの背面を覆う色。背後のボタンを押せなくする (alpha 0 でも操作は遮断される)")]
        public Color BackdropColor = new(0f, 0f, 0f, 0.5f);

        [Tooltip("背面をクリックしたら一番上のポップアップを閉じる")]
        public bool CloseOnBackdropClick;

        [Tooltip("ポップアップ用 Canvas の Sorting Order")]
        public int SortingOrder = 100;

        [Tooltip("Canvas Scaler の基準解像度")]
        public Vector2 ReferenceResolution = new(1920f, 1080f);
    }
}
