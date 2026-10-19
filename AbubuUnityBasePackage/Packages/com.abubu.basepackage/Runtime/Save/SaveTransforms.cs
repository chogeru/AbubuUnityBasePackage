using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Abubu.Save
{
    /// <summary>
    /// シリアライズ後のデータに掛ける変換 (圧縮・暗号化など)。既定では何も登録されていない (変換なし)。
    /// 登録した順に適用され、読み込み時は逆順に復元される。
    /// <code>
    /// Container.Bind&lt;ISaveTransform&gt;().To&lt;GZipSaveTransform&gt;().AsSingle();                       // 1. 圧縮
    /// Container.Bind&lt;ISaveTransform&gt;().FromInstance(new AesSaveTransform("my-secret", "my-game")); // 2. 暗号化
    /// </code>
    /// </summary>
    /// <remarks>
    /// 適用した変換の <see cref="Id"/> はデータと一緒に保存されるため、変換を後から追加・変更しても、
    /// 変換なしで保存された古いデータや、別の順番で保存されたデータはそのまま読める
    /// (読み込みに必要な変換が登録されていない場合は失敗する)。
    /// <para>
    /// 例外として <see cref="GZipSaveTransform"/> (Id "gzip") は鍵が要らないため、登録していなくても読み込みには常に使われる
    /// (圧縮を途中で無効にしても以前のデータが読める)。暗号化 (<see cref="AesSaveTransform"/>) が登録されていない場合は
    /// <see cref="SaveFailureReason.TransformFailed"/> で失敗するが、データは壊れていないので .broken への退避やバックアップからの復旧はせず、そのまま残す。
    /// </para>
    /// <para>
    /// 非同期 API (<see cref="IAsyncSaveService"/>) では別スレッドから呼ばれる。Unity の API を使わず、スレッドセーフに実装すること。
    /// 失敗時は例外を投げてよい (SaveService が捕まえて <see cref="SaveFailureReason.TransformFailed"/> にする)。
    /// </para>
    /// </remarks>
    public interface ISaveTransform
    {
        /// <summary>保存データに記録される識別子 ("gzip" など)。一度使い始めたら変えないこと。登録する変換の間で重複不可</summary>
        string Id { get; }

        /// <summary>保存時の変換 (圧縮・暗号化)</summary>
        byte[] Apply(byte[] data);

        /// <summary>読み込み時の復元 (展開・復号)。<see cref="Apply"/> の逆</summary>
        byte[] Revert(byte[] data);
    }

    /// <summary>
    /// GZip で圧縮する。JSON は繰り返しが多いので数分の 1 になることが多い。
    /// <code>
    /// Container.Bind&lt;ISaveTransform&gt;().To&lt;GZipSaveTransform&gt;().AsSingle();
    /// </code>
    /// </summary>
    /// <remarks>
    /// 圧縮結果は Base64 で保存されるため、数百バイト程度の小さなデータでは逆に大きくなることがある。
    /// </remarks>
    public sealed class GZipSaveTransform : ISaveTransform
    {
        /// <summary>保存データに記録される識別子</summary>
        public const string TransformId = "gzip";

        private readonly CompressionLevel _level;

        /// <param name="level">圧縮レベル。Fastest は速度優先</param>
        public GZipSaveTransform(CompressionLevel level = CompressionLevel.Optimal) => _level = level;

        /// <inheritdoc />
        public string Id => TransformId;

        /// <inheritdoc />
        public byte[] Apply(byte[] data)
        {
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, _level, true)) gzip.Write(data, 0, data.Length);
            return output.ToArray();
        }

        /// <inheritdoc />
        public byte[] Revert(byte[] data)
        {
            using var input = new MemoryStream(data);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            gzip.CopyTo(output);
            return output.ToArray();
        }
    }

    /// <summary>
    /// AES-256 (CBC) で暗号化し、HMAC-SHA256 で改ざんを検出する。鍵は利用者が渡す。
    /// <code>
    /// Container.Bind&lt;ISaveTransform&gt;().FromInstance(new AesSaveTransform("パスフレーズ", "ゲームごとの salt"));
    /// </code>
    /// </summary>
    /// <remarks>
    /// 鍵はアプリに埋め込まれるため、解析すれば取り出せる。「セーブファイルを手で書き換えられにくくする」難読化・改ざん検出として使い、
    /// 本当に守るべき秘密 (課金情報など) はサーバー側で管理すること。
    /// 鍵を変えると以前のデータは読めなくなる (<see cref="SaveFailureReason.TransformFailed"/>)。
    /// <para>形式: [形式番号 1 バイト][IV 16 バイト][暗号文][HMAC 32 バイト]</para>
    /// </remarks>
    public sealed class AesSaveTransform : ISaveTransform
    {
        /// <summary>保存データに記録される既定の識別子</summary>
        public const string DefaultId = "aes";

        private const byte FormatVersion = 1;
        private const int IvSize = 16;
        private const int MacSize = 32;
        private const int KeySize = 32;

        /// <summary>パスフレーズから鍵を作るときの PBKDF2 の反復回数 (生成は構築時の 1 回だけ)</summary>
        public const int DefaultIterations = 10000;

        private readonly byte[] _encryptionKey;
        private readonly byte[] _macKey;

        /// <inheritdoc />
        public string Id { get; }

        /// <param name="key">32 バイト以上の鍵。前半 32 バイトを暗号化、そこから HMAC 用の鍵を導出する</param>
        /// <param name="id">保存データに記録する識別子。鍵を切り替える場合は別の識別子にし、古い鍵の変換も登録しておくと移行できる</param>
        public AesSaveTransform(byte[] key, string id = DefaultId)
        {
            if (key == null || key.Length < KeySize) throw new ArgumentException($"鍵は {KeySize} バイト以上必要です", nameof(key));
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("識別子が空です", nameof(id));
            Id = id;
            _encryptionKey = new byte[KeySize];
            Array.Copy(key, _encryptionKey, KeySize);
            using var hmac = new HMACSHA256(key);
            _macKey = hmac.ComputeHash(Encoding.UTF8.GetBytes("abubu.save.mac"));
        }

        /// <param name="passphrase">パスフレーズ</param>
        /// <param name="salt">ゲームごとに変える文字列 (同じパスフレーズでも別の鍵になる)</param>
        /// <param name="id">保存データに記録する識別子</param>
        /// <param name="iterations">PBKDF2 の反復回数</param>
        public AesSaveTransform(string passphrase, string salt, string id = DefaultId, int iterations = DefaultIterations)
            : this(DeriveKey(passphrase, salt, iterations), id)
        {
        }

        private static byte[] DeriveKey(string passphrase, string salt, int iterations)
        {
            if (string.IsNullOrEmpty(passphrase)) throw new ArgumentException("パスフレーズが空です", nameof(passphrase));
            var saltBytes = Encoding.UTF8.GetBytes(string.IsNullOrEmpty(salt) ? "abubu.save" : salt);
            if (saltBytes.Length < 8) Array.Resize(ref saltBytes, 8);
            using var kdf = new Rfc2898DeriveBytes(passphrase, saltBytes, iterations, HashAlgorithmName.SHA256);
            return kdf.GetBytes(KeySize);
        }

        /// <inheritdoc />
        public byte[] Apply(byte[] data)
        {
            using var aes = CreateAes();
            aes.GenerateIV();
            byte[] cipher;
            using (var encryptor = aes.CreateEncryptor()) cipher = encryptor.TransformFinalBlock(data, 0, data.Length);

            var result = new byte[1 + IvSize + cipher.Length + MacSize];
            result[0] = FormatVersion;
            Buffer.BlockCopy(aes.IV, 0, result, 1, IvSize);
            Buffer.BlockCopy(cipher, 0, result, 1 + IvSize, cipher.Length);
            var mac = ComputeMac(result, 1 + IvSize + cipher.Length);
            Buffer.BlockCopy(mac, 0, result, result.Length - MacSize, MacSize);
            return result;
        }

        /// <inheritdoc />
        /// <exception cref="CryptographicException">鍵が違う・改ざんされている・形式が違う</exception>
        public byte[] Revert(byte[] data)
        {
            if (data == null || data.Length < 1 + IvSize + MacSize) throw new CryptographicException("暗号化データが短すぎます");
            if (data[0] != FormatVersion) throw new CryptographicException($"未対応の暗号化形式です ({data[0]})");

            var macOffset = data.Length - MacSize;
            var expected = ComputeMac(data, macOffset);
            var diff = 0;
            for (var i = 0; i < MacSize; i++) diff |= expected[i] ^ data[macOffset + i];
            if (diff != 0) throw new CryptographicException("改ざんされているか、鍵が違います");

            using var aes = CreateAes();
            var iv = new byte[IvSize];
            Buffer.BlockCopy(data, 1, iv, 0, IvSize);
            aes.IV = iv;
            using var decryptor = aes.CreateDecryptor();
            return decryptor.TransformFinalBlock(data, 1 + IvSize, macOffset - 1 - IvSize);
        }

        private Aes CreateAes()
        {
            var aes = Aes.Create();
            aes.KeySize = KeySize * 8;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = _encryptionKey;
            return aes;
        }

        private byte[] ComputeMac(byte[] buffer, int count)
        {
            using var hmac = new HMACSHA256(_macKey);
            return hmac.ComputeHash(buffer, 0, count);
        }
    }
}
