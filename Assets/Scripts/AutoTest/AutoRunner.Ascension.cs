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
    /// <summary>AutoRunner: アセンション (周回) の実行と報告。</summary>
    public partial class AutoRunner
    {
        private IEnumerator RunAscension()
        {
            _ascensionTraces.Clear();
            ResetBatchStatics();
            // 自動獲得を止める。 周回モードは自前で RelicRoller を呼ぶので二重取得になるうえ、
            // 毎ラン PlayerPrefs.Save() が走るとバッチが極端に遅くなる。
            bool prevSuppress = MetaProgression.MetaBuffApplicator.SuppressRelicGrant;
            MetaProgression.MetaBuffApplicator.SuppressRelicGrant = true;

            // 周回モードはペルソナを固定するので、 ラン毎の抽選を止める。
            // itemPickMode は BuildFocused でなければペルソナがアイテム選好に効かないので合わせる。
            var prevPickMode = itemPickMode;
            itemPickMode = ItemPickMode.BuildFocused;
            _ascensionPersonaLock = true;

            int runs = Mathf.Max(10, ascensionRunsPerPersona);
            ResetScreenProgress(AscensionPersonas.Length * runs, "周回モード");
            for (int p = 0; p < AscensionPersonas.Length; p++)
            {
                var persona = AscensionPersonas[p];
                curArmIndex = p + 1; curArmCount = AscensionPersonas.Length;
                curRunsPerArm = runs; curArmLabel = persona.ToString();
                _currentPersona = persona;                       // ライン中は固定
                _ascension = new AscensionLoop(persona, ascensionPolicy, ascensionWindow);
                _ascension.Reset();

                var tr = new AscensionTrace { persona = persona };
                _ascensionTraces.Add(tr);

                for (int i = 0; i < runs; i++)
                {
                    curRunInArm = i + 1;
                    _currentPersona = persona;                   // RunOne が上書きしないよう毎回入れ直す
                    // BeforeRun は RunOne の中 (ResetAll の直後) で呼ぶ。 ここで呼ぶと消される。
                    yield return RunOne(p * runs + i);

                    // **_cur はランの終端で null 化される**ので、 確定済みレコードから読む。
                    var rec = _records.Count > 0 ? _records[_records.Count - 1] : null;
                    int reached = rec != null ? rec.reachedFloor : 1;
                    bool died = rec == null || rec.outcome == Outcome.GameOver;

                    // **死んだ層はクリアしていない。** currentFloor は「死んだ時にいた層」なので、
                    // クリア済みの最深層は 1 つ下になる。 踏破 (GameOver 以外) ならその層まで。
                    int clearedFloor = died ? reached - 1 : reached;
                    // §15-5 の層点は 5 層クリア以降だけが跳ねる。 4 層以下のクリアは死亡と同格なので、
                    // 「5 層以上をクリアしたか」を cleared として渡し、 それ未満は到達層で評価する。
                    bool cleared = clearedFloor >= 5;
                    int layerFloor = cleared ? clearedFloor : reached;
                    _ascension.AfterRun(layerFloor, cleared);

                    tr.score.Add(_ascension.ChallengeScore);
                    tr.util.Add(_ascension.CurrentUtility);
                    tr.floor.Add(reached);
                    tr.win.Add(cleared);

                    if ((i + 1) % Mathf.Max(1, runsPerYield) == 0) yield return null;
                }
                tr.swaps     = _ascension.SwapCount;
                tr.upCount   = _ascension.DifficultyUpCount;
                tr.downCount = _ascension.DifficultyDownCount;
                tr.finalRelic = _ascension.CurrentRelic != null
                              ? _ascension.CurrentRelic.Describe() : "遺物なし";
            }

            _ascension = null;
            _ascensionPersonaLock = false;
            itemPickMode = prevPickMode;
            MetaProgression.MetaBuffApplicator.SuppressRelicGrant = prevSuppress;
            WriteAscensionReport();
        }

        /// <summary>周回モードのレポート。 収束したか / 停滞したか / 発散したかを読めるように、
        /// **前半と後半を分けて**出す（平均だけだと途中の伸びが潰れる）。</summary>
        private void WriteAscensionReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== 周回モード (遺物 §15-5) ===");
            sb.AppendLine($"日時    : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"ポリシー: {ascensionPolicy}  窓幅 {ascensionWindow}  "
                        + $"1ペルソナ {ascensionRunsPerPersona} ラン × {AscensionPersonas.Length} = "
                        + $"{ascensionRunsPerPersona * AscensionPersonas.Length} ラン");
            sb.AppendLine(GameLoop.GameRng.IsSeeded
                ? $"決定論シード: {GameLoop.GameRng.FormatSeed(GameLoop.GameRng.MasterSeed)}" : "シード: 未固定");
            sb.AppendLine();
            // **双方向ラチェット (2026-08-04) 以降、「終」は意味を持たない。**
            //   上げ下げを繰り返すので run1000 時点の値はただのスナップショット。
            //   到達できた難易度を見るなら **最高値**、 落ち着き先を見るなら **後半の中央値**。
            sb.AppendLine("ペルソナ    | 挑戦pt 最高/後半中央/終 | 効用 初→終 | 5層クリア率 前半→後半 | 乗換 難度↑↓");
            sb.AppendLine(new string('-', 104));

            foreach (var tr in _ascensionTraces)
            {
                int n = tr.score.Count;
                if (n == 0) continue;
                int half = Mathf.Max(1, n / 2);
                float winEarly = 0f, winLate = 0f;
                for (int i = 0; i < half; i++)      if (tr.win[i]) winEarly++;
                for (int i = half; i < n; i++)      if (tr.win[i]) winLate++;
                winEarly = winEarly / half * 100f;
                winLate  = winLate / Mathf.Max(1, n - half) * 100f;

                int peak = 0;
                for (int i = 0; i < n; i++) if (tr.score[i] > peak) peak = tr.score[i];
                var lateSorted = new List<int>();
                for (int i = half; i < n; i++) lateSorted.Add(tr.score[i]);
                lateSorted.Sort();
                int lateMed = lateSorted.Count > 0 ? lateSorted[lateSorted.Count / 2] : 0;

                sb.AppendLine($"{tr.persona,-10} |   {peak,3} / {lateMed,3} / {tr.score[n - 1],-3}       | "
                            + $"{tr.util[0],4:F1} → {tr.util[n - 1],-4:F1} | "
                            + $"{winEarly,5:F1}% → {winLate,5:F1}%        | "
                            + $"{tr.swaps,3}  ↑{tr.upCount,2} ↓{tr.downCount,2}");
            }

            // オーバーロード (§23-10) が実際に撃たれているかの確認。
            // バッチ中は Debug.Log が抑止されるので、 数字で残さないと検証できない。
            // 与ダメージの内訳。 **実機の火力がどこから来ているか**を数字で残す。
            // §15-5 の遺物単位を机上の参照状態で較正して 5 倍ずれた経緯があるため (2026-08-03)。
            var db = CombatSystem.CombatManager.DamageBreakdown;
            if (db[0] > 0)
            {
                double n = db[0];
                sb.AppendLine();
                sb.AppendLine("=== 与ダメージの内訳 (1 攻撃あたり平均) ===");
                sb.AppendLine($"  武器素火力 {db[1] / n,6:F1}  攻撃端子の出目 {db[2] / n,6:F1}  "
                            + $"パッシブ加算 {db[3] / n,6:F1}  消費 {db[4] / n,5:F1}  遺物 {db[5] / n,5:F1}");
                sb.AppendLine($"  → atkBase {db[6] / n,6:F1}   最終与ダメ {db[7] / n,6:F1}   "
                            + $"倍率 ×{db[7] / System.Math.Max(1.0, db[6]),5:F2}");
                sb.AppendLine($"  会心率 {db[8] * 100 / n,5:F1}%   固定ダメ {db[9] / n,5:F1}   "
                            + $"追撃 {db[10] / n,5:F1}");
                sb.AppendLine($"  与ダメ倍率の内訳: パッシブ由来 ×{db[11] / n,4:F2} "
                            + $"(+業物 {db[12] / n,4:F2} +メタ {db[13] / n,4:F2} "
                            + $"+遺物 {db[15] / n,4:F2}) "
                            + $"= ×{db[16] / n,4:F2}   最大観測 ×{db[18],4:F2}");
                sb.AppendLine($"  平均 消費GOLD {db[17] / n,6:F0}  "
                            + $"→ 黄金卿の剣を持つなら outgoing +{db[17] / n * 0.01,4:F2} 相当");
                sb.AppendLine($"  **1 ターンあたりの総与ダメ ≒ {(db[7] + db[9]) / n,6:F1}**  "
                            + $"(§15-5 の参照状態が想定していた値: 21.4)");
            }

            // 総倍率の**段別**分解。 会心 × outgoing だけでは総倍率に届かなかったため
            // (残差 ×1.27 の所在不明・2026-08-03)、 修飾チェーンの各段の平均倍率を実測する。
            var st = CombatSystem.CombatManager.DamageStages;
            if (st[0] > 0)
            {
                double sn = st[0];
                string[] label =
                {
                    "A atkBase", "B damageBonus+与ダメパッシブ", "C 追撃加算", "D 会心/鈍器",
                    "E outgoing", "F 鬼火の油+攻撃バースト", "G 練度不足", "H 敵軽減パッシブ",
                    "I 敵の防御値", "J (撤去済み)", "K (撤去済み)", "L 脆弱",
                    "M 最低保証", "N 狂暴化", "O 俊敏/防御スタンス",
                };
                sb.AppendLine();
                sb.AppendLine("=== 総倍率の段別分解 (各段の通過後平均と、 直前段との比) ===");
                for (int i = 1; i <= 15; i++)
                {
                    double cur = st[i] / sn;
                    if (i == 1) { sb.AppendLine($"  {label[0],-24} {cur,8:F2}"); continue; }
                    double prev = st[i - 1] / sn;
                    double ratio = prev > 0.0001 ? cur / prev : 0;
                    string mark = ratio > 1.03 || ratio < 0.97 ? " ★" : "";
                    sb.AppendLine($"  {label[i - 1],-24} {cur,8:F2}   ×{ratio,5:F3}{mark}");
                }
                sb.AppendLine($"  → A から O までの総倍率 ×{st[15] / System.Math.Max(1.0, st[1]),5:F2}");
                double critMul = st[17] > 0 ? st[16] / st[17] : 0;
                sb.AppendLine($"  実測 会心倍率 平均 ×{critMul:F2} (基準 2.00 に固定ではない: メタ精密 r10 等)  "
                            + $"会心 {st[17] * 100 / sn:F1}%");
                sb.AppendLine($"  乗算時点の outgoing 平均 ×{st[22] / sn:F2} (研磨剤込み)");
                sb.AppendLine($"  発動回数: 鈍器 {st[18]:F0} / 研磨剤 {st[19]:F0} / "
                            + $"脆弱 {st[20]:F0} / 狂暴化 {st[21]:F0}   (総攻撃 {sn:F0})");
            }

            // 層 × 敵種別の与ダメ分布。 **全層平均の 137 で敵HPを決めると序盤を壊す**ため、
            // 敵HP の再設計は必ずこの表を根拠にする (2026-08-03)。
            var fd = CombatSystem.CombatManager.FloorDamage;
            var fh = CombatSystem.CombatManager.FloorDamageHist;
            sb.AppendLine();
            sb.AppendLine("=== 層 × 敵種別の 1 攻撃あたり与ダメ (主+固定) ===");
            sb.AppendLine("層 種別    攻撃数    最小   p10   p50   p90   最大    平均 | 敵maxHP  HP/平均 | 平均T");
            string[] kindName = { "通常", "エリ", "ボス" };
            // f=0 は旧 Λ層の枠 (2026-10-28 廃止) で、 いまは何も入らない。
            for (int f = 1; f <= 7; f++)
                for (int k = 0; k < 3; k++)
                {
                    double cnt = fd[f, k, 0];
                    if (cnt < 20) continue;   // 標本が薄い組み合わせは出さない
                    double avg = fd[f, k, 1] / cnt;
                    double ehp = fd[f, k, 4] / cnt;
                    double turns = fd[f, k, 6] > 0 ? fd[f, k, 0] / fd[f, k, 6] : 0;
                    double[] q = new double[3];
                    double[] want = { 0.10, 0.50, 0.90 };
                    int acc = 0, qi = 0;
                    for (int b = 0; b < 80 && qi < 3; b++)
                    {
                        acc += fh[f, k, b];
                        while (qi < 3 && acc >= cnt * want[qi]) q[qi++] = b * 10 + 5;
                    }
                    sb.AppendLine($"{f}F {kindName[k]}  {cnt,8:F0}  {fd[f, k, 2],6:F0}{q[0],6:F0}{q[1],6:F0}"
                                + $"{q[2],6:F0}{fd[f, k, 3],7:F0}  {avg,6:F1} | {ehp,7:F0}  "
                                + $"{(avg > 0 ? ehp / avg : 0),6:F2} | {turns,5:F2}");
                }
            sb.AppendLine("※ HP/平均 = 敵maxHP ÷ 1攻撃の平均与ダメ ＝ **理論上の必要ターン数**。 1.0 未満は 1 撃で消える。");
            sb.AppendLine("※ 平均T = 攻撃回数 ÷ 戦闘数。 ロール敗北のターンは攻撃が無いので実ターン数の下限。");

            // ターン収支。 **敵が手番を得るようになった影響**と、 BOT がブロック端子へ
            // 配線し始めたか (ADR-0009 のジレンマが起動したか) を同時に見る。
            var ts = CombatSystem.CombatManager.TurnStats;
            sb.AppendLine();
            sb.AppendLine("=== 層 × 敵種別の ターン収支 ===");
            sb.AppendLine("層 種別    ターン   被ダメ/T  敵攻撃値  ブロック出目  遮断率 | 配線 攻/防/充  防配線率 | ctxHP runHP  HP残");
            for (int f = 1; f <= 7; f++)
                for (int k = 0; k < 3; k++)
                {
                    double t = ts[f, k, 0];
                    if (t < 20) continue;
                    double atkV = ts[f, k, 6] / t, blkV = ts[f, k, 7] / t;
                    sb.AppendLine($"{f}F {kindName[k]}  {t,8:F0}   {ts[f, k, 5] / t,7:F2}  {atkV,7:F1}  "
                                + $"{blkV,10:F1}  {(atkV > 0 ? blkV / atkV : 0),5:P0} | "
                                + $"{ts[f, k, 2] / t,4:F2}/{ts[f, k, 3] / t,4:F2}/{ts[f, k, 4] / t,4:F2}  "
                                + $"{ts[f, k, 1] * 100 / t,6:F1}% | "
                                + $"{ts[f, k, 9] / t,5:F0} {ts[f, k, 10] / t,5:F0}  "
                                + $"{(ts[f, k, 9] > 0 ? ts[f, k, 8] / ts[f, k, 9] : 0),5:P0}");
                }
            sb.AppendLine("※ 遮断率 = ブロック端子の出目合計 ÷ 敵攻撃値。 1.00 を超えると被ダメ 0。");
            sb.AppendLine("※ 防配線率 = ブロック端子へ 1 本以上配線したターンの割合。 **ADR-0009 のジレンマが");
            sb.AppendLine("   起動しているかの指標** ── 1 ターンで決着していた頃はここが常時 0 に近かった。");

            // **どこで走が終わっているか**。 被ダメ/T が 1 前後しかないのにクリア率が落ちたので、
            // 「削られて死ぬ」以外の終わり方をしている可能性を潰す (2026-08-03)。
            if (_records.Count > 0)
            {
                var byFloor = new int[9];
                var byFloorBoss = new int[9];
                var byCause = new Dictionary<DeathCause, int>();
                var reachDist = new int[9];
                int died = 0;
                foreach (var r in _records)
                {
                    reachDist[Mathf.Clamp(r.reachedFloor, 0, 8)]++;
                    if (r.outcome != Outcome.GameOver) continue;
                    died++;
                    byCause.TryGetValue(r.cause, out int c);
                    byCause[r.cause] = c + 1;
                    int df = Mathf.Clamp(r.deathFloor, 0, 8);
                    byFloor[df]++;
                    if (r.deathInBossFight) byFloorBoss[df]++;
                }
                sb.AppendLine();
                sb.AppendLine($"=== ランの終わり方 (全 {_records.Count} ラン / 死亡 {died}) ===");
                sb.Append("到達層     ");
                for (int i = 1; i <= 8; i++) sb.Append($"{i}F{reachDist[i] * 100.0 / _records.Count,5:F1}% ");
                sb.AppendLine();
                sb.Append("死亡層     ");
                for (int i = 1; i <= 8; i++)
                    sb.Append($"{i}F{(died > 0 ? byFloor[i] * 100.0 / died : 0),5:F1}% ");
                sb.AppendLine();
                sb.Append("うちボス戦 ");
                for (int i = 1; i <= 8; i++)
                    sb.Append($"{i}F{(byFloor[i] > 0 ? byFloorBoss[i] * 100.0 / byFloor[i] : 0),5:F1}% ");
                sb.AppendLine();
                // **層別死亡率** = その層に到達したランのうち、 その層で死んだ割合。
                //   「死亡層」の分布は到達数に引きずられる (到達しなければ死にようがない) ので、
                //   層ごとの危険度を比べるならこちらを見る。 分母は reachedFloor >= i のラン数。
                sb.Append("到達数     ");
                var entered = new int[10];
                foreach (var r in _records)
                    for (int i = 1; i <= Mathf.Clamp(r.reachedFloor, 0, 8); i++) entered[i]++;
                for (int i = 1; i <= 8; i++) sb.Append($"{i}F{entered[i],6} ");
                sb.AppendLine();
                sb.Append("**層別死亡率** ");
                for (int i = 1; i <= 8; i++)
                    sb.Append($"{i}F{(entered[i] > 0 ? byFloor[i] * 100.0 / entered[i] : 0),5:F1}% ");
                sb.AppendLine();
                // 経済。 消費アイテムの価格を「同 Tier アイテムの半額」に直した (2026-08-04) ので、
                // 実際に買われるようになったかを見る。 買われていないなら価格ではなく優先度の問題。
                double gain = 0, fin = 0, peak = 0, buys = 0;
                foreach (var r in _records)
                { gain += r.totalGoldGained; fin += r.finalCoins; peak += r.peakCoins; buys += r.shopPurchases; }
                double rn = _records.Count;
                sb.AppendLine($"GOLD: 総獲得 {gain / rn,6:F1}  ピーク所持 {peak / rn,6:F1}  終了時残 {fin / rn,6:F1}"
                            + $"  → 実消費 {(gain - fin) / rn,6:F1}   ショップ購入 {buys / rn,5:F2} 回/ラン");

                // 最大HP が道中で半分近くまで落ちている件の切り分け (2026-08-04)。
                // 最有力候補は **ラストスタンド** ([LastStand.cs] 発動時に最大HP を半減)。
                // 発動率と発動層が分かれば、 層別の平均最大HP の落ち方と突き合わせられる。
                var lsFloor = new int[9];
                int lsUsed = 0; double finMax = 0;
                foreach (var r in _records)
                {
                    finMax += r.finalMaxHP;
                    if (!r.lastStandUsed) continue;
                    lsUsed++;
                    lsFloor[Mathf.Clamp(r.lastStandFloor, 0, 8)]++;
                }
                sb.Append($"ラストスタンド: 発動 {lsUsed * 100.0 / rn,5:F1}% (最大HP半減)  終了時 最大HP 平均 {finMax / rn,5:F1}"
                        + $"  発動層 ");
                for (int i = 1; i <= 7; i++)
                    sb.Append($"{i}F{(lsUsed > 0 ? lsFloor[i] * 100.0 / lsUsed : 0),5:F1}% ");
                sb.AppendLine();
                sb.Append("死因: ");
                var causes = new List<KeyValuePair<DeathCause, int>>(byCause);
                causes.Sort((a, b) => b.Value.CompareTo(a.Value));
                for (int i = 0; i < causes.Count && i < 8; i++)
                    sb.Append($"{causes[i].Key} {causes[i].Value * 100.0 / System.Math.Max(1, died):F1}%  ");
                sb.AppendLine();
            }

            var rr = CombatSystem.CombatManager.RerollStats;
            sb.AppendLine();
            sb.AppendLine("=== リロール経済 (ADR-0010) ===");
            sb.AppendLine($"  回数 {rr[0]:N0}  振り直したダイス {rr[1]:N0}  消費充電 {rr[2]:N0}"
                        + (rr[0] > 0 ? $"  (1回あたり {rr[1] / (double)rr[0]:F2}個 / {rr[2] / (double)rr[0]:F2}充電)" : ""));
            if (rr[0] == 0)
                sb.AppendLine("  **一度も振り直していない** — 方策か充電収支を確認すること");
            sb.AppendLine();
            sb.AppendLine("=== 役の成立/発動 (ADR-0010 Verification ④) ===");
            sb.AppendLine(CombatSystem.YachtRoleEffects.DescribeStats());
            sb.Append(SuperCombatAI.DescribeSearch());

            sb.AppendLine();
            sb.AppendLine(GameLoop.RunChronicle.DescribeCounts(_ascensionTraces.Count));

            sb.AppendLine();
            sb.AppendLine("=== 各ラインの最終遺物 ===");
            foreach (var tr in _ascensionTraces)
                sb.AppendLine($"  {tr.persona,-10} {tr.finalRelic}");

            // 推移を 10 分割して出す。 収束/停滞/発散の形を目で見るため。
            sb.AppendLine();
            sb.AppendLine("=== 挑戦スコアの推移 (10 分割の各区間末) ===");
            sb.Append("ペルソナ    ");
            for (int k = 1; k <= 10; k++) sb.Append($"{k * 10,5}%");
            sb.AppendLine();
            foreach (var tr in _ascensionTraces)
            {
                sb.Append($"{tr.persona,-10}  ");
                int n = tr.score.Count;
                for (int k = 1; k <= 10; k++)
                {
                    int idx = Mathf.Clamp(n * k / 10 - 1, 0, n - 1);
                    sb.Append($"{tr.score[idx],5}");
                }
                sb.AppendLine();
            }
            sb.AppendLine();
            sb.AppendLine("=== 遺物の効用の推移 (同上) ===");
            foreach (var tr in _ascensionTraces)
            {
                sb.Append($"{tr.persona,-10}  ");
                int n = tr.util.Count;
                for (int k = 1; k <= 10; k++)
                {
                    int idx = Mathf.Clamp(n * k / 10 - 1, 0, n - 1);
                    sb.Append($"{tr.util[idx],5:F1}");
                }
                sb.AppendLine();
            }

            string text = sb.ToString();
            LogAlways(text);
            try
            {
                string dir = ResolveAutoRunOutputRoot();
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(dir, $"ascension_{DateTime.Now:yyyyMMdd_HHmmss}.txt"), text);
            }
            catch (Exception e) { Debug.LogWarning($"[周回モード] レポート保存に失敗: {e.Message}"); }
        }
    }
}
