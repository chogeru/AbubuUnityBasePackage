using System;

namespace Abubu.Save
{
    /// <summary>
    /// セーブ/ロードの窓口。保存先 (ISaveStorage) とシリアライザ (ISaveSerializer) は差し替え可能。
    /// <code>
    /// _save.Save("progress", progress);
    /// var progress = _save.Load("progress", new ProgressData());
    /// </code>
    /// </summary>
    public interface ISaveService
    {
        /// <summary>
        /// データをシリアライズして保存する。同じキーの既存データは上書きされる。
        /// 保存に失敗しても例外は投げず、エラーログを出すだけ。
        /// </summary>
        void Save<T>(string key, T data);

        /// <summary>
        /// データを読み込む。旧バージョンのデータは登録済みの <see cref="ISaveMigration"/> で最新形式に変換される。
        /// </summary>
        /// <returns>読み込めたら true。データが無い・壊れている・変換できない場合は false</returns>
        bool TryLoad<T>(string key, out T data);

        /// <summary>データを読み込む。読み込めなかった場合は <paramref name="defaultValue"/> を返す。</summary>
        T Load<T>(string key, T defaultValue = default);

        /// <summary>指定キーのデータが保存されているか。</summary>
        bool Exists(string key);

        /// <summary>指定キーのデータを削除する。存在しなくてもエラーにはならない。</summary>
        void Delete(string key);
    }

    /// <summary>保存先。File / PlayerPrefs / サーバーなど</summary>
    /// <remarks>文字列の読み書きだけを担当する。形式やバージョン管理は <see cref="ISaveService"/> 側で扱う。</remarks>
    public interface ISaveStorage
    {
        /// <summary>キーに対応する文字列を読む。無ければ false。</summary>
        bool TryRead(string key, out string text);

        /// <summary>キーに対応する文字列を書き込む (上書き)。</summary>
        void Write(string key, string text);

        /// <summary>キーに対応するデータが存在するか。</summary>
        bool Exists(string key);

        /// <summary>キーに対応するデータを削除する。</summary>
        void Delete(string key);
    }

    /// <summary>
    /// データ ⇔ 文字列の変換方法。既定は JsonUtility だが、Newtonsoft.Json や MessagePack などに差し替えられる。
    /// </summary>
    public interface ISaveSerializer
    {
        /// <summary>データを文字列に変換する。</summary>
        string Serialize<T>(T data);

        /// <summary>文字列からデータを復元する。</summary>
        T Deserialize<T>(string text);
    }

    /// <summary>
    /// セーブデータの現在のバージョン。形式を変えたら数値を上げ、ISaveMigration で旧形式からの変換を書く。
    /// 付けない場合はバージョン 1 として扱う。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public sealed class SaveVersionAttribute : Attribute
    {
        /// <summary>データ形式のバージョン番号 (1 始まり)</summary>
        public int Version { get; }
        public SaveVersionAttribute(int version) => Version = version;
    }

    /// <summary>
    /// 旧バージョンのセーブデータ (シリアライズ済み文字列) を 1 つ新しいバージョンに変換する。
    /// Installer で Container.Bind&lt;ISaveMigration&gt;().To&lt;MyMigration&gt;().AsSingle() のように登録する。
    /// </summary>
    /// <remarks>
    /// v1 → v3 のように複数段階離れている場合は、v1→v2、v2→v3 の順にマイグレーションが連続して適用される。
    /// </remarks>
    public interface ISaveMigration
    {
        /// <summary>変換対象のデータ型</summary>
        Type DataType { get; }

        /// <summary>このバージョンのデータを FromVersion + 1 に変換する</summary>
        int FromVersion { get; }

        /// <summary>
        /// シリアライズ済みの旧データを、1 つ新しいバージョンの形式の文字列に書き換えて返す。
        /// </summary>
        string Migrate(string serialized);
    }

    /// <summary>
    /// <see cref="ISaveMigration"/> を型引数で書くための基底クラス。DataType を自動で埋める。
    /// </summary>
    /// <typeparam name="T">変換対象のデータ型</typeparam>
    public abstract class SaveMigration<T> : ISaveMigration
    {
        public Type DataType => typeof(T);
        public abstract int FromVersion { get; }
        public abstract string Migrate(string serialized);
    }
}
