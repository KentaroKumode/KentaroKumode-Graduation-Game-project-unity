using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace AutoTest.EditorTools
{
    /// <summary>
    /// **学習した序列がどれだけ当たっているかを測る。**
    ///
    /// <para>アブレーション実測 (2026-08-22) で、 ショップの「順位付けだけ」を潰すと
    /// 7層クリアが 32.2% → 20.7% (−11.5pt, p&lt;0.0001) 落ちた ── 学習した序列そのものに
    /// 戦闘技量帯 (+2.5pt・有意差なし) の約 5 倍の価値がある。 では**その序列は正しいのか**。</para>
    ///
    /// <para>突き合わせるのは 2 つ:
    ///   ① <c>LearnedPriorityProvider.BuyScore</c> ── AI が信じている強さ (購入順位の主軸)
    ///   ② <c>ItemAggregate.ExploreLift</c> ── ランダム化ホールドアウトだけから出した**不偏推定**
    /// ②は選択バイアスが無い唯一の量。 通常の lift は「金があり順調なときに買う」ので
    /// 符号すら反転しうる (§ 帯別学習と探索の記録)。</para>
    ///
    /// <para><b>BuyScore を再実装しない。</b> 正本を呼んで値を吐くだけにする ──
    /// 写しを作ると必ず食い違う (役効果表で 3 箇所に増えていた例がある)。</para>
    /// </summary>
    public static class LearnedScoreAudit
    {
        [MenuItem("Tools/AutoRun/学習の精度: BuyScore vs 不偏lift を書き出す", priority = 120)]
        public static void Dump()
        {
            // **BOT が実際に読む根と同じものを使う。** Tier 較正用と BOT 学習用は
            //   ディレクトリが別なので、 ここを取り違えると「賢くならない方」を監査してしまう。
            string root = AutoTest.MetaProfileHelper.BotLearningRoot(0);
            string path = Path.Combine(root, "item_stats.json");
            if (!File.Exists(path))
            {
                Debug.LogError("[学習監査] item_stats が見つからない: " + path);
                return;
            }

            // **本番と同じ経路で読み込ませる。** ここで自前パースすると学習の解釈が二重になる。
            AutoTest.LearnedPriorityProvider.Reload(root, writeMarkdown: false);
            var sf = AutoTest.ItemLearningStats.LoadOrNew(path);
            if (sf == null || sf.items == null || sf.items.Count == 0)
            {
                Debug.LogError("[学習監査] items が空");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("id\tbuyScore\texploreLift\texpAcqRuns\texpNoAcqRuns\tobservedLift\thasLearned");
            int paired = 0;
            foreach (var it in sf.items)
            {
                if (it == null || string.IsNullOrEmpty(it.id)) continue;
                // 両群そろっていない品は不偏推定が作れない (ExploreLift が 0 を返す)
                bool ok = it.expAcqRuns > 0 && it.expNoAcqRuns > 0;
                if (ok) paired++;
                sb.Append(it.id).Append('\t')
                  .Append(AutoTest.LearnedPriorityProvider.BuyScore(it.id).ToString("R")).Append('\t')
                  .Append(ok ? it.ExploreLift.ToString("R") : "").Append('\t')
                  .Append(it.expAcqRuns).Append('\t')
                  .Append(it.expNoAcqRuns).Append('\t')
                  .Append(it.Lift.ToString("R")).Append('\t')
                  .Append(AutoTest.LearnedPriorityProvider.HasLearned(it.id) ? 1 : 0)
                  .AppendLine();
            }

            string outPath = Path.Combine(root, "learned_score_audit.tsv");
            File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
            Debug.LogWarning($"[学習監査] {sf.items.Count} 品 (ホールドアウト両群あり {paired} 品) → {outPath}");
        }
    }
}
