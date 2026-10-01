using System;
using R3;
using UnityEngine;
using UnityEngine.UI;

namespace Abubu.Samples
{
    /// <summary>SampleTitle → SampleGame に渡すデータ</summary>
    [Serializable]
    public sealed class SampleGamePayload
    {
        public string Message;
        public string StartedAt;
    }

    /// <summary>イベントバスの動作確認用メッセージ</summary>
    public readonly struct SampleScored
    {
        public readonly int Total;
        public SampleScored(int total) => Total = total;
    }
}
