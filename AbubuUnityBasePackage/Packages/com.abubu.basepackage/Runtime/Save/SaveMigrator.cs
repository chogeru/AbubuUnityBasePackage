using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Abubu.Save
{
    /// <summary>
    /// 旧バージョンのデータ (シリアライズ済み文字列) を、登録された <see cref="ISaveMigration"/> で 1 段ずつ現在のバージョンまで変換する。
    /// 利用者のコードを呼ぶため、メインスレッドから使う。
    /// </summary>
    internal sealed class SaveMigrator
    {
        /// <summary>(データ型, 変換元バージョン) からマイグレーションを引く表</summary>
        private readonly Dictionary<(Type, int), ISaveMigration> _migrations;

        /// <param name="migrations">登録されたマイグレーション (null なら無し)</param>
        /// <exception cref="ArgumentException">同じ (型, バージョン) が重複している</exception>
        public SaveMigrator(IEnumerable<ISaveMigration> migrations)
        {
            _migrations = (migrations ?? Enumerable.Empty<ISaveMigration>())
                .ToDictionary(m => (m.DataType, m.FromVersion));
        }

        /// <summary>
        /// savedVersion から現在のバージョン (<see cref="SaveDataVersion{T}"/>) まで変換する。
        /// savedVersion が 0 (古い形式で Envelope に version が無かった場合など) は v1 として扱う。
        /// マイグレーションが足りなければ log に MigrationMissing を記録して false。マイグレーション自体の例外はそのまま投げる
        /// </summary>
        public bool TryMigrate<T>(string key, string serialized, int savedVersion, SaveOperationLog log, out string migrated)
        {
            migrated = serialized;
            var current = SaveDataVersion<T>.Value;
            for (var v = Math.Max(1, savedVersion); v < current; v++)
            {
                if (!_migrations.TryGetValue((typeof(T), v), out var migration))
                {
                    log.Fail(SaveFailureReason.MigrationMissing, $"{typeof(T).Name} の v{v} → v{v + 1} のマイグレーションが登録されていません (key: {key})");
                    return false;
                }
                migrated = migration.Migrate(migrated);
            }
            return true;
        }
    }

    /// <summary>型に付いた <see cref="SaveVersionAttribute"/> のバージョン (未指定なら 1)。型ごとに 1 回だけリフレクションする</summary>
    internal static class SaveDataVersion<T>
    {
        public static readonly int Value = typeof(T).GetCustomAttribute<SaveVersionAttribute>()?.Version ?? 1;
    }
}
