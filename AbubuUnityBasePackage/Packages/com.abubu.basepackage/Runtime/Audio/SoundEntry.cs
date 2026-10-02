using System;
using UnityEngine;

namespace Abubu.Audio
{
    /// <summary>
    /// サウンドの再生カテゴリ。カテゴリごとに音量を個別に調整できる。
    /// </summary>
    public enum SoundCategory
    {
        Bgm,
        /// <summary>効果音 / VFX に付随する音</summary>
        Se,
        /// <summary>環境音 (風、川、街の雑踏など。複数レイヤー同時再生可)</summary>
        Ambient,
        /// <summary>キャラクターのセリフ等。再生中のボイスは新しいボイスで置き換わる (重ならない)</summary>
        Voice,
    }

    /// <summary>
    /// SoundLibrary に登録する1サウンド分の定義。
    /// Clips を複数登録すると再生のたびにランダムで1つ選ばれる (足音などのバリエーション用。AvoidRepeat で連続を避ける)。
    /// </summary>
    [Serializable]
    public sealed class SoundEntry
    {
        [Tooltip("再生時に指定するキー (例: \"jump\", \"title_bgm\")")]
        public string Key;

        public AudioClip[] Clips = Array.Empty<AudioClip>();

        [Range(0f, 1f)] public float Volume = 1f;

        [Tooltip("x〜y の範囲でランダムにピッチが決まる。変化させないなら (1,1)")]
        public Vector2 PitchRange = Vector2.one;

        [Tooltip("SE の連打防止。この秒数以内の同一キー再生は無視される")]
        [Min(0f)] public float Cooldown;

        [Tooltip("0 = 2D, 1 = 3D。位置指定で SE を鳴らすときのみ有効")]
        [Range(0f, 1f)] public float SpatialBlend;

        [Tooltip("SE の同時発音数が上限のとき、優先度の低い音から止められる (大きいほど優先)。新しい音が全ての再生中の音より低ければ鳴らさない")]
        public int Priority;

        [Tooltip("Clips が複数のとき、直前と同じ Clip を続けて選ばない")]
        public bool AvoidRepeat = true;

        [NonSerialized] private int _lastIndex = -1;

        public bool HasClip => Clips != null && Clips.Length > 0;

        public AudioClip PickClip()
        {
            if (!HasClip) return null;
            if (Clips.Length == 1) return Clips[0];

            int index;
            if (AvoidRepeat && _lastIndex >= 0 && _lastIndex < Clips.Length)
            {
                // 直前の 1 つを除いた中から選ぶ
                index = UnityEngine.Random.Range(0, Clips.Length - 1);
                if (index >= _lastIndex) index++;
            }
            else
            {
                index = UnityEngine.Random.Range(0, Clips.Length);
            }
            _lastIndex = index;
            return Clips[index];
        }

        public float PickPitch() => UnityEngine.Random.Range(PitchRange.x, PitchRange.y);

        public static SoundEntry FromClip(AudioClip clip, float volume = 1f) => new()
        {
            // SoundLibrary のキーと衝突しないよう接頭辞を付ける
            Key = clip != null ? "clip:" + clip.GetInstanceID() : string.Empty,
            Clips = new[] { clip },
            Volume = volume,
        };
    }
}
