using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Abubu.Save
{
    /// <summary>
    /// <see cref="ISaveSlotService"/> の標準実装。任意の <see cref="ISaveService"/> の上に、キーの接頭辞でスロットを作る。
    /// <code>
    /// var slots = new SaveSlotService(save, storage);
    /// slots.Switch("1");
    /// slots.Active.Save("progress", progress);
    /// </code>
    /// </summary>
    /// <remarks>
    /// データの読み書きはすべて渡された <see cref="ISaveService"/> を通すため、バージョン管理・マイグレーション・バックアップ・
    /// 圧縮/暗号化・失敗の通知はスロット内のデータにもそのまま効く。
    /// <see cref="ISaveStorage"/> はスロットのコピー (保存済みの文字列をそのまま複製する) にだけ使う。
    /// </remarks>
    public sealed class SaveSlotService : ISaveSlotService
    {
        /// <summary>共通の領域に保存するスロットの一覧</summary>
        [Serializable]
        private sealed class SaveSlotIndex
        {
            public List<SaveSlotInfo> Slots = new();
            public string LastSelected;
        }

        /// <summary>スロット ID の最大文字数</summary>
        public const int MaxSlotIdLength = 32;

        private readonly ISaveService _root;
        private readonly ISaveStorage _storage;
        private readonly SaveSlotOptions _options;
        private readonly Scope _active;
        private readonly Scope _global;
        private readonly Dictionary<string, Scope> _scopes = new();
        private SaveSlotIndex _index;

        /// <summary>最後に一覧を保存してから AddPlayTime で加算した、まだ保存していない秒数</summary>
        private double _unsavedPlayTime;

        /// <inheritdoc />
        public event Action<string> ActiveSlotChanged;

        /// <param name="root">データの読み書きに使うセーブサービス (共通の領域)</param>
        /// <param name="storage"><paramref name="root"/> が使っている保存先 (スロットのコピーに使う)</param>
        /// <param name="options">設定 (null なら既定値)</param>
        public SaveSlotService(ISaveService root, ISaveStorage storage, SaveSlotOptions options = null)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _options = options ?? new SaveSlotOptions();
            if (string.IsNullOrEmpty(_options.IndexKey)) throw new ArgumentException("SaveSlotOptions.IndexKey が空です", nameof(options));
            if (string.IsNullOrEmpty(_options.KeyPrefix)) throw new ArgumentException("SaveSlotOptions.KeyPrefix が空です", nameof(options));
            _active = new Scope(this, null, followsActive: true);
            _global = new Scope(this, null, followsActive: false);
        }

        /// <inheritdoc />
        public string ActiveSlotId { get; private set; }

        /// <inheritdoc />
        public string LastSelectedSlotId => Index.LastSelected;

        /// <inheritdoc />
        public ISaveScope Active => _active;

        /// <inheritdoc />
        public ISaveScope Global => _global;

        /// <inheritdoc />
        public ISaveScope GetScope(string slotId)
        {
            if (!TryNormalize(slotId, out var id, logError: false)) throw new ArgumentException($"スロット ID \"{slotId}\" が不正です (英小文字・数字・'-' のみ、{MaxSlotIdLength} 文字まで)", nameof(slotId));
            if (!_scopes.TryGetValue(id, out var scope)) _scopes[id] = scope = new Scope(this, id, followsActive: false);
            return scope;
        }

        /// <inheritdoc />
        public IReadOnlyList<SaveSlotInfo> GetSlots()
        {
            var list = new List<SaveSlotInfo>(Index.Slots.Count);
            foreach (var slot in Index.Slots) list.Add(slot.Clone());
            return list;
        }

        /// <inheritdoc />
        public bool TryGetSlot(string slotId, out SaveSlotInfo info)
        {
            info = TryNormalize(slotId, out var id, logError: false) ? Find(id)?.Clone() : null;
            return info != null;
        }

        /// <inheritdoc />
        public bool Exists(string slotId) => TryNormalize(slotId, out var id, logError: false) && Find(id) != null;

        /// <inheritdoc />
        public bool Create(string slotId, string summary = null)
        {
            if (!TryNormalize(slotId, out var id)) return false;
            if (Find(id) != null)
            {
                Debug.LogWarning($"[Abubu.Save] スロット \"{id}\" はすでにあります。");
                return false;
            }
            var info = NewInfo(id);
            info.Summary = summary;
            Index.Slots.Add(info);
            return SaveIndex();
        }

        /// <inheritdoc />
        public bool Copy(string sourceSlotId, string destinationSlotId, bool overwrite = false)
        {
            if (!TryNormalize(sourceSlotId, out var sourceId) || !TryNormalize(destinationSlotId, out var destinationId)) return false;
            var source = Find(sourceId);
            if (source == null)
            {
                Debug.LogError($"[Abubu.Save] コピー元のスロット \"{sourceId}\" がありません。");
                return false;
            }
            if (sourceId == destinationId) return true;
            if (Find(destinationId) != null)
            {
                if (!overwrite)
                {
                    Debug.LogError($"[Abubu.Save] コピー先のスロット \"{destinationId}\" はすでにあります (置き換える場合は overwrite: true)。");
                    return false;
                }
                DeleteData(Find(destinationId));
                Index.Slots.Remove(Find(destinationId));
            }

            // 書き込み途中のデータを確実にコピーするため、先に非同期の書き込みを終わらせる
            _root.Flush();
            var copy = source.Clone();
            copy.Id = destinationId;
            copy.CreatedAtUtc = SaveSlotInfo.FormatUtc(DateTime.UtcNow);
            copy.Keys = new List<string>();
            var ok = true;
            foreach (var key in source.Keys)
            {
                var from = ToStorageKey(sourceId, key);
                var to = ToStorageKey(destinationId, key);
                try
                {
                    if (!_storage.TryRead(from, out var text)) continue;
                    // 古い状態 (新しいバージョンのデータの保護など) が残らないよう、セーブサービス経由で消してから書く
                    _root.Delete(to);
                    _storage.Write(to, text);
                    copy.Keys.Add(key);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Abubu.Save] スロット \"{sourceId}\" の \"{key}\" を \"{destinationId}\" にコピーできませんでした: {e}");
                    ok = false;
                }
            }
            // 一部失敗しても、コピーできた分は一覧に載せて削除できるようにする
            Index.Slots.Add(copy);
            return SaveIndex() && ok;
        }

        /// <inheritdoc />
        public bool Delete(string slotId)
        {
            if (!TryNormalize(slotId, out var id)) return false;
            var info = Find(id);
            if (info == null) return false;

            DeleteData(info);
            Index.Slots.Remove(info);
            if (Index.LastSelected == id) Index.LastSelected = null;
            var saved = SaveIndex();
            if (ActiveSlotId == id) ClearActive();
            return saved;
        }

        /// <inheritdoc />
        public bool Switch(string slotId, bool createIfMissing = true)
        {
            if (!TryNormalize(slotId, out var id)) return false;
            if (Find(id) == null)
            {
                if (!createIfMissing)
                {
                    Debug.LogError($"[Abubu.Save] スロット \"{id}\" がありません。");
                    return false;
                }
                Index.Slots.Add(NewInfo(id));
            }

            Index.LastSelected = id;
            SaveIndex();
            if (ActiveSlotId == id) return true;
            ActiveSlotId = id;
            RaiseActiveSlotChanged();
            return true;
        }

        /// <inheritdoc />
        public void ClearActive()
        {
            if (ActiveSlotId == null) return;
            ActiveSlotId = null;
            RaiseActiveSlotChanged();
        }

        /// <inheritdoc />
        public bool SetSummary(string slotId, string summary)
        {
            if (!TryFindExisting(slotId, out var info)) return false;
            info.Summary = summary;
            return SaveIndex();
        }

        /// <inheritdoc />
        public bool AddPlayTime(string slotId, TimeSpan delta)
        {
            if (!TryFindExisting(slotId, out var info)) return false;
            info.PlayTimeSeconds = Math.Max(0, info.PlayTimeSeconds + delta.TotalSeconds);
            // 毎フレーム呼ばれても毎回は書き込まない (メモリ上の値はすぐ更新され、ほかの保存時にも一緒に保存される)
            _unsavedPlayTime += Math.Abs(delta.TotalSeconds);
            if (_unsavedPlayTime < _options.PlayTimeSaveInterval) return true;
            return SaveIndex();
        }

        /// <summary>スロット内のキーから、共通の領域で使うキーを作る ("1", "progress" → "slot/1/progress")</summary>
        public string ToStorageKey(string slotId, string key) => _options.KeyPrefix + slotId + "/" + key;

        // ───────────────────────── 内部 ─────────────────────────

        private SaveSlotIndex Index
        {
            get
            {
                if (_index != null) return _index;
                _index = _root.TryLoad<SaveSlotIndex>(_options.IndexKey, out var loaded) && loaded != null ? loaded : new SaveSlotIndex();
                _index.Slots ??= new List<SaveSlotInfo>();
                _index.Slots.RemoveAll(s => s == null || string.IsNullOrEmpty(s.Id));
                foreach (var slot in _index.Slots) slot.Keys ??= new List<string>();
                return _index;
            }
        }

        private bool SaveIndex()
        {
            // 失敗しても毎フレーム保存し直さないよう、先に戻す
            _unsavedPlayTime = 0;
            return _root.Save(_options.IndexKey, Index);
        }

        private SaveSlotInfo Find(string id) => Index.Slots.Find(s => s.Id == id);

        private bool TryFindExisting(string slotId, out SaveSlotInfo info)
        {
            info = null;
            if (!TryNormalize(slotId, out var id)) return false;
            info = Find(id);
            if (info != null) return true;
            Debug.LogError($"[Abubu.Save] スロット \"{id}\" がありません。");
            return false;
        }

        private SaveSlotInfo NewInfo(string id) => new()
        {
            Id = id,
            CreatedAtUtc = SaveSlotInfo.FormatUtc(DateTime.UtcNow),
            LastSavedAtUtc = "",
            GameVersion = GameVersion,
        };

        private string GameVersion => string.IsNullOrEmpty(_options.GameVersion) ? Application.version : _options.GameVersion;

        private void DeleteData(SaveSlotInfo info)
        {
            foreach (var key in info.Keys) _root.Delete(ToStorageKey(info.Id, key));
        }

        /// <summary>スロットへの保存が成功した時: キーを記録し、最終保存日時を更新する</summary>
        private void OnWritten(string id, string key)
        {
            var info = Find(id);
            var changed = false;
            if (info == null)
            {
                info = NewInfo(id);
                Index.Slots.Add(info);
                changed = true;
            }
            if (!info.Keys.Contains(key))
            {
                info.Keys.Add(key);
                changed = true;
            }
            if (_options.UpdateLastSavedOnSave)
            {
                info.LastSavedAtUtc = SaveSlotInfo.FormatUtc(DateTime.UtcNow);
                info.GameVersion = GameVersion;
                changed = true;
            }
            if (changed) SaveIndex();
        }

        private void OnDeleted(string id, string key)
        {
            var info = Find(id);
            if (info != null && info.Keys.Remove(key)) SaveIndex();
        }

        private void RaiseActiveSlotChanged()
        {
            try
            {
                ActiveSlotChanged?.Invoke(ActiveSlotId);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        /// <summary>ID を検証し、小文字に揃える</summary>
        private static bool TryNormalize(string slotId, out string id, bool logError = true)
        {
            id = null;
            if (string.IsNullOrEmpty(slotId) || slotId.Length > MaxSlotIdLength)
            {
                if (logError) Debug.LogError($"[Abubu.Save] スロット ID \"{slotId}\" が不正です (1〜{MaxSlotIdLength} 文字)。");
                return false;
            }
            var lower = slotId.ToLowerInvariant();
            foreach (var c in lower)
            {
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-') continue;
                if (logError) Debug.LogError($"[Abubu.Save] スロット ID \"{slotId}\" が不正です (英小文字・数字・'-' のみ)。");
                return false;
            }
            id = lower;
            return true;
        }

        /// <summary>1 つの領域の読み書き。キーに接頭辞を付けて共通のセーブサービスに渡す</summary>
        private sealed class Scope : ISaveScope
        {
            private readonly SaveSlotService _owner;
            private readonly string _fixedId;
            private readonly bool _followsActive;

            public Scope(SaveSlotService owner, string fixedId, bool followsActive)
            {
                _owner = owner;
                _fixedId = fixedId;
                _followsActive = followsActive;
            }

            public string SlotId => _followsActive ? _owner.ActiveSlotId : _fixedId;

            private ISaveService Root => _owner._root;

            /// <summary>保存先のキー。共通の領域・空のキー (エラーはセーブサービスが出す) はそのまま</summary>
            private string Map(string key, out string slotId)
            {
                slotId = SlotId;
                return slotId == null || string.IsNullOrEmpty(key) ? key : _owner.ToStorageKey(slotId, key);
            }

            public bool Save<T>(string key, T data)
            {
                var ok = Root.Save(Map(key, out var slotId), data);
                if (ok && slotId != null) _owner.OnWritten(slotId, key);
                return ok;
            }

            public bool ForceSave<T>(string key, T data)
            {
                var ok = Root.ForceSave(Map(key, out var slotId), data);
                if (ok && slotId != null) _owner.OnWritten(slotId, key);
                return ok;
            }

            public bool TryLoad<T>(string key, out T data) => Root.TryLoad(Map(key, out _), out data);

            public T Load<T>(string key, T defaultValue = default) => Root.Load(Map(key, out _), defaultValue);

            public bool Exists(string key) => Root.Exists(Map(key, out _));

            public void Delete(string key)
            {
                Root.Delete(Map(key, out var slotId));
                if (slotId != null && !string.IsNullOrEmpty(key)) _owner.OnDeleted(slotId, key);
            }

            public async Task<SaveResult> SaveAsync<T>(string key, T data)
            {
                var result = await Root.SaveAsync(Map(key, out var slotId), data);
                if (result.Success && slotId != null) _owner.OnWritten(slotId, key);
                return result;
            }

            public async Task<SaveResult> ForceSaveAsync<T>(string key, T data)
            {
                var result = await Root.ForceSaveAsync(Map(key, out var slotId), data);
                if (result.Success && slotId != null) _owner.OnWritten(slotId, key);
                return result;
            }

            public Task<SaveLoadResult<T>> LoadAsync<T>(string key) => Root.LoadAsync<T>(Map(key, out _));

            public bool HasPendingWrites => Root is IAsyncSaveService asyncSave && asyncSave.HasPendingWrites;

            public void Flush() => Root.Flush();

            public Task FlushAsync() => Root is IAsyncSaveService asyncSave ? asyncSave.FlushAsync() : Task.CompletedTask;
        }
    }
}
