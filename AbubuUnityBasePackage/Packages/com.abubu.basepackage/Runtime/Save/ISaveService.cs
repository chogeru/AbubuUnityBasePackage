using System;

namespace Abubu.Save
{
    /// <summary>
    /// セーブ/ロードの窓口。保存先 (ISaveStorage) とシリアライザ (ISaveSerializer) は差し替え可能。
    /// <code>
    /// if (!_save.Save("progress", progress)) ShowSaveFailedDialog();
    /// var progress = _save.Load("progress", new ProgressData());
    /// </code>
    /// </summary>
    /// <remarks>
    /// どのメソッドも例外を投げない (失敗はエラーログと戻り値で知らせる)。自動保存の購読内などから安心して呼べる。
    /// <para>
    /// 新しいバージョンのビルドで保存されたデータ (<see cref="SaveVersionAttribute"/> が今より大きい) を古いビルドが見つけた場合:
    /// 読み込みは失敗 (TryLoad が false) し、元データを "キー + .newer" に 1 度だけ複製し、
    /// そのキーへの <see cref="Save{T}"/> はこの実行中は拒否される (false)。
    /// 知らない項目を落として上書きし、新しいビルドのデータを壊さないため。
    /// 上書きしてよい場合は <see cref="ForceSave{T}"/> を使うか、<see cref="Delete"/> で消してから保存する。
    /// </para>
    /// <para>
    /// 読み込みに必要な変換 (<see cref="ISaveTransform"/>。暗号化の鍵など) が登録されていないデータも同じく保護される:
    /// 読み込みは <see cref="SaveFailureReason.TransformFailed"/> で失敗し、データはそのまま残り、
    /// そのキーへの <see cref="Save{T}"/> は (TransformFailed で) 拒否される。変換を登録し直すか、ForceSave / Delete で解除する。
    /// </para>
    /// <para>
    /// 関連する機能: 大きなデータの非同期保存は <see cref="IAsyncSaveService"/> (どの実装にも拡張メソッド SaveAsync / LoadAsync がある)、
    /// セーブスロットは <see cref="ISaveSlotService"/> (この ISaveService はスロットに関係しない共通の領域)、
    /// 圧縮・暗号化は <see cref="ISaveTransform"/>、失敗理由の受け取りは <see cref="ISaveFailureNotifier"/>。
    /// </para>
    /// </remarks>
    public interface ISaveService
    {
        /// <summary>
        /// データをシリアライズして保存する。同じキーの既存データは上書きされる。
        /// 失敗しても例外は投げず、エラーログを出して false を返す。
        /// </summary>
        /// <returns>
        /// 保存先への書き込みまで完了したら true。
        /// シリアライズ失敗・書き込み失敗 (ディスクフル・権限など)・新しいバージョンのデータや読み込めない変換済みデータの保護中は false。
        /// </returns>
        bool Save<T>(string key, T data);

        /// <summary>
        /// 新しいバージョンのデータの保護 (クラスの remarks 参照) を無視して保存する。
        /// プレイヤーが「データを初期化して続ける」を選んだ場合など、上書きしてよいと確定している時だけ使う。
        /// </summary>
        /// <returns><see cref="Save{T}"/> と同じ</returns>
        bool ForceSave<T>(string key, T data);

        /// <summary>
        /// データを読み込む。旧バージョンのデータは登録済みの <see cref="ISaveMigration"/> で最新形式に変換される。
        /// </summary>
        /// <returns>
        /// 読み込めたら true。データが無い・壊れている・変換できない・今より新しいバージョンのデータの場合は false
        /// (無い場合以外はエラーログが出る)
        /// </returns>
        bool TryLoad<T>(string key, out T data);

        /// <summary>データを読み込む。読み込めなかった場合は <paramref name="defaultValue"/> を返す。</summary>
        T Load<T>(string key, T defaultValue = default);

        /// <summary>指定キーのデータが保存されているか。</summary>
        bool Exists(string key);

        /// <summary>
        /// 指定キーのデータを削除する。存在しなくてもエラーにはならない。
        /// 付随するデータ (バックアップ・"キー + .broken"・"キー + .newer") も削除し、新しいバージョンのデータの保護も解除する。
        /// </summary>
        void Delete(string key);
    }

    /// <summary>保存先。File / PlayerPrefs / サーバーなど</summary>
    /// <remarks>
    /// 文字列の読み書きだけを担当する。形式やバージョン管理は <see cref="ISaveService"/> 側で扱う。
    /// 失敗時は例外を投げてよい (SaveService が捕まえてログと戻り値に変換する)。
    /// </remarks>
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
    /// 1 つ前のデータを別に保持している保存先 (任意)。
    /// 実装していると、本体が壊れていたときに <see cref="ISaveService"/> がバックアップから復旧する。
    /// </summary>
    public interface ISaveBackupStorage
    {
        /// <summary>キーに対応する 1 つ前のデータを読む。無ければ false。</summary>
        bool TryReadBackup(string key, out string text);

        /// <summary>
        /// バックアップの内容で本体を置き換える。バックアップ自体は残す (本体を新たなバックアップとして回さない)。
        /// 壊れた本体でバックアップを上書きしてしまわないように、通常の Write ではなくこちらで復旧する。
        /// </summary>
        void RestoreFromBackup(string key);
    }

    /// <summary>
    /// データ ⇔ 文字列の変換方法。既定は JsonUtility だが、Newtonsoft.Json や MessagePack などに差し替えられる。
    /// </summary>
    /// <remarks>変換できない場合は例外を投げてよい (SaveService が捕まえてログと戻り値に変換する)。</remarks>
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
