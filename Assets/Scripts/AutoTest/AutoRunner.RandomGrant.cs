using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using GameLoop;
using CombatSystem;
using MapSystem;
using EventSystem;
using ItemSystem;
using ItemSystem.Shop;
using AutoTest.Ultra;

namespace AutoTest
{
    /// <summary>AutoRunner: ランダム付与試行 (2026-09-08)。</summary>
    public partial class AutoRunner
    {
        /// <summary>このランの割り当てを引く。 <b>GameRng を使わない</b> ── 消費列が変われば
        /// 同一シードの他の測定と比較できなくなる。 ラン番号だけで決まるので再現する。</summary>
        private void RollGrantPlan(int runIndex, GameLoop.RunState run)
        {
            _grantPlan.Clear();
            _grantGoldAmount = 0; _grantGoldFloor = 0;
            _grantRng = new System.Random(unchecked(runIndex * 2654435761u).GetHashCode());

            var db = ItemSystem.ItemDatabase.Instance;
            var all = db?.GetAllItems();
            if (all == null || all.Count == 0) return;

            // 対象は「普通に手へ入る品」。 イベント専用・職業スターターは
            //   通常の入手経路が無いので、 配ると存在しない状況を測ることになる
            //   (EventOnlyItemFilter がどちらも弾く)。
            //
            // [2026-09-17] **randomGrantAllKinds で消耗品・武器・出目パーツまで広げる。**
            //   ITT が意見を持つのがパッシブだけだと、 準パワーを Δクリア率 (pt) 単位で
            //   運用できない ── 非ITT品が regβ (band 単位) のまま同じ序列へ混ざり、
            //   しきい値が両者の間に落ちて**パーツと消耗品が丸ごと足切りされる**。
            //   band→pt の等化は引けなかった (2026-09-17 実測 89 対で r=−0.100 ＝
            //   相関が検出できない。 regβ は内生・ITT は外生で別物を測っている)。
            //   単位を揃える道は「全品を付与プールへ入れる」しか無い。
            var pool = new List<string>();
            int nPassive = 0, nCons = 0, nWeapon = 0, nPart = 0;
            foreach (var it in all)
            {
                if (it == null) continue;
                if (!ItemSystem.Shop.EventOnlyItemFilter.IsAllowed(it)) continue;
                if (it.category == ItemSystem.ItemCategory.Passive)
                { pool.Add(it.internalName); nPassive++; continue; }
                if (!randomGrantAllKinds) continue;
                if (it.category == ItemSystem.ItemCategory.Consumable)
                { pool.Add(it.internalName); nCons++; continue; }
                if (it.category == ItemSystem.ItemCategory.Weapon)
                { pool.Add(it.internalName); nWeapon++; continue; }
            }
            if (randomGrantAllKinds)
            {
                // 出目パーツは ItemDatabase に無い (DiceFaceParts.cs の規約コメント)。
                //   36 種を ID で列挙する ── 所持済みの除外は配る側 (ApplyGrants) で見る。
                foreach (var kind in GameLoop.DiceFaceParts.AllKinds())
                { pool.Add(GameLoop.DiceFaceParts.Id(kind)); nPart++; }
            }
            if (pool.Count == 0) return;
            if (!_grantPoolLogged)
            {
                _grantPoolLogged = true;
                Debug.Log($"[AutoRunner] 付与プール {pool.Count} 品"
                        + $" (パッシブ {nPassive} / 消耗品 {nCons} / 武器 {nWeapon} / 出目パーツ {nPart})"
                        + $" / 1品あたり付与確率 {Mathf.Clamp01(randomGrantExpectedItems / pool.Count):P2}"
                        + $" / 期待付与数 {randomGrantExpectedItems:F1}");
            }

            float p = Mathf.Clamp01(randomGrantExpectedItems / pool.Count);
            for (int i = 0; i < pool.Count; i++)
            {
                if (_grantRng.NextDouble() >= p) continue;
                // **層をランダムにする。** 開幕固定だと複利で効く品だけが贔屓される。
                _grantPlan.Add((_grantRng.Next(1, 6), pool[i]));
            }

            // 金も 1 つの処置。 係数がそのまま 1G の band 価値になる。
            var levels = new List<int> { 0 };
            if (!string.IsNullOrEmpty(randomGrantGoldLevels))
                foreach (var s in randomGrantGoldLevels.Split(','))
                    if (int.TryParse(s.Trim(), out int v) && v > 0) levels.Add(v);
            _grantGoldAmount = levels[_grantRng.Next(levels.Count)];
            _grantGoldFloor  = _grantGoldAmount > 0 ? _grantRng.Next(1, 6) : 0;

            // 割り当てログはここで確定させる (band は Finish 後に足す)。
            var sb = new StringBuilder();
            sb.Append(_grantGoldAmount).Append('@').Append(_grantGoldFloor).Append('|');
            for (int i = 0; i < _grantPlan.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(_grantPlan[i].id).Append('@').Append(_grantPlan[i].floor);
            }
            _pendingGrantRow = sb.ToString();
        }
        private string _pendingGrantRow;

        /// <summary>割り当てられた 1 品を種別に応じて配る (2026-09-17)。
        ///
        /// <para>入手経路は<b>ショップの購入処理と同じものを通す</b>
        /// (<see cref="ItemSystem.Shop.ShopManager"/> の CompletePurchase)。
        /// 別経路を作ると「配られた状態」と「買った状態」がずれて、
        /// ITT で測った効果が本番の購入判断へ載らない。</para>
        ///
        /// <para><b>失敗しても計画からは消す。</b> 所持済みの出目パーツや、
        /// 更新にならない武器は何も起きないが、 それは「その品を渡されることの効果」が
        /// たまたま 0 だったということ ── 割り当ては割り当てなので ITT は偏らない。
        /// ここで配り直すと処置確率が状態に依存してランダム化が壊れる。</para></summary>
        private static void GrantOne(GameLoop.RunState run, string id)
        {
            if (run == null || string.IsNullOrEmpty(id)) return;

            // 出目パーツ: ItemDatabase に無いので ID から復元する。
            if (GameLoop.DiceFaceParts.IsPartId(id))
            {
                if (!GameLoop.DiceFaceParts.TryParseId(id, out var part)) { NoteGrant(0, false); return; }
                if (run.diceFaceParts == null) { NoteGrant(0, false); return; }
                // 同じ目は 1 枚まで: 同じか低い Tier なら何も起きない (差し替えは高い Tier の時だけ)。
                bool changed = GameLoop.DiceFaceParts.AddOrReplace(run.diceFaceParts, part);
                NoteGrant(0, changed);
                return;
            }

            var data = ItemSystem.ItemDatabase.Instance?.GetItem(id);
            if (data != null && data.category == ItemSystem.ItemCategory.Consumable)
            { NoteGrant(1, run.TryAddConsumable(id)); return; }

            // パッシブと武器は同じ経路 (ShopManager も同じ 2 行を呼ぶ)。
            bool added = ItemSystem.Helpers.PassiveAddHelper.AddPassiveItem(run, id);
            bool weapon = data != null && data.category == ItemSystem.ItemCategory.Weapon;
            if (weapon) GameLoop.Loadout.TryAutoEquip(run, id);
            NoteGrant(weapon ? 2 : 3, added);
        }

        /// <summary>[計装 2026-09-17] <b>割り当てではなく「実際に届いたか」を数える。</b>
        ///
        /// <para>grant_runs.csv に載るのは <b>割り当て計画</b> (RollGrantPlan が組む) なので、
        /// 配る側が黙って落としていても行は立つ。 出目パーツは ItemDatabase に存在しない ID で、
        /// 消耗品・武器も別経路なので、 **ログを見ただけでは配布の成否が分からない**。
        /// 添字: 0=出目パーツ 1=消耗品 2=武器 3=パッシブ。</para></summary>
        public static readonly long[] GrantOk = new long[4];
        public static readonly long[] GrantNg = new long[4];
        public static readonly string[] GrantKindName = { "出目パーツ", "消耗品", "武器", "パッシブ" };
        public static void ResetGrantStats()
        { System.Array.Clear(GrantOk, 0, 4); System.Array.Clear(GrantNg, 0, 4); }
        private static void NoteGrant(int kind, bool ok)
        { if (kind < 0 || kind > 3) return; if (ok) GrantOk[kind]++; else GrantNg[kind]++; }

        /// <summary>到達した層ぶんの付与を実行する。 配り終えたものは計画から外す。</summary>
        private void ApplyDueGrants(GameManager gm)
        {
            var run = gm?.Run;
            if (run == null) return;
            int floor = run.Depth;

            for (int i = _grantPlan.Count - 1; i >= 0; i--)
            {
                if (_grantPlan[i].floor > floor) continue;
                GrantOne(run, _grantPlan[i].id);
                _cur?.acquiredItemsEver.Add(_grantPlan[i].id);
                _grantPlan.RemoveAt(i);
            }
            if (_grantGoldAmount > 0 && _grantGoldFloor <= floor)
            {
                GameLoop.GoldIncome.Gain(run, _grantGoldAmount, "付与試行");
                _grantGoldAmount = 0;
            }
        }

        /// <summary>ラン終了後に band と近位指標を添えて割り当てログへ 1 行足す。
        ///
        /// <para><b>近位指標を足した理由 (2026-09-08)。</b> 鈍器家系が「効果 6 倍で価値が単調に下がる」
        /// (Bludgeon_1 +0.134 → Lv4 −0.365) 問題で、 <b>会心との衝突という仮説は棄却された</b>
        /// (交互作用 +0.024 ± 0.036 / 会心を多く配るほど鈍器は<b>良く</b>なる)。
        /// 残る有力な筋は「配線 AI を誤誘導している」 ── Optimal は 3^5 を全列挙して評価関数で
        /// 選ぶので、 「非会心なら +120%」が評価に乗ると会心を避ける配線を選ぶ。
        /// <b>実ダメージが落ちていれば誤誘導、 ダメージは変わらず band だけ落ちるなら別の副作用</b>。
        /// band は 7 層ぶんの高分散な成果なので、 近位指標の方が同じラン数で解像度が高い。</para>
        ///
        /// <para>列: <c>band|gold@floor|id@floor,...|与ダメ,被ダメ,戦闘数,勝利数,回復,シールド</c></para></summary>
        private void RecordGrantRow()
        {
            if (!randomGrantTrial || string.IsNullOrEmpty(_pendingGrantRow)) return;
            var rec = _records.Count > 0 ? _records[_records.Count - 1] : null;
            int band = rec != null ? rec.bandScore : -1;
            string prox = rec == null ? "0,0,0,0,0,0"
                : string.Join(",", rec.totalDamageDealt, rec.totalDamageTaken,
                              rec.totalCombats, rec.totalWins, rec.totalHealed, rec.totalShieldGained);
            _grantLog.Add(band + "|" + _pendingGrantRow + "|" + prox);
            _pendingGrantRow = null;
        }

        /// <summary>ランダム付与試行の本体。 アームは無く、 全ランが同じ手続きで割り当てを引く。</summary>
        private IEnumerator RunRandomGrantTrial()
        {
            int runs = Mathf.Max(100, randomGrantRuns);
            _grantLog.Clear();
            ResetScreenProgress(runs, "ランダム付与");
            curArmLabel = "ランダム付与"; curArmIndex = 1; curArmCount = 1; curRunsPerArm = runs;

            for (int i = 0; i < runs; i++)
            {
                // **並列化の要 (2026-09-17)。** runIdx は GameRng と付与抽選 (RollGrantPlan)
                //   の両方を決める。 プロセスごとに区間をずらさないと、 全プロセスが
                //   **同じ割り当てを引いて同じランを繰り返す** ── 行数だけ増えて情報は増えない。
                int runIdx = 90000 + randomGrantStartIndex + i;
                GameLoop.GameRng.BeginRun(runIdx);
                if (GameLoop.GameRng.IsSeeded)
                    _rng = new System.Random(GameLoop.GameRng.Range(0, int.MaxValue, "autorun.eventTiebreak", runIdx));
                yield return RunOne(runIdx);
                RecordGrantRow();
                if ((i + 1) % Mathf.Max(1, runsPerYield) == 0) yield return null;
            }
        }
    }
}
