using System;
using System.Collections.Generic;
using R3;
using UnityEngine;

namespace Abubu.Inputs
{
    /// <summary>よく使う振動の強さ。中身は <see cref="HapticsSettings.Presets"/> の同名の項目で変えられる</summary>
    public enum HapticPreset
    {
        Light,
        Medium,
        Heavy,
    }

    /// <summary>
    /// ゲームパッドの振動の窓口。ゲームパッドが無い・振動オフ・ポーズ中は何もしない。
    /// <code>
    /// GameInput.Rumble(HapticPreset.Light);              // static で一行
    /// _haptics.Play(0.2f, 0.8f, 0.15f);                  // 低周波 / 高周波モーター (0〜1) と秒数
    /// _haptics.Play("Explosion");                        // HapticsSettings.Presets に追加した名前
    /// _haptics.SetIntensity(0.5f);                       // オプション画面から (保存される)
    /// vibrationToggle.gameObject.SetActive(_haptics.SupportsRumble);   // 振動できないゲームパッドでは設定を隠す
    /// </code>
    /// </summary>
    public interface IHapticsService
    {
        /// <summary>振動が有効か (オプション設定。保存される)</summary>
        ReadOnlyReactiveProperty<bool> Enabled { get; }

        /// <summary>全体の強さ 0〜1 (オプション設定。保存される)</summary>
        ReadOnlyReactiveProperty<float> Intensity { get; }

        /// <summary>今、振動を出せるか (振動できるゲームパッドがある・有効・ポーズ中でない)</summary>
        bool CanPlay { get; }

        /// <summary>
        /// 出力先のデバイスが振動に対応しているか (Switch Pro コントローラーの HID 接続・WebGL・汎用 HID などは false)。
        /// オプション画面で振動の設定を出すかどうかに使う
        /// </summary>
        bool SupportsRumble { get; }

        /// <summary>振動の有効/無効を設定する。無効にすると鳴っている振動も止める</summary>
        void SetEnabled(bool enabled);

        /// <summary>全体の強さ (0〜1 に丸める) を設定する</summary>
        void SetIntensity(float intensity);

        /// <summary>
        /// 振動させる。複数同時に鳴らした場合は、モーターごとに一番強いものが使われる
        /// </summary>
        /// <param name="lowFrequency">低周波 (左・重い) モーターの強さ 0〜1</param>
        /// <param name="highFrequency">高周波 (右・軽い) モーターの強さ 0〜1</param>
        /// <param name="duration">秒数</param>
        void Play(float lowFrequency, float highFrequency, float duration);

        /// <summary>プリセットで振動させる</summary>
        void Play(HapticPreset preset);

        /// <summary><see cref="HapticsSettings.Presets"/> の名前で振動させる。見つからなければ false</summary>
        bool Play(string presetName);

        /// <summary>鳴っている振動をすべて止める</summary>
        void Stop();
    }

    /// <summary>
    /// 振動の出力先。既定はゲームパッドに出す <see cref="GamepadRumbleBackend"/>。
    /// テストや、独自 SDK (Steamworks など) で振動させる場合に差し替える。
    /// <code>
    /// Container.Bind&lt;IRumbleBackend&gt;().To&lt;MyRumbleBackend&gt;().AsSingle();
    /// </code>
    /// </summary>
    public interface IRumbleBackend
    {
        /// <summary>振動を出せるか (出力先のデバイスがあり、振動に対応している)</summary>
        bool IsAvailable { get; }

        /// <summary>出力先のデバイスが振動に対応しているか (<see cref="GamepadRumbleSupport"/>)</summary>
        bool SupportsRumble { get; }

        /// <summary>モーターの強さ (0〜1) を設定する。0, 0 で止まる</summary>
        void SetMotorSpeeds(float lowFrequency, float highFrequency);
    }

    /// <summary>振動の設定 1 つ分</summary>
    [Serializable]
    public sealed class HapticPresetDefinition
    {
        [Tooltip("Play(string) で指定する名前。HapticPreset の Light / Medium / Heavy と同名にすると、その中身を変えられる")]
        public string Name;
        [Range(0f, 1f)] public float LowFrequency;
        [Range(0f, 1f)] public float HighFrequency;
        [Min(0f)] public float Duration;

        public HapticPresetDefinition() { }

        public HapticPresetDefinition(string name, float lowFrequency, float highFrequency, float duration)
        {
            Name = name;
            LowFrequency = lowFrequency;
            HighFrequency = highFrequency;
            Duration = duration;
        }
    }

    /// <summary>振動の設定 (<see cref="AbubuInputSettings.Haptics"/>)</summary>
    [Serializable]
    public sealed class HapticsSettings
    {
        /// <summary>有効/無効・強さを保存するキーの既定値</summary>
        public const string DefaultSaveKey = "abubu.haptics";

        [Tooltip("初期状態で振動を有効にする (保存された設定があればそちらが優先)")]
        public bool EnabledByDefault = true;

        [Tooltip("初期状態の全体の強さ (保存された設定があればそちらが優先)")]
        [Range(0f, 1f)]
        public float DefaultIntensity = 1f;

        [Tooltip("有効/無効・強さを ISaveService に保存するキー。空なら保存しない")]
        public string SaveKey = DefaultSaveKey;

        [Tooltip("ポーズ中は振動を止め、新しい振動も鳴らさない")]
        public bool StopOnPause = true;

        [Tooltip("アプリがフォーカスを失ったら振動を止める (ゲームパッドが鳴りっぱなしにならないように)")]
        public bool StopOnFocusLost = true;

        [Tooltip("振動の秒数を Time.timeScale の影響を受けない時間で数える (ヒットストップ中でも決まった時間で止まる)")]
        public bool IgnoreTimeScale = true;

        [Tooltip("キーボード&マウスを使用中は振動させない (ゲームパッドを繋いだままキーボードで遊んでいる場合)")]
        public bool OnlyWhenUsingGamepad = true;

        [Tooltip("Play(HapticPreset) / Play(string) で使う振動の一覧")]
        public List<HapticPresetDefinition> Presets = new()
        {
            new HapticPresetDefinition(nameof(HapticPreset.Light), 0.1f, 0.3f, 0.08f),
            new HapticPresetDefinition(nameof(HapticPreset.Medium), 0.4f, 0.5f, 0.15f),
            new HapticPresetDefinition(nameof(HapticPreset.Heavy), 0.9f, 0.8f, 0.3f),
        };

        /// <summary>名前でプリセットを探す (大文字小文字を区別しない)。見つからなければ null</summary>
        public HapticPresetDefinition FindPreset(string name)
        {
            if (string.IsNullOrEmpty(name) || Presets == null) return null;
            foreach (var preset in Presets)
            {
                if (preset != null && string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase)) return preset;
            }
            return null;
        }
    }
}
