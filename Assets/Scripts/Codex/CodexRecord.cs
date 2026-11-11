using System.Collections.Generic;
using GameLoop;
using MetaProgression;
using UnityEngine;

namespace Codex
{
    /// <summary>
    /// 記録 (codex) の軽い項目 ── 層・敵・品 ── の「着いた・戦った・手に入れた」を数える (#24・2026-11-11)。
    /// 重大な読み物 (<see cref="WorldVignettes"/>) は結末の後で開くが、 こちらはランの途中で出会った時に開く (docs/GAME.md §20「codex の解禁」)。
    /// <para>保存は <see cref="MetaProgressState.codexEnemies"/> / <see cref="MetaProgressState.codexItems"/> / <see cref="MetaProgressState.codexFloors"/>。
    /// 数えるのは人が遊んでいる時だけ (BOT・バッチ実行では数えない ── AutoRunner はラン毎にメタ進行を作り直すので、 数えても残らないが無駄な保存になる)。</para>
    /// <para>ゲームの処理には触らない: <see cref="CodexRecorder"/> が毎フレーム、 今のラン・戦闘の状態を読むだけ。</para>
    /// </summary>
    public static class CodexRecord
    {
        private static MetaProgressState Meta => MetaProgressManager.Instance?.State;

        public static bool EnemyMet(string id) { var m = Meta; return m != null && id != null && m.codexEnemies != null && m.codexEnemies.Contains(id); }
        public static bool ItemOwned(string id) { var m = Meta; return m != null && id != null && m.codexItems != null && m.codexItems.Contains(id); }
        public static bool FloorReached(int depth) { var m = Meta; return m != null && m.codexFloors != null && m.codexFloors.Contains(depth); }
        /// <summary>着いたことのある一番深い層 (Run.Depth・無ければ −1)。</summary>
        public static int DeepestFloor() { var m = Meta; int d = -1; if (m?.codexFloors != null) foreach (int f in m.codexFloors) if (f > d) d = f; return d; }

        private static bool Add<T>(List<T> l, T v) { if (l == null || l.Contains(v)) return false; l.Add(v); return true; }

        /// <summary>今の状態から新しい記録を足す。 足したら true (呼び側が保存する)。</summary>
        internal static bool Note(RunState run, CombatSystem.CombatManager cm)
        {
            var m = Meta; if (m == null) return false;
            if (m.codexEnemies == null) m.codexEnemies = new List<string>();
            if (m.codexItems == null) m.codexItems = new List<string>();
            if (m.codexFloors == null) m.codexFloors = new List<int>();
            bool added = false;
            if (run != null)
            {
                added |= Add(m.codexFloors, run.Depth);
                var db = ItemSystem.ItemDatabase.Instance;
                if (db != null)
                {
                    void Item(string id) { if (string.IsNullOrEmpty(id)) return; var data = db.GetItem(id); if (data != null) added |= Add(m.codexItems, data.id); }
                    if (run.ownedPassiveItems != null) foreach (var id in run.ownedPassiveItems) Item(id);
                    if (run.ownedConsumables != null) foreach (var id in run.ownedConsumables) Item(id);
                    Item(run.equippedWeaponId);
                }
            }
            if (cm != null && cm.IsCombatActive)
            {
                if (cm.CurrentEnemy != null) added |= Add(m.codexEnemies, cm.CurrentEnemy.id);
                if (cm.SecondaryEnemy != null) added |= Add(m.codexEnemies, cm.SecondaryEnemy.id);
            }
            return added;
        }
    }
}
