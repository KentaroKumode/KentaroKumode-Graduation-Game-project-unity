using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace AutoTest
{
    /// <summary>
    /// 戦闘名ごとの危険度を、 実際の戦闘の結果から統計的に学習する (2026-11-05)。
    ///
    /// <para><b>なぜ要るか。</b> マップに出る戦闘名 (2 体戦のプリセット名・単体戦の名前・ボスの名前) は、 プレイヤーが
    /// 「この名前は危なかった」と覚える情報として設計されている。 BOT はその名前を読めず、 敵の数値からの見積り
    /// (<c>TileDanger</c>) は既定で無効だった。 偵察・照明で名前が見えても活かす判断が無く、 使うほど悪化した
    /// (同じ 5,000 シードでクリア 19.4% → 17.9%)。 ここでは名前の危険度を結果から学ぶ。</para>
    ///
    /// <para><b>鍵</b> = 「層|種類|戦闘名」。 種類は N 通常 / E エリート / B ボス。 エリートは通常と同じ名前を持つので
    /// (マスの種別で見分けがつく)、 種類を鍵に含めないと分布が混ざる。 層を含めるのは、 同じ敵が複数の層に出て
    /// プレイヤーの強さが違うため。</para>
    ///
    /// <para><b>危険度</b> = 戦闘で失った HP の最大 HP に対する割合 (開始時 HP − 終了時 HP) / 最大 HP。 平均・標準偏差・敗北率を持つ。
    /// 敗北した戦闘は HP が 0 で打ち切られる (本当はもっと失う) ので、 敗北率も別に持つ。</para>
    /// </summary>
    public static class EncounterDangerStats
    {
        public const string FileName = "encounter_danger.json";
        /// <summary>鍵の戦闘数がこれ未満なら学習値は使わない (層の最悪値の見積りへ落ちる)。</summary>
        public const int MinSamples = 40;

        [Serializable] public class Entry { public string key; public int n; public double sum, sumSq; public int lost; }
        [Serializable] private class FileData { public List<Entry> entries = new List<Entry>(); }

        private static readonly Dictionary<string, Entry> _byKey = new Dictionary<string, Entry>();
        public static string LoadedFrom = "";

        /// <summary>鍵を作る。 戦闘名が無い (追跡者・特殊戦) なら null。</summary>
        public static string KeyOf(MapSystem.MapNode node, int depth)
        {
            if (node == null) return null;
            var t = node.EffectiveType;
            char kind = t == MapSystem.TileType.Boss ? 'B' : t == MapSystem.TileType.EliteBattle ? 'E' : t == MapSystem.TileType.Battle ? 'N' : '\0';
            if (kind == '\0') return null;
            var preset = GameLoop.EncounterPresets.Get(node.encounterId);
            string title = preset != null && !string.IsNullOrEmpty(preset.title) ? preset.title : GameLoop.EncounterPresets.SoloTitle(node.encounterEnemyA);
            if (string.IsNullOrEmpty(title)) title = node.encounterEnemyA;
            if (string.IsNullOrEmpty(title)) return null;
            int layer = MapSystem.FreeMove.FloorPlan.OrdinalOf(depth);
            return layer + "|" + kind + "|" + title;
        }

        public static void Reload(string root)
        {
            _byKey.Clear(); _kindCache.Clear(); LoadedFrom = "";
            try
            {
                string path = Path.Combine(root, FileName);
                if (!File.Exists(path)) return;
                var d = JsonUtility.FromJson<FileData>(File.ReadAllText(path, Encoding.UTF8));
                if (d?.entries == null) return;
                foreach (var e in d.entries) if (e != null && !string.IsNullOrEmpty(e.key)) _byKey[e.key] = e;
                LoadedFrom = path;
            }
            catch (Exception ex) { Debug.LogWarning("[EncounterDangerStats] 読み込み失敗: " + ex.Message); }
        }

        /// <summary>戦闘の結果を積んで書き出す。 <paramref name="samples"/> = (鍵, 失った HP の割合, 敗北したか)。</summary>
        public static void Ingest(string root, IEnumerable<(string key, double lossFrac, bool lost)> samples)
        {
            try
            {
                Reload(root);
                foreach (var (key, loss, lost) in samples)
                {
                    if (string.IsNullOrEmpty(key)) continue;
                    if (!_byKey.TryGetValue(key, out var e)) { e = new Entry { key = key }; _byKey[key] = e; }
                    double l = Math.Max(0.0, Math.Min(1.0, loss));
                    e.n++; e.sum += l; e.sumSq += l * l; if (lost) e.lost++;
                }
                var d = new FileData(); d.entries.AddRange(_byKey.Values);
                d.entries.Sort((a, b) => string.CompareOrdinal(a.key, b.key));
                Directory.CreateDirectory(root);
                File.WriteAllText(Path.Combine(root, FileName), JsonUtility.ToJson(d, true), new UTF8Encoding(false));
            }
            catch (Exception ex) { Debug.LogWarning("[EncounterDangerStats] 書き込み失敗: " + ex.Message); }
        }

        /// <summary>serial バッチ (AutoRunner) の記録から積む。</summary>
        public static void IngestRecs(string root, IList<AutoRunner.RunRec> recs)
        {
            if (recs == null) return;
            var list = new List<(string, double, bool)>();
            foreach (var r in recs)
            {
                if (r?.combats == null) continue;
                foreach (var c in r.combats)
                {
                    if (!c.isFightEnd || string.IsNullOrEmpty(c.encounterKey) || c.playerMaxHpEnd <= 0) continue;
                    list.Add((c.encounterKey, (double)(c.hpBefore - c.hpAfter) / c.playerMaxHpEnd, !c.won));
                }
            }
            Ingest(root, list);
        }

        /// <summary>学習値。 戦闘数が <see cref="MinSamples"/> に満たなければ false。</summary>
        /// <summary>層と種類 ('N' 通常 / 'E' 精鋭) の全戦闘名の平均。 戦闘名が開示されていない点の見積りに使う。</summary>
        public static bool TryGetKind(int layer, char kind, out double mean, out double loseRate)
        {
            mean = loseRate = 0;
            string prefix = layer + "|" + kind + "|";
            if (!_kindCache.TryGetValue(prefix, out var c))
            {
                double sum = 0, lost = 0; int n = 0;
                foreach (var kv in _byKey)
                    if (kv.Key.StartsWith(prefix, StringComparison.Ordinal)) { sum += kv.Value.sum; lost += kv.Value.lost; n += kv.Value.n; }
                c = (n, n > 0 ? sum / n : 0.0, n > 0 ? lost / n : 0.0);
                _kindCache[prefix] = c;
            }
            if (c.Item1 < MinSamples) return false;
            mean = c.Item2; loseRate = c.Item3;
            return true;
        }
        private static readonly Dictionary<string, (int, double, double)> _kindCache = new Dictionary<string, (int, double, double)>();

        public static bool TryGet(string key, out int n, out double mean, out double sd, out double loseRate)
        {
            n = 0; mean = sd = loseRate = 0;
            if (string.IsNullOrEmpty(key) || !_byKey.TryGetValue(key, out var e) || e.n < MinSamples) return false;
            n = e.n; mean = e.sum / e.n;
            double var = Math.Max(0.0, e.sumSq / e.n - mean * mean);
            sd = Math.Sqrt(var); loseRate = (double)e.lost / e.n;
            return true;
        }

        /// <summary>鍵ごとの学習値の一覧 (診断用)。</summary>
        public static IEnumerable<Entry> All => _byKey.Values;
    }
}
