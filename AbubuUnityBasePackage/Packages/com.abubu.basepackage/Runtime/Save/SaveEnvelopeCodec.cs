using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Abubu.Save
{
    /// <summary>保存時にデータを包む封筒 (保存形式そのもの)。バージョンと型名を持ち、ロード時のマイグレーション判定に使う</summary>
    /// <remarks>フィールド名がそのまま JSON の項目名になるため、名前を変えないこと (以前のデータが読めなくなる)。</remarks>
    [Serializable]
    internal sealed class SaveEnvelope
    {
        /// <summary>
        /// Abubu のセーブデータであることを示す目印 (<see cref="SaveService.FormatMarker"/>)。
        /// 目印の無い旧形式のデータも読めるよう、空の場合は data があれば Abubu のデータとみなす
        /// </summary>
        public string format;
        /// <summary>保存時のデータ形式バージョン (<see cref="SaveVersionAttribute"/>)</summary>
        public int version;
        /// <summary>保存したデータ型の完全名。読み込み時の型違いの警告に使う</summary>
        public string type;
        /// <summary>保存日時 (UTC, ISO 8601)。デバッグ用</summary>
        public string savedAt;
        /// <summary>
        /// data に適用した <see cref="ISaveTransform.Id"/> (適用順)。空なら data はシリアライズ結果そのまま、
        /// あれば変換結果の Base64。この項目が無かった頃のデータは変換なしとして読む
        /// </summary>
        public string[] transforms;
        /// <summary>ISaveSerializer で文字列化したユーザーデータ本体</summary>
        public string data;
    }

    /// <summary>シリアライズ済みのデータと、Envelope に書く情報</summary>
    internal readonly struct SavePayload
    {
        public readonly string Data;
        public readonly int Version;
        public readonly string TypeName;

        public SavePayload(string data, int version, string typeName)
        {
            Data = data;
            Version = version;
            TypeName = typeName;
        }
    }

    /// <summary>圧縮・暗号化の復元に失敗した (理由を TransformFailed にするための目印)</summary>
    internal sealed class SaveTransformException : Exception
    {
        /// <summary>
        /// 必要な変換が登録されていなかった (鍵の渡し忘れなど設定の問題で、データ自体は壊れていない)。
        /// false なら変換の復元そのものに失敗した (鍵の違い・改ざん・データの破損)
        /// </summary>
        public bool IsMissingTransform { get; }

        public SaveTransformException(string message, Exception inner = null, bool isMissingTransform = false) : base(message, inner)
        {
            IsMissingTransform = isMissingTransform;
        }
    }

    /// <summary>
    /// 保存形式 (Envelope) の組み立てと解析、圧縮・暗号化 (<see cref="ISaveTransform"/>) の適用と復元。
    /// 状態を変えないので、どのスレッドから呼んでもよい (登録された変換がスレッドセーフである前提)。
    /// </summary>
    /// <remarks>
    /// 保存時は登録された変換だけを登録順に適用する。読み込み時は Envelope に記録された Id から変換を引く。
    /// 引く表には登録された変換に加えて、鍵の要らない組み込みの変換 (<see cref="GZipSaveTransform"/>) を常に入れておく。
    /// そのため <see cref="SaveSettings.Compress"/> を後から無効にしても、圧縮して保存された以前のデータはそのまま読める。
    /// 暗号化 (<see cref="AesSaveTransform"/>) は鍵が要るため組み込みにはできず、登録されていなければ読み込みは失敗する
    /// (<see cref="SaveFailureReason.TransformFailed"/>。データは壊れていないので退避・復旧はしない)。
    /// </remarks>
    internal sealed class SaveEnvelopeCodec
    {
        /// <summary>保存時に適用する変換 (適用順)</summary>
        private readonly ISaveTransform[] _writeTransforms;

        /// <summary>読み込み時に Id から変換を引く表 (登録された変換 + 組み込みの変換)</summary>
        private readonly Dictionary<string, ISaveTransform> _readTransforms;

        /// <param name="transforms">保存時に適用する変換 (この順に適用する。null なら変換なし)</param>
        /// <exception cref="ArgumentException">Id が重複している</exception>
        public SaveEnvelopeCodec(IEnumerable<ISaveTransform> transforms)
        {
            _writeTransforms = (transforms ?? Enumerable.Empty<ISaveTransform>()).Where(t => t != null).ToArray();
            _readTransforms = _writeTransforms.ToDictionary(t => t.Id, StringComparer.Ordinal);
            foreach (var builtIn in CreateBuiltInReadTransforms())
            {
                // 同じ Id が登録されていればそちらを優先する
                if (!_readTransforms.ContainsKey(builtIn.Id)) _readTransforms.Add(builtIn.Id, builtIn);
            }
        }

        /// <summary>登録されていなくても読み込みには使う、鍵の要らない組み込みの変換</summary>
        private static IEnumerable<ISaveTransform> CreateBuiltInReadTransforms()
        {
            yield return new GZipSaveTransform();
        }

        /// <summary>Envelope の JSON を作る。変換が登録されていれば適用して Base64 にする</summary>
        /// <exception cref="Exception">変換が失敗した (呼び出し側で TransformFailed として扱う)</exception>
        public string Encode(SavePayload payload)
        {
            var envelope = new SaveEnvelope
            {
                format = SaveService.FormatMarker,
                version = payload.Version,
                type = payload.TypeName,
                savedAt = DateTime.UtcNow.ToString("o"),
                data = payload.Data,
            };

            if (_writeTransforms.Length > 0)
            {
                var bytes = Encoding.UTF8.GetBytes(payload.Data ?? "");
                var ids = new string[_writeTransforms.Length];
                for (var i = 0; i < _writeTransforms.Length; i++)
                {
                    var transform = _writeTransforms[i];
                    bytes = transform.Apply(bytes) ?? throw new InvalidOperationException($"変換 \"{transform.Id}\" が null を返しました");
                    ids[i] = transform.Id;
                }
                envelope.data = Convert.ToBase64String(bytes);
                envelope.transforms = ids;
            }

            return JsonUtility.ToJson(envelope);
        }

        /// <summary>保存されていた文字列を Envelope として解析する。Abubu のデータでなければ FormatException</summary>
        public static SaveEnvelope Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) throw new FormatException("データが空です");
            var envelope = JsonUtility.FromJson<SaveEnvelope>(text);
            if (envelope == null) throw new FormatException("データが空です");
            if (!string.IsNullOrEmpty(envelope.format) ? envelope.format != SaveService.FormatMarker : envelope.data == null)
            {
                throw new FormatException("Abubu のセーブデータの形式ではありません (同じフォルダに別のデータが置かれていませんか?)");
            }
            return envelope;
        }

        /// <summary>
        /// 保存されていたデータの見出し (バージョンと、読み込みに必要な変換) だけを調べる。データ本体の変換の復元はしない
        /// (上書きしてよいかの確認用)。読めない・壊れている場合は false (壊れたデータの判定は読み込み側の役目)
        /// </summary>
        /// <param name="missingTransform">読み込みに必要なのに登録されていない変換の Id (揃っていれば null)</param>
        public bool TryPeekHeader(string text, out int version, out string missingTransform)
        {
            version = 0;
            missingTransform = null;
            if (string.IsNullOrEmpty(text)) return false;
            try
            {
                var envelope = JsonUtility.FromJson<SaveEnvelope>(text);
                if (envelope == null) return false;
                version = envelope.version;
                missingTransform = FindMissingTransform(envelope);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>読み込みに必要なのに登録されていない変換の Id。揃っていれば null</summary>
        public string FindMissingTransform(SaveEnvelope envelope)
        {
            if (envelope.transforms == null) return null;
            foreach (var id in envelope.transforms)
            {
                if (!_readTransforms.ContainsKey(id ?? "")) return id ?? "";
            }
            return null;
        }

        /// <summary>Envelope に記録された変換を逆順に復元し、シリアライズ済みの文字列を取り出す</summary>
        /// <exception cref="SaveTransformException">変換が登録されていない・復元に失敗した</exception>
        public string Unwrap(SaveEnvelope envelope)
        {
            var ids = envelope.transforms;
            if (ids == null || ids.Length == 0) return envelope.data;

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(envelope.data ?? "");
            }
            catch (Exception e)
            {
                throw new SaveTransformException("変換済みデータが Base64 ではありません", e);
            }

            // 復元を始める前に、必要な変換がすべて揃っているか確かめる (揃っていなければデータの問題ではない)
            var missing = FindMissingTransform(envelope);
            if (missing != null)
            {
                throw new SaveTransformException(
                    $"変換 \"{missing}\" が登録されていないため復元できません (ISaveTransform を登録してください)。" +
                    "データは壊れていないため、そのまま残し、このキーへの Save は拒否します (ForceSave / Delete で解除)",
                    isMissingTransform: true);
            }

            for (var i = ids.Length - 1; i >= 0; i--)
            {
                var transform = _readTransforms[ids[i]];
                try
                {
                    bytes = transform.Revert(bytes) ?? throw new InvalidOperationException("null を返しました");
                }
                catch (Exception e)
                {
                    throw new SaveTransformException($"変換 \"{ids[i]}\" の復元に失敗しました", e);
                }
            }
            return Encoding.UTF8.GetString(bytes);
        }
    }
}
