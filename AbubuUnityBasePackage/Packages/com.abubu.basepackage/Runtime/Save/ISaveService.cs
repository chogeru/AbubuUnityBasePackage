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
        void Save<T>(string key, T data);
        bool TryLoad<T>(string key, out T data);
        T Load<T>(string key, T defaultValue = default);
        bool Exists(string key);
        void Delete(string key);
    }

    /// <summary>保存先。File / PlayerPrefs / サーバーなど</summary>
    public interface ISaveStorage
    {
        bool TryRead(string key, out string text);
        void Write(string key, string text);
        bool Exists(string key);
        void Delete(string key);
    }

    public interface ISaveSerializer
    {
        string Serialize<T>(T data);
        T Deserialize<T>(string text);
    }

    /// <summary>
    /// セーブデータの現在のバージョン。形式を変えたら数値を上げ、ISaveMigration で旧形式からの変換を書く。
    /// 付けない場合はバージョン 1 として扱う。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public sealed class SaveVersionAttribute : Attribute
    {
        public int Version { get; }
        public SaveVersionAttribute(int version) => Version = version;
    }

    /// <summary>
    /// 旧バージョンのセーブデータ (シリアライズ済み文字列) を 1 つ新しいバージョンに変換する。
    /// Installer で Container.Bind&lt;ISaveMigration&gt;().To&lt;MyMigration&gt;().AsSingle() のように登録する。
    /// </summary>
    public interface ISaveMigration
    {
        Type DataType { get; }
        /// <summary>このバージョンのデータを FromVersion + 1 に変換する</summary>
        int FromVersion { get; }
        string Migrate(string serialized);
    }

    public abstract class SaveMigration<T> : ISaveMigration
    {
        public Type DataType => typeof(T);
        public abstract int FromVersion { get; }
        public abstract string Migrate(string serialized);
    }
}
