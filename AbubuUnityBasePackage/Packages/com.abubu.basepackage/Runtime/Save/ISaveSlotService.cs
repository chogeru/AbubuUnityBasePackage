using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Abubu.Save
{
    /// <summary>
    /// セーブスロット (「データ 1」「データ 2」…) の管理。スロットの一覧・作成・コピー・削除・切り替えと、
    /// スロットごとのデータの読み書き (<see cref="Active"/>) を行う。
    /// <code>
    /// _slots.Switch("1");                          // 無ければ作って切り替える
    /// _slots.Active.Save("progress", progress);    // スロット 1 に保存
    /// _slots.Global.Save("volume", volume);        // どのスロットからも共通の領域 (= ISaveService と同じ)
    /// foreach (var slot in _slots.GetSlots()) Debug.Log($"{slot.Id} {slot.LastSavedAt} {slot.Summary}");
    /// </code>
    /// </summary>
    /// <remarks>
    /// <see cref="ISaveService"/> そのもの (音量・画面設定などパッケージの各機能が使う領域) はスロットを切り替えても変わらない「共通の領域」。
    /// スロットごとに分けたいデータだけを <see cref="Active"/> (または <see cref="GetScope"/>) 経由で読み書きする。
    /// スロット未選択の間は <see cref="Active"/> も共通の領域を指すので、スロットを使わないゲームはこれまでと同じ場所に保存される。
    /// <para>
    /// スロットのデータは共通の領域に "接頭辞 + スロット ID + / + キー" (既定 "slot/1/progress") のキーで保存される。
    /// スロットの一覧とメタデータ (<see cref="SaveSlotInfo"/>) は共通の領域の 1 キー (<see cref="SaveSlotOptions.IndexKey"/>) にまとめて保存される。
    /// </para>
    /// <para>
    /// スロット ID は英小文字・数字・'-' のみ (32 文字まで)。大文字は小文字に揃える (Windows のファイル名が大文字・小文字を区別しないため)。
    /// 失敗しても例外は投げず、エラーログと戻り値 (false) で知らせる。メインスレッドから使う。
    /// </para>
    /// </remarks>
    public interface ISaveSlotService
    {
        /// <summary>選択中のスロット ID。未選択なら null</summary>
        string ActiveSlotId { get; }

        /// <summary>前回 (前回の起動を含む) 最後に選択したスロット ID。「つづきから」の初期選択などに使う。無ければ null</summary>
        string LastSelectedSlotId { get; }

        /// <summary>
        /// 選択中のスロットのデータの読み書き。切り替えに追従する (取得しておいたものを使い続けてよい)。
        /// 未選択の間は共通の領域 (<see cref="Global"/>) と同じ場所を読み書きする。
        /// </summary>
        ISaveScope Active { get; }

        /// <summary>スロットに関係しない共通の領域 (設定など)。<see cref="ISaveService"/> と同じ場所</summary>
        ISaveScope Global { get; }

        /// <summary>
        /// 指定スロットのデータの読み書き (切り替えずに、一覧画面で各スロットの中身を読む場合など)。
        /// このスコープ経由で保存するとスロットが一覧に無ければ作られる。
        /// </summary>
        /// <exception cref="ArgumentException">ID が不正 (プログラムの誤り)</exception>
        ISaveScope GetScope(string slotId);

        /// <summary>スロットの一覧 (作成順)。返したオブジェクトを変更しても保存内容には影響しない</summary>
        IReadOnlyList<SaveSlotInfo> GetSlots();

        /// <summary>スロットの情報を取得する。無ければ false</summary>
        bool TryGetSlot(string slotId, out SaveSlotInfo info);

        /// <summary>スロットがあるか</summary>
        bool Exists(string slotId);

        /// <summary>空のスロットを作る。すでにある・ID が不正なら false</summary>
        /// <param name="summary">一覧に表示する概要 (任意の文字列。JSON を入れてもよい)</param>
        bool Create(string slotId, string summary = null);

        /// <summary>スロットのデータとメタデータを別のスロットに複製する。コピー先がある場合は overwrite が true の時だけ置き換える</summary>
        bool Copy(string sourceSlotId, string destinationSlotId, bool overwrite = false);

        /// <summary>スロットとそのデータをすべて削除する。選択中なら未選択に戻る。無ければ false</summary>
        bool Delete(string slotId);

        /// <summary>スロットを選択する。以降 <see cref="Active"/> はこのスロットを読み書きする</summary>
        /// <param name="createIfMissing">無ければ作る (false なら無い場合は失敗)</param>
        bool Switch(string slotId, bool createIfMissing = true);

        /// <summary>スロットを未選択に戻す (<see cref="Active"/> は共通の領域を指す)</summary>
        void ClearActive();

        /// <summary>一覧に表示する概要を設定する</summary>
        bool SetSummary(string slotId, string summary);

        /// <summary>
        /// プレイ時間を加算する (ゲーム側で計測した時間を、保存のたびや毎フレームに渡す)。
        /// 毎フレーム呼んでも毎回は書き込まない: 加算は即座に <see cref="GetSlots"/> などに反映され、
        /// 保存は未保存の加算が <see cref="SaveSlotOptions.PlayTimeSaveInterval"/> 秒たまった時か、スロットの一覧がほかの理由で保存される時
        /// (スロットへの Save・<see cref="SetSummary"/> など) にまとめて行う。
        /// </summary>
        /// <returns>加算できたら true (保存した場合は保存にも成功したら true)。スロットが無い・ID が不正なら false</returns>
        bool AddPlayTime(string slotId, TimeSpan delta);

        /// <summary>選択中のスロットが変わった時 (引数は新しいスロット ID。未選択になったら null)</summary>
        event Action<string> ActiveSlotChanged;
    }

    /// <summary>
    /// 1 つの領域 (スロット / 共通) のデータの読み書き。<see cref="ISaveService"/> と同じ使い方で、非同期版も使える。
    /// <code>
    /// ISaveScope data = _slots.Active;
    /// data.Save("progress", progress);
    /// await data.SaveAsync("world", world);
    /// </code>
    /// </summary>
    public interface ISaveScope : ISaveService, IAsyncSaveService
    {
        /// <summary>読み書きするスロットの ID。共通の領域なら null</summary>
        string SlotId { get; }
    }

    /// <summary>
    /// スロットの情報 (一覧画面の表示用)。<see cref="ISaveSlotService.GetSlots"/> で取得する。
    /// <code>
    /// label.text = $"{info.Id}  {info.PlayTime:hh\\:mm}  {info.LastSavedAt?.ToLocalTime():g}  {info.Summary}";
    /// </code>
    /// </summary>
    /// <remarks>
    /// どのシリアライザでも保存できるよう public フィールドで持つ。値の変更は <see cref="ISaveSlotService"/> のメソッドで行う
    /// (ここで書き換えても保存されない)。
    /// </remarks>
    [Serializable]
    public sealed class SaveSlotInfo
    {
        /// <summary>スロット ID</summary>
        public string Id;
        /// <summary>作成日時 (UTC, ISO 8601)</summary>
        public string CreatedAtUtc;
        /// <summary>最後にこのスロットへ保存した日時 (UTC, ISO 8601)。未保存なら空</summary>
        public string LastSavedAtUtc;
        /// <summary>プレイ時間 (秒)</summary>
        public double PlayTimeSeconds;
        /// <summary>一覧に表示する概要 (任意の文字列。JSON を入れてもよい)</summary>
        public string Summary;
        /// <summary>最後に保存したときのゲームのバージョン (既定は Application.version)</summary>
        public string GameVersion;
        /// <summary>このスロットに保存されているキー (コピー・削除に使う)</summary>
        public List<string> Keys = new();

        /// <summary>作成日時 (UTC)</summary>
        public DateTime? CreatedAt => ParseUtc(CreatedAtUtc);
        /// <summary>最後に保存した日時 (UTC)。未保存なら null</summary>
        public DateTime? LastSavedAt => ParseUtc(LastSavedAtUtc);
        /// <summary>プレイ時間</summary>
        public TimeSpan PlayTime => TimeSpan.FromSeconds(PlayTimeSeconds);

        /// <summary>複製 (Keys も別のリストにする)</summary>
        public SaveSlotInfo Clone()
        {
            var clone = (SaveSlotInfo)MemberwiseClone();
            clone.Keys = Keys != null ? new List<string>(Keys) : new List<string>();
            return clone;
        }

        internal static string FormatUtc(DateTime utc) => utc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

        private static DateTime? ParseUtc(string text) =>
            !string.IsNullOrEmpty(text) && DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value)
                ? value.ToUniversalTime()
                : null;
    }

    /// <summary>
    /// セーブスロットの設定。<see cref="SaveSettings.Slots"/> から Inspector で編集できる。
    /// <code>
    /// new SaveSlotService(save, storage, new SaveSlotOptions { KeyPrefix = "profile/" });
    /// </code>
    /// </summary>
    [Serializable]
    public sealed class SaveSlotOptions
    {
        [Tooltip("スロットの一覧を保存する共通の領域のキー")]
        public string IndexKey = "abubu.slots";

        [Tooltip("スロットのデータのキーの接頭辞 (\"slot/\" なら \"slot/1/progress\")。運用開始後は変えないこと")]
        public string KeyPrefix = "slot/";

        [Tooltip("スロットに保存するたびに最終保存日時とゲームのバージョンを更新する (一覧の保存も発生する)")]
        public bool UpdateLastSavedOnSave = true;

        [Tooltip("メタデータに記録するゲームのバージョン。空なら Application.version")]
        public string GameVersion = "";

        [Tooltip("AddPlayTime の加算を保存する間隔 (秒)。この秒数分たまるまでは書き込まない (ほかの保存時には一緒に保存される)。0 なら毎回保存する")]
        public float PlayTimeSaveInterval = 10f;
    }

    /// <summary>
    /// 番号でスロットを扱う拡張メソッド ("1" などの ID に変換する)。
    /// <code>
    /// _slots.Switch(1);
    /// </code>
    /// </summary>
    public static class SaveSlotServiceExtensions
    {
        /// <summary>番号をスロット ID にする (1 → "1")</summary>
        public static string ToSlotId(int number) => number.ToString(CultureInfo.InvariantCulture);

        public static bool Switch(this ISaveSlotService slots, int number, bool createIfMissing = true) => slots.Switch(ToSlotId(number), createIfMissing);
        public static bool Exists(this ISaveSlotService slots, int number) => slots.Exists(ToSlotId(number));
        public static bool Create(this ISaveSlotService slots, int number, string summary = null) => slots.Create(ToSlotId(number), summary);
        public static bool Delete(this ISaveSlotService slots, int number) => slots.Delete(ToSlotId(number));
        public static bool Copy(this ISaveSlotService slots, int source, int destination, bool overwrite = false) =>
            slots.Copy(ToSlotId(source), ToSlotId(destination), overwrite);
        public static ISaveScope GetScope(this ISaveSlotService slots, int number) => slots.GetScope(ToSlotId(number));
    }
}
