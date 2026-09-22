using System;
using System.Collections.Generic;
using CombatSystem;
using InventorySystem.PassiveSkills;

namespace AutoTest
{
    /// <summary>
    /// ADR-0010 技量帯の **天井**。 配線・リロール・役の 3 判断を、
    /// **今ターンの収支ではなく「この戦闘に勝つ確率」** で選ぶ。
    ///
    /// 既存の <c>MutualWiringPolicy</c> (Optimal) は 1 ターンぶんの効用に ttk の罰を足した
    /// **貪欲**で、 構造的に次の 3 つを扱えない:
    ///   ① 役は 1 戦闘 1 回。 いま切るか温存するかは**残りターン数の関数**である
    ///   ② 充電はリロールの原資。 いま何本充電へ回すかは**将来のリロール価値**で決まる
    ///   ③ エスカレーションは時間の関数。 「3 ターン後に敵攻撃が 1.3 倍になる」ことを
    ///      1 ターンの効用に押し込む手段が無い (ttk の線形罰は近似にすぎない)
    /// これらは**先を読まないと原理的に評価できない**。 だから前向きシミュレーションを持つ。
    ///
    /// **未来予知はしない。** ロールアウトのダイスは本クラス専用の <see cref="System.Random"/>
    /// から引く ── <see cref="GameLoop.GameRng"/> は一切消費しないので、
    ///   (a) 実際にこの後出る目を読むことは原理的にできない
    ///   (b) 本 AI を挟んでもゲーム本体の乱数列がずれない (決定性検査がそのまま通る)
    /// の両方が同時に成り立つ。 ここが「天井であって不正ではない」の境界である。
    ///
    /// **敵の内部状態も読まない。** 見るのは予告 (<see cref="MutualTurnTelegraph"/>) と
    /// 公開ステータスだけ ── これは人間のプレイヤーが盤面から読めるものと同じ。
    ///
    /// ダメージの絶対値は**実測から自己較正する** (<see cref="Observe"/>)。 パッシブ・遺物・
    /// 敵の軽減を個別に再実装すると本体と二重管理になり、 必ず食い違う。
    /// 「自分の一撃がいくら通るか」は人間も数戦で覚えることなので、 観測で埋めるのが筋が通る。
    /// </summary>
    [System.Serializable]
    public sealed class SuperCombatAI
    {
        // ============================================================
        //  レバー (AutoRunner のインスペクタから触る)
        // ============================================================

        /// <summary>候補 1 つあたりのロールアウト本数。 候補間は**共通乱数**なので
        /// 少ない本数でも順位は安定する (差の分散だけを見ているため)。</summary>
        public int rolloutCount = 12;
        /// <summary>深く読む配線候補の数。 全列挙 (243〜1024) を浅い評価で絞ってから回す。</summary>
        public int candidateWirings = 6;
        /// <summary>**浅い評価が最良と同点だらけのとき**だけ候補枠をここまで広げる (0 = 無効)。
        ///
        /// 実測 (2026-08-22 / 0pt 遺物なし n=1000) では **56.8% の判断で上位候補が全て同点**だった。
        /// 同点なら `_candScore` は順位を決められないので、 どの <see cref="candidateWirings"/> 本が
        /// ロールアウトへ進むかは **`BuildCodes` の列挙順**が決めている ── 探索の入口が恣意的。
        /// 広げてもロールアウト本数が増えるだけで、 主判断の向きは変えない。
        ///
        /// **既定 0 (OFF)。** Super は全測定の基準線なので、 立てない限り従来とビット単位で同一。
        /// 立てた測定を既存の基準値と混ぜないこと。</summary>
        public int tieExpandCandidates = 0;
        /// <summary>浅い評価の同点判定の幅。 <see cref="StaticValue"/> は確率 (0〜1.5 程度) を返す。</summary>
        private const float TieEps = 1e-4f;
        /// <summary>**同点の中身を数える** (既定 OFF)。 判断そのものは 1 命令も変えない診断専用。
        ///
        /// 「同点 平均 154.6 本」が
        ///   ① 本当に全部同じ盤面へ着地している (＝盤面が飽和していて選択肢が無い)
        ///   ② 別々の盤面なのに <see cref="StaticValue"/> が同じ値を返している (＝評価の解像度不足)
        /// のどちらなのかを分ける。 ①なら探索側の打ち止めであり、 同時に
        /// **「ゲームが選択肢を出していない」という設計側の問題**になる。
        ///
        /// 同点判断あたり列挙を 1 周増やす。 ロールアウト (候補 × 本数 × 深さ) に比べれば軽い。</summary>
        public bool saturationCensus;
        /// <summary>バッファはどちらの経路でも足りるように最大側で確保する。</summary>
        private int MaxCandidates => Math.Max(1, Math.Max(candidateWirings, tieExpandCandidates));
        /// <summary>ロールアウトの打ち切りターン数。 越えたら決着なしとして部分点で評価する。</summary>
        public int rolloutDepth = 20;
        /// <summary>**生存見込み (ttd) の頭打ちターン数**。 0 で無効 (従来どおり 999 ＝ 実質無限)。
        ///
        /// <para>これが無いと「防げている」局面で勝率 p が 1 に飽和し、
        /// <b>敵HP が評価から消えて削る価値が 0 になる</b>。 同じ関数の中で既に
        /// <c>midTurn</c> の先読みが <c>Math.Min(12f, ttk*0.5f)</c> と 12 ターンで頭を打っているので、
        /// 安全宣言の期限も同じ尺度に合わせるのが自然。</para>
        ///
        /// <para><b>2026-08-22 に 12 で採用。</b> パーツ有り・同一シード・各1000ラン:
        /// 0pt 遺物なし <b>31.1% → 37.7% (p&lt;0.0001)</b> / 0pt 遺物あり <b>70.9% → 74.9% (p=0.0078)</b>。
        /// 内訳も予測どおりの署名を出した ── 7層の攻撃端子出目 22.40 → 30.89 (+37.9%)、
        /// 攻撃回数 19,284 → 16,376 (−15%)、 <b>実効倍率は 5.414 → 5.456 で不変</b>
        /// ＝ 倍率ではなく「攻撃へ載せる出目」が増えた。</para></summary>
        public float ttdCapTurns = 12f;
        /// <summary>ボス戦のロールアウト深さ。 7層の 4 連戦は 40 ターン級なので、
        /// 通常戦と同じ 20 で打ち切ると読みが全部「未決着」に落ちる。</summary>
        public int bossRolloutDepth = 45;
        /// <summary>リロール判断で 1 案あたり振る回数 (共通乱数)。</summary>
        public int rerollSamples = 8;
        /// <summary>**振り直す個数がこれ以下なら全列挙する** (0 = 常に標本抽出)。
        ///
        /// <para>引き直しの全パターンは 面数^個数 ── 6 面なら 1 個で 6 通り、 2 個で 36 通り。
        /// <see cref="rerollSamples"/>=8 の標本より**安いか同程度で、 しかも厳密**。</para>
        ///
        /// <para><b>なぜ効くか。</b> 8 標本では確率 1/36 の引き (例: 1,1,1 から 1 を 2 つ引いて〈極〉)
        /// はまず当たらず、 **構造的に見えていない**。 実測で採用リロールの平均は 1.60 個なので、
        /// いちばん頻繁な判断がいちばん安く厳密化できる。 専用の役狙いロジックを足すより素直で、
        /// 全リロール判断が同時に改善する。</para>
        ///
        /// <para><b>2026-08-22 に 2 で採用。 ただし一度は空振りしている</b> ──
        /// <see cref="ttdCapTurns"/> の飽和を直す**前**に測ったときは
        /// 16.1% → 14.9% (p=0.3420) で**逆向き**だった。 厳密な期待値を計算しても、
        /// 計算する対象 (`TurnValue`→`StaticValue`) が壊れていれば意味が無い。
        /// 直した後に測り直すと符号が反転した:</para>
        ///
        /// <para>0pt 遺物なし seed1 37.7→40.3% / seed2 35.7→38.1%、
        /// <b>2シード併合 260対210 / p=0.0237</b>。 0pt 遺物あり 74.9→76.3% (p=0.2752・同じ向き)。</para>
        ///
        /// <para><b>教訓</b>: null は「効かない」ではなく「**その評価関数の上では効かない**」。
        /// 評価関数を直したら、 それを経由する介入は測り直す。</para></summary>
        public int exactRerollMaxDice = 2;

        /// <summary>引き直しの面の種類数。 <see cref="RollFace"/> と同じ台に立つ。</summary>
        private int FaceCount => _faces != null ? _faces.Length : _diceMax;
        /// <summary>i 番目の面。 **重複した面はそのまま重複させる** ── 出現確率が面の多重度に
        /// 比例するので、 一様に列挙すればそれがそのまま正しい期待値になる。</summary>
        private int FaceAt(int i) => _faces != null ? _faces[i] : (i + 1);
        /// <summary>1 ターンあたりのリロール試行上限。 コストが `個数 × 回数` で逓増する。</summary>
        public int rerollMaxAttempts = 3;
        /// <summary>勝利時の価値に HP 残存率を足す重み。 「勝てば同じ」ではラン全体を落とす。
        ///
        /// <para><b>一度も掃引していない手置き値 (2026-08-22 時点)。</b> 効用は
        /// <c>p × (1 + winHpWeight × HP率)</c> で、 0.5 だと満タン勝利 1.5 対 瀕死勝利 1.0 ＝
        /// **わずか 1.5 倍の開き**しかない。 7層は 4 連戦で、 しかも役札の連戦持ち越しを
        /// 採用した (<see cref="CombatSystem.CombatManager.VescaKeepRolesAcrossPhases"/>) ので、
        /// **HP は形態を跨いで効くのに役札は跨いで減る** ── 残 HP の価値は明確に非線形寄りのはず。</para></summary>
        public float winHpWeight = 0.5f;
        /// <summary>現在ランの外部観測用カウンタ。長い判断が実際に前進しているかを確認する。</summary>
        [NonSerialized] public long wiringDecisions, rerollDecisions, rolloutEvaluations;
        /// <summary>リロール 1 個あたりの出目底上げ (面平均に対する比)。
        ///
        /// 振り直す対象は面平均を下回るダイスなので、 素の差は
        /// E[新] − E[旧｜平均未満] ≈ 面平均 − 0.6×面平均 = 0.4×面平均。
        /// 役を狙って期待値を捨てる振り直しもあるぶん、 控えめに 0.30 で置く。</summary>
        ///
        /// <para><b>掃引可能にした (2026-08-22)。</b> 実測で **リロール判断の 34.6% を
        /// 「利得なし」で断っており、 その差は平均 0.0008** ── 15 万件が紙一重で落ちている。
        /// **下げると充電の価値が下がり、 払うのが安くなってリロールが増える。**
        /// 既定 0.30 は従来どおりなので、 触らない限りビット単位で同一。</para></summary>
        public float chargeRerollGain = DefaultChargeRerollGain;
        /// <summary><b>0.30 のまま。 一度 0.20 を採用して撤回した (2026-08-22)。</b>
        ///
        /// <para>採用時の根拠は「0pt 遺物なしで 17.8% → 21.5% (McNemar p=0.0060)」だったが、
        /// **その測定は出目パーツを抑止したまま走っていた** (`suppressFacePartOffers=true` が
        /// EditorPrefs で持ち越されていた)。 製品条件 (パーツ有り) で測り直すと
        /// **32.2% → 31.1% / p=0.5012 で無差**。 機構側も動かない
        /// (上限破棄 14.7% vs 15.7%、 充電収支 +2.63 vs +2.64/ターン)。</para>
        ///
        /// <para>パーツが高い面 (7〜9) を供給するので充電収入が増え、 この定数を下げた程度では
        /// 「使えない資源への駐車」をやめない。 **0.20 の優位はパーツ無し世界の現象だった。**</para>
        ///
        /// <para><b>教訓</b>: サマリーの `実効トグル:` 行を毎回読むこと。 印字は最初からあった。</para></summary>
        public const float DefaultChargeRerollGain = 0.30f;

        // ============================================================
        //  [計装] 先読みが実際に仕事をしているか (バッチ横断・static)
        // ============================================================
        //  Super は Optimal の上に **ロールアウトのぶんだけ** 乗っている。 その差が
        //  最終的な順位に現れていなければ、 払っている時間は丸ごと無駄である。
        //  「覆した率」と「打ち切り率」の 2 つで、 先読みが効いていない理由を
        //    ① 深く読んでも順位が変わらない (評価関数側の問題)
        //    ② 深く読む前に候補が落ちている (フィルタ側の問題)
        //    ③ 深く読んでも決着しない (打ち切りで点が潰れている)
        //  へ切り分ける。 **ワーカースレッドから触るので Interlocked で積む。**

        /// <summary>ロールアウトまで進んだ配線判断の数 (候補が 2 本以上残ったもの)。</summary>
        public static long SearchDecisions;
        /// <summary>ロールアウトが浅い評価の 1 位を覆した数。 **0 に近ければ先読みは順位を変えていない。**</summary>
        public static long SearchOverturned;
        /// <summary>候補間でロールアウト値に差が付かなかった数。 実質**列挙順**で決まっている。</summary>
        public static long SearchNoSeparation;
        /// <summary>浅い評価で**最良と同点の候補が <see cref="candidateWirings"/> 本より多かった**数。
        /// どの候補をロールアウトへ通すかが列挙順まかせ ＝ フィルタが選別していない。</summary>
        public static long SearchShallowTied;
        /// <summary><see cref="SearchShallowTied"/> のときの同点本数の合計 (平均を出す用)。</summary>
        public static long SearchTiedSum;
        /// <summary>**厳密 float 同値**で最良と並んだ候補が <see cref="candidateWirings"/> を超えた判断の数。
        ///
        /// 挿入ソートは <c>if (v &lt;= _candScore[i]) continue;</c> ＝ **厳密比較**なので、
        /// 1e-7 の差でも順位は正しく付く。 列挙順が効くのは**厳密同値のときだけ**である。
        /// <see cref="SearchShallowTied"/> (eps 1e-4) とは別物なので混同しないこと。</summary>
        public static long SearchExactTied;
        /// <summary>全判断にわたる厳密同値の本数の合計 (平均を出す用)。</summary>
        public static long SearchExactTiedSum;
        /// <summary>同点なので候補枠を実際に広げた数 (<see cref="tieExpandCandidates"/> が有効なときのみ)。</summary>
        public static long SearchExpanded;

        // ---- リロール判断の内訳 ----
        //  実測で **充電収支 +2.35/ターン・収入の 11.3% を上限で破棄**。 余っている理由が
        //    ① 配線が充電端子へ挿しすぎている (供給過剰)
        //    ② リロールを断りすぎている (需要不足)
        //    ③ そもそもリロールに価値が無く、 余剰は正しい
        //  のどれなのかを分ける。 ②なら疑うのは `TurnValue` と `ChargeRerollGain = 0.30`。

        /// <summary><see cref="ChooseReroll"/> の呼び出し回数。</summary>
        public static long RrCalls;
        /// <summary>試行上限 (<see cref="rerollMaxAttempts"/>) で断った数。</summary>
        public static long RrDeclineAttempt;
        /// <summary>最小コストすら払えずに断った数 (充電不足)。</summary>
        public static long RrDeclineAfford;
        /// <summary>**今ターン撃破できるので**断った数 (正しい辞退)。</summary>
        public static long RrDeclineLethal;
        /// <summary>**どの部分集合も基準を超えなかった**ので断った数。 ここが多いなら需要不足。</summary>
        public static long RrDeclineNoGain;
        /// <summary>採用した数。</summary>
        public static long RrAccept;
        /// <summary>採用時の 振り直し個数 / コスト の合計。</summary>
        public static long RrAcceptDiceSum, RrAcceptCostSum;
        /// <summary>入口の充電量の合計と、 入口で上限に張り付いていた数。</summary>
        public static long RrChargeSum, RrAtCap;
        /// <summary>「利得なし」で断ったときの **基準との差** の合計 (×1000)。
        /// 小さければ紙一重で断っている ＝ 評価がわずかに保守的なだけ。</summary>
        public static long RrNoGainDeficitMilli;
        /// <summary>配線が充電端子へ挿したダイス本数の合計と、 その判断数 (供給側の実測)。</summary>
        public static long WireChargeDice, WireChargeDecisions;
        /// <summary>レポート用のダイス本数 (最後に見たスナップショットの値)。</summary>
        public static int KForReport;
        /// <summary>レポート用の <see cref="chargeRerollGain"/>。 **実効状態を必ず印字する** ──
        /// 設定を取り違えたまま測る事故が実際に起きているため。</summary>
        public static float GainForReport = DefaultChargeRerollGain;

        // ---- 飽和センサス (<see cref="saturationCensus"/> 有効時のみ) ----
        /// <summary>センサスを採った同点判断の数。</summary>
        public static long SatDecisions;
        /// <summary>その同点本数の合計。</summary>
        public static long SatTiedTotal;
        /// <summary>同点候補の**相異なる着地点** (自HP/敵HP/充電) の合計。
        /// これが同点本数と比べて極端に小さければ ①飽和、 近ければ ②解像度不足。</summary>
        public static long SatDistinctLanding;
        /// <summary>着地点が **1 種類しか無かった**判断の数 ＝ 純粋に選択肢が無いターン。</summary>
        public static long SatSingleLanding;
        /// <summary>同点候補のうち ブロック合計 ≧ 敵攻撃値 だった数 (超過分が 0 に潰れている)。</summary>
        public static long SatBlockSaturated;
        /// <summary>同点候補のうち 着地時に充電が上限に張り付いていた数。</summary>
        public static long SatChargeCapped;
        /// <summary>同点候補のうち <see cref="StaticValue"/> が「自分は安全」と見積もった数
        /// (<c>hisDps ≈ 0 → ttd = 999 → p が 1 に飽和</c>)。 **このとき戻り値から敵HPが消える。**</summary>
        public static long SatSafeProjected;
        /// <summary>同点候補の**相異なる自HP**の合計。 <see cref="SatDistinctLanding"/> より
        /// 大幅に小さければ「自HPは同じで敵HPだけが違う」＝ 与ダメが評価されていない証拠。</summary>
        public static long SatDistinctPHp;
        /// <summary>ロールアウトが決着まで到達した本数。 [0]=通常戦 [1]=ボス戦。</summary>
        public static readonly long[] SearchRolloutDecided = new long[2];
        /// <summary>深さ上限で打ち切った本数。 打ち切りは最大 0.45 点なので、
        /// 高いと候補が軒並み低い値に固まって**順位が付かなくなる**。 [0]=通常戦 [1]=ボス戦。</summary>
        public static readonly long[] SearchRolloutCapped = new long[2];

        /// <summary>バッチ頭で落とす。 **プロセス横断の static なので、 落とさないと
        /// 前バッチの値が混ざる** (2026-08-10 の _combatSeq と同じ罠)。</summary>
        public static void ResetSearchStats()
        {
            SearchDecisions = SearchOverturned = SearchNoSeparation = SearchShallowTied = 0;
            SearchTiedSum = SearchExpanded = 0;
            SearchExactTied = SearchExactTiedSum = 0;
            SatDecisions = SatTiedTotal = SatDistinctLanding = SatSingleLanding = 0;
            SatBlockSaturated = SatChargeCapped = 0;
            SatSafeProjected = SatDistinctPHp = 0;
            RrCalls = RrDeclineAttempt = RrDeclineAfford = RrDeclineLethal = 0;
            RrDeclineNoGain = RrAccept = RrAcceptDiceSum = RrAcceptCostSum = 0;
            RrChargeSum = RrAtCap = RrNoGainDeficitMilli = 0;
            WireChargeDice = WireChargeDecisions = 0;
            Array.Clear(SearchRolloutDecided, 0, SearchRolloutDecided.Length);
            Array.Clear(SearchRolloutCapped, 0, SearchRolloutCapped.Length);
        }

        /// <summary>[計装] 先読みの実測。 **覆した率が 0 に近ければ、 Super は Optimal に
        /// ロールアウトのコストを足しただけ**という判定になる。</summary>
        public static string DescribeSearch()
        {
            if (SearchDecisions <= 0) return "";
            var sb = new System.Text.StringBuilder();
            double d = SearchDecisions;
            sb.AppendLine("【先読みの実測 (SuperCombatAI)】");
            sb.AppendLine($"  ロールアウトまで進んだ配線判断 {SearchDecisions:N0}");
            sb.AppendLine($"    浅い評価の1位を覆した     {SearchOverturned,10:N0} ({100.0 * SearchOverturned / d,5:F1}%)"
                        + "  ※0%に近いと先読みは順位を変えていない");
            sb.AppendLine($"    候補間に差が付かなかった   {SearchNoSeparation,10:N0} ({100.0 * SearchNoSeparation / d,5:F1}%)"
                        + "  ※列挙順まかせで決まっている");
            sb.AppendLine($"    **厳密同値が候補枠を超えた** {SearchExactTied,10:N0} ({100.0 * SearchExactTied / d,5:F1}%)"
                        + $"  ※厳密同値 平均 {SearchExactTiedSum / d,5:F1} 本"
                        + " — ここだけが本当に列挙順で決まる");
            sb.AppendLine($"    浅い評価が最良同点だらけ  {SearchShallowTied,10:N0} ({100.0 * SearchShallowTied / d,5:F1}%)"
                        + $"  ※同点 平均 {(SearchShallowTied > 0 ? SearchTiedSum / (double)SearchShallowTied : 0):F1} 本"
                        + " — どの候補を残すかが選別されていない");
            if (SearchExpanded > 0)
                sb.AppendLine($"    同点で候補枠を広げた      {SearchExpanded,10:N0} ({100.0 * SearchExpanded / d,5:F1}%)"
                            + "  ※tieExpandCandidates 有効時のみ");
            for (int b = 0; b < 2; b++)
            {
                long dec = SearchRolloutDecided[b], cap = SearchRolloutCapped[b];
                long tot = dec + cap;
                if (tot <= 0) continue;
                sb.AppendLine($"  ロールアウト {(b == 1 ? "ボス戦" : "通常戦")}: {tot,12:N0} 本 / 決着 {dec,12:N0}"
                            + $" / 打ち切り {cap,12:N0} ({100.0 * cap / tot,5:F1}%)"
                            + (b == 1 ? "  ※打ち切りは最大0.45点。高いと順位が潰れる" : ""));
            }
            if (RrCalls > 0)
            {
                double rc = RrCalls;
                sb.AppendLine($"  【リロール判断の内訳】chargeRerollGain={GainForReport:F2}"
                            + (Math.Abs(GainForReport - DefaultChargeRerollGain) > 1e-6f
                               ? " **既定 0.30 から変更あり**" : "")
                            + $" / 呼び出し {RrCalls:N0}"
                            + $" / 入口の平均充電 {RrChargeSum / rc,5:F1}"
                            + $" (上限張り付き {100.0 * RrAtCap / rc,4:F1}%)");
                sb.AppendLine($"    採用          {RrAccept,10:N0} ({100.0 * RrAccept / rc,5:F1}%)"
                            + (RrAccept > 0 ? $"  平均 {RrAcceptDiceSum / (double)RrAccept:F2} 個"
                                            + $" / 充電 {RrAcceptCostSum / (double)RrAccept:F2}" : ""));
                sb.AppendLine($"    辞退: 撃破確定 {RrDeclineLethal,9:N0} ({100.0 * RrDeclineLethal / rc,5:F1}%)"
                            + "  ※正しい辞退");
                sb.AppendLine($"    辞退: 利得なし {RrDeclineNoGain,9:N0} ({100.0 * RrDeclineNoGain / rc,5:F1}%)"
                            + (RrDeclineNoGain > 0
                               ? $"  基準との差 平均 {RrNoGainDeficitMilli / (double)RrDeclineNoGain / 1000.0:F4}" : "")
                            + "  ※ここが多く差が小さいなら評価が保守的");
                sb.AppendLine($"    辞退: 充電不足 {RrDeclineAfford,9:N0} ({100.0 * RrDeclineAfford / rc,5:F1}%)"
                            + $" / 試行上限 {RrDeclineAttempt,9:N0} ({100.0 * RrDeclineAttempt / rc,5:F1}%)");
                if (WireChargeDecisions > 0)
                    sb.AppendLine($"    供給側: 1判断あたり充電端子へ {WireChargeDice / (double)WireChargeDecisions:F2} 個"
                                + $" (全 {KForReport} 個中)");
            }
            if (SatDecisions > 0)
            {
                double sd = SatDecisions, tied = Math.Max(1, SatTiedTotal);
                sb.AppendLine("  【飽和センサス】同点の中身 (判断 " + SatDecisions.ToString("N0") + ")");
                sb.AppendLine($"    同点 平均 {SatTiedTotal / sd,6:F1} 本 / **相異なる着地点 平均 {SatDistinctLanding / sd,5:F1} 種**");
                sb.AppendLine($"    着地点が1種類だけ  {SatSingleLanding,10:N0} ({100.0 * SatSingleLanding / sd,5:F1}%)"
                            + "  ※そのターンは本当に選択肢が無い");
                sb.AppendLine($"    うち相異なる自HP 平均 {SatDistinctPHp / sd,5:F1} 種"
                            + "  ※着地点より大幅に少なければ「自HPは同じで敵HPだけ違う」");
                sb.AppendLine($"    同点候補のうち ブロック≧敵攻撃 {100.0 * SatBlockSaturated / tied,5:F1}%"
                            + $" / 充電が上限 {100.0 * SatChargeCapped / tied,5:F1}%");
                sb.AppendLine($"    **「自分は安全」と見積もった候補 {100.0 * SatSafeProjected / tied,5:F1}%**"
                            + "  ※この経路では p が 1 に飽和し、戻り値から敵HPが消える");
                sb.AppendLine("    ※着地点が同点本数に近い → 評価の解像度不足。 1 に近い → 盤面の飽和");
            }
            return sb.ToString();
        }

        // ============================================================
        //  隔離乱数 (**GameRng を絶対に消費しない**)
        // ============================================================

        private System.Random _rng = new System.Random(0x5EED);
        private int _rolloutSeed;

        /// <summary>ラン開始時に呼ぶ。 内部乱数を固定シードへ戻す ──
        /// 同一シードのラン比較 (paircmp) で本 AI の判断まで再現させるため。</summary>
        public void ResetForRun(int runSeed)
        {
            unchecked { _rolloutSeed = (runSeed * 1103515245 + 12345) & 0x7FFFFFFF; }
            _rng = new System.Random(_rolloutSeed);
            _dmgMul = 1f; _takenMul = 1f; _calSamples = 0;
            _pendingPredAtk = -1; _pendingPredRaw = -1;
            wiringDecisions = rerollDecisions = rolloutEvaluations = 0;
        }

        /// <summary>戦闘方策を外部ルーチンへ切り替える際、前の敵に対する未確定観測を捨てる。
        /// 次にSuperへ戻ったとき、別の敵のHP差を自己較正へ混ぜないため。</summary>
        public void BeginCombat(string enemyId)
        {
            _pendingPredAtk = -1;
            _pendingPredRaw = -1;
            _obsEnemyId = enemyId;
            _obsTurns = _obsHalve = _obsExecute = _obsShieldTurns = _obsBlaze = 0;
            _obsShieldSum = _obsDodgeSum = _obsReflectSum = 0f;
        }

        // ============================================================
        //  実測からの自己較正
        // ============================================================

        /// <summary>「攻撃端子合計 + 素火力」1 に対して実際に敵 HP がいくら減るか。</summary>
        private float _dmgMul = 1f;
        /// <summary>「敵攻撃値 − ブロック合計」1 に対して実際に自 HP がいくら減るか。</summary>
        private float _takenMul = 1f;
        private int _calSamples;
        private int _pendingPredAtk = -1, _pendingPredRaw = -1;
        private int _pendingEnemyHp, _pendingPlayerHp;

        /// <summary>[計装] 較正値の現在地。 レポートで「AI が自分をどう見積もっているか」を出す用。</summary>
        /// <summary>較正済みの与ダメ倍率。 ラン全体の戦力見積もり (<see cref="RunPowerBudget"/>) が使う。</summary>
        public float DmgMul => _dmgMul;

        public string DescribeCalibration()
            => $"与ダメ倍率 {_dmgMul:F2} / 被ダメ倍率 {_takenMul:F2} (標本 {_calSamples})";

        /// <summary>前ターンの予測と実測を突き合わせて倍率を更新する。 配線判断の冒頭で呼ぶ。
        ///
        /// 出血・毒・反射のような「攻撃値に乗らないチャネル」もまとめて倍率へ吸わせている。
        /// 個別に持つと本体の変更に追従できず、 静かに古くなるため。</summary>
        private void Observe(CombatManager cm)
        {
            if (cm == null || _pendingPredAtk < 0) return;
            const float Alpha = 0.25f;

            if (_pendingPredAtk > 0)
            {
                int lost = Math.Max(0, _pendingEnemyHp - cm.EnemyHP);
                float r = lost / (float)_pendingPredAtk;
                if (r > 0.01f && r < 8f) { _dmgMul += (r - _dmgMul) * Alpha; _calSamples++; }
            }
            if (_pendingPredRaw > 0)
            {
                int lost = Math.Max(0, _pendingPlayerHp - cm.PlayerHP);
                float r = lost / (float)_pendingPredRaw;
                if (r >= 0f && r < 8f) _takenMul += (r - _takenMul) * Alpha;
            }
            _dmgMul = Clamp(_dmgMul, 0.2f, 6f);
            _takenMul = Clamp(_takenMul, 0f, 4f);
            _pendingPredAtk = -1; _pendingPredRaw = -1;
        }

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);

        private static int PopCount(int v)
        {
            int n = 0; while (v != 0) { v &= v - 1; n++; }
            return n;
        }

        // ============================================================
        //  盤面スナップショット (読み取り専用・毎ターン更新)
        // ============================================================

        private int _k;                       // ダイス本数
        private int _pMaxHp, _pAtkPower;
        private int[] _faces;                 // 装備ダイスの面。 null なら 1..._diceMax の一様
        private int _diceMax;
        private float _meanFace;
        private float _critBase, _critMul;
        private int _eBaseAtk, _eDiceCount, _eRollMax, _eMaxHp;

        /// <summary>2 体戦 (2026-08-28) の 2 体目の攻撃値。 0 = 単体戦 or 撃破済み。
        /// 毎ターン予告から取り直すので、 2 体目が落ちれば自然に 0 へ戻る。</summary>
        private int _secondAtk;
        private string _escProfile;
        /// <summary>大技サイクル (EnemyData.heavyPeriod)。 **公開規則なのでロールアウトで正確に再現する** ──
        /// 先の出目は知りようがないが、 何ターン目に大技が来るかは盤面に出ている (予知ではない)。</summary>
        private int _heavyPeriod;
        private float _heavyMul = 1f, _heavyWindupMul = 1f;
        private float HeavyAt(int turn)
        {
            if (_heavyPeriod <= 0) return 1f;
            return (turn > 0 && turn % _heavyPeriod == 0) ? _heavyMul : _heavyWindupMul;
        }
        /// <summary>1 周期の平均倍率 (静的評価用)。</summary>
        private float HeavyAvg => _heavyPeriod <= 0 ? 1f
            : ((_heavyPeriod - 1) * _heavyWindupMul + _heavyMul) / _heavyPeriod;
        private bool _suppressTerm, _isBoss;
        private int _sealedRoles;             // 〈凶運〉封印された役のビットマスク
        private int _termCap, _specialLimit, _chargeMax, _chargePerTurn;
        private SpecialTerminalDef _spec;
        private int _pierceMul;               // 〈貫きの錐〉: 敵攻撃値 1 につきブロックを削る量
        private int _sealPeriod;              // 〈綻び〉: 端子封印の周期 (0 = 無効)
        private int _sealedNow = -1;          // 今ターン封印されている端子 (-1 = なし)

        private bool Snapshot(CombatManager cm, int k)
        {
            var ctx = PassiveSkillManager.Instance?.Context;
            var enemy = cm?.CurrentEnemy;
            if (ctx == null || enemy == null || k <= 0) return false;

            _k = k;
            KForReport = k;
            GainForReport = chargeRerollGain;
            _pMaxHp = Math.Max(1, cm.PlayerMaxHP);
            _pAtkPower = Math.Max(0, cm.PlayerAttackPower);
            _faces = (ctx.equippedDiceFaces != null && ctx.equippedDiceFaces.Length > 0)
                   ? ctx.equippedDiceFaces : null;
            _diceMax = Math.Max(1, ctx.playerDiceMax);
            if (_faces != null)
            {
                long s = 0; for (int i = 0; i < _faces.Length; i++) s += _faces[i];
                _meanFace = s / (float)_faces.Length;
            }
            else _meanFace = (_diceMax + 1) / 2f;

            _critBase = Clamp(cm.PlayerCritBaseRate + ctx.critRatePctAdd, 0f, 1f);
            _critMul = Math.Max(1f, ctx.criticalMultiplier);
            _eBaseAtk = enemy.EffectiveBaseAttack;
            _eDiceCount = Math.Max(0, enemy.EffectiveRollCount);
            _eRollMax = Math.Max(1, enemy.EffectiveRollMax);
            _eMaxHp = Math.Max(1, enemy.maxHP);
            _escProfile = enemy.EffectiveEscalationProfile;
            _heavyPeriod = enemy.heavyPeriod;
            _heavyMul = enemy.heavyMul > 0f ? enemy.heavyMul : 1f;
            _heavyWindupMul = enemy.heavyWindupMul > 0f ? enemy.heavyWindupMul : 1f;
            _suppressTerm = ctx.suppressTerminalRoles;
            _sealedRoles = ctx.sealedRoleMask;   // T4-C〈凶運〉ラン単位の封印
            var run = GameLoop.GameManager.Instance?.Run;
            _isBoss = !string.IsNullOrEmpty(ctx.bossId);
            _spec = SpecialTerminals.Equipped(run);
            _specialLimit = SpecialTerminals.EquippedLimit(run);
            _termCap = 4;   // 〈不器用〉は端子の種類を縛らなくなった (2026-08-10 リワーク)
            _sealPeriod = MetaProgression.MetaDebuffApplicator.GetTerminalSealPeriod();
            _chargeMax = CombatManager.ChargeMax;
            _chargePerTurn = MetaProgression.Relics.RelicApplicator.GetChargePerTurn(run, ctx);
            _pierceMul = ctx.playerBlockIgnored ? CombatManager.PierceAwlPerAttack : 0;

            EnsureBuffers();
            return true;
        }

        // ============================================================
        //  シミュレーション状態
        // ============================================================

        /// <summary>先読み中の盤面。 **戦闘 1 回ぶんで閉じている** ── ラン側 (所持金・アイテム) は
        /// この戦闘の勝敗と HP 残量にしか効かないので、 終端評価へ畳んである。</summary>
        private struct SimState
        {
            public int pHp, eHp, turn, charge;
            public int usedMask;          // 使用済み役のビットマスク (RoleKind は 16 種)
            public bool stunNext;         // 〈大束〉: 次ターン敵は攻撃しない
            public bool freeRerollNext;   // 〈中階〉: 次ターンのリロール 1 回無料
            public int blazeStacks;       // 烈炎: ターン開始時に受ける軽減無視ダメージ
        }

        // ============================================================
        //  ヴェスカ抽選の頻度学習 (§13-4)
        // ============================================================
        //  予告は**そのターンの抽選結果**しか教えない。 先のターンに何が引かれるかは
        //  人間にも分からないので、 **この戦闘で見た頻度**から専用乱数で引き直す。
        //  戦闘をまたいで持ち越さない (ボスごとに引きの性質が違うため)。

        private int _obsTurns, _obsHalve, _obsExecute, _obsShieldTurns, _obsBlaze;
        private float _obsShieldSum, _obsDodgeSum, _obsReflectSum;
        private string _obsEnemyId;

        private void ObserveTelegraph(MutualTurnTelegraph tele, string enemyId)
        {
            if (_obsEnemyId != enemyId || tele.turn <= 1)
            {
                _obsEnemyId = enemyId;
                _obsTurns = _obsHalve = _obsExecute = _obsShieldTurns = _obsBlaze = 0;
                _obsShieldSum = _obsDodgeSum = _obsReflectSum = 0f;
            }
            _obsTurns++;
            if (tele.enemyHalvesDamageThisTurn) _obsHalve++;
            if (tele.executeArmed) _obsExecute++;
            if (tele.blazePenalizesBlock) _obsBlaze++;
            if (tele.enemyShield > 0)
            {
                _obsShieldTurns++;
                _obsShieldSum += tele.enemyShield;
                _obsReflectSum += tele.enemyShieldReflectRate;
            }
            _obsDodgeSum += tele.enemyDodgeChance;
        }

        /// <summary>今ターンの予告を、 そのまま先読みの修飾へ写す。 **完全情報の範囲**。</summary>
        private TurnMods ModsFromTelegraph(MutualTurnTelegraph tele) => new TurnMods
        {
            halveDamage = tele.enemyHalvesDamageThisTurn,
            damageTakenMul = tele.enemyDamageTakenMul,
            dodgeChance = tele.enemyDodgeChance,
            shield = tele.enemyShield,
            shieldReflectRate = tele.enemyShieldReflectRate,
            executeArmed = tele.executeArmed,
            pierce = tele.playerBlockIgnored ? CombatManager.PierceAwlPerAttack : 0,
            blazePenalizesBlock = tele.blazePenalizesBlock,
        };

        /// <summary>ロールアウトの先のターン用。 **観測した頻度から引き直す**。
        /// 抽選が無いボス (通常敵・6層以下) では全部 0 になるので、 何も足さない。</summary>
        private TurnMods SampleMods()
        {
            if (_obsTurns <= 0) return default;
            float inv = 1f / _obsTurns;
            var m = new TurnMods();
            if (_obsHalve > 0 && _rng.NextDouble() < _obsHalve * inv) m.halveDamage = true;
            if (_obsExecute > 0 && _rng.NextDouble() < _obsExecute * inv) m.executeArmed = true;
            if (_obsBlaze > 0 && _rng.NextDouble() < _obsBlaze * inv) m.blazePenalizesBlock = true;
            if (_obsShieldTurns > 0 && _rng.NextDouble() < _obsShieldTurns * inv)
            {
                m.shield = (int)(_obsShieldSum / _obsShieldTurns);
                m.shieldReflectRate = _obsReflectSum / _obsShieldTurns;
            }
            m.dodgeChance = _obsDodgeSum * inv;    // 回避は期待値でならす (0/1 の抽選ではないため)
            m.pierce = _pierceMul;                 // 錐は装備由来なので戦闘中ずっと続く
            return m;
        }

        private const int Ongoing = 0, Win = 1, Loss = 2;

        /// <summary>そのターンだけの盤面修飾。 7層ヴェスカ〈遺物学者〉の抽選 (§13-4) と
        /// 烈炎の税を先読みへ通すための入れ物。
        ///
        /// **中身はすべて予告 (<see cref="MutualTurnTelegraph"/>) に開示されているものだけ。**
        /// 敵の内部状態は読まない ── 人間が盤面から読めるものと同じ範囲に留める。
        /// ロールアウトの先のターンは抽選結果を知りようがないので、
        /// **この戦闘で観測した頻度**から専用乱数で引き直す (<see cref="SampleMods"/>)。</summary>
        private struct TurnMods
        {
            public bool halveDamage;        // 天与の盾: このターンの与ダメ半減
            public float damageTakenMul;    // 旧経路 (倍率)。 0 なら未使用
            public float dodgeChance;       // 解析演算: 与ダメを確率で回避
            public int shield;              // シールド: 与ダメを先に吸う
            public float shieldReflectRate; // 吸われた分 × これが自 HP へ返る
            public bool executeArmed;       // 処刑人の烙印: 攻撃後 HP が 20% 以下なら即死
            public int pierce;              // 貫きの錐: 敵攻撃値 1 につきブロックを削る量
            public bool blazePenalizesBlock;// 烈炎: ブロックへ厚く挿すとスタックが増える
        }

        // 使い回しバッファ (243〜1024 通りを回すので毎回 new すると GC を踏む)
        private DiceTerminal[] _assign, _best, _rolloutW;
        private int[] _grpA, _grpB, _simDice, _sortIdx, _keyBuf, _trialBuf;
        /// <summary><see cref="TurnValue"/> のメモ。 <see cref="ChooseReroll"/> の 1 呼び出しで閉じる。</summary>
        private readonly Dictionary<long, float> _tvMemo = new Dictionary<long, float>(512);
        private readonly List<RoleKind> _roleBuf = new List<RoleKind>(24);
        private readonly List<RoleKind> _handBuf = new List<RoleKind>(8);
        private readonly bool[] _termSeen = new bool[4];
        private float[] _candScore;
        private DiceTerminal[][] _candW;

        private void EnsureBuffers()
        {
            if (_assign == null || _assign.Length != _k)
            {
                _assign = new DiceTerminal[_k];
                _best = new DiceTerminal[_k];
                _rolloutW = new DiceTerminal[_k];
                _grpA = new int[_k];
                _grpB = new int[_k];
                _simDice = new int[_k];
                _sortIdx = new int[_k];
                _keyBuf = new int[_k];
                _valSort = new int[_k];
                _trialBuf = new int[_k];
                _codesKey = -1; _valSortKey = -1;
                _handKey = 0;              // ダイス本数が変わったら手札役のメモは無効
                _tvMemo.Clear();
            }
            // **同点拡張の上限側で確保する** ── 枠を広げたときに書き込み先が足りないと踏む。
            int m = MaxCandidates;
            if (_candW == null || _candW.Length != m)
            {
                _candW = new DiceTerminal[m][];
                _candScore = new float[m];
                for (int i = 0; i < m; i++) _candW[i] = new DiceTerminal[_k];
            }
            else if (_candW[0].Length != _k)
            {
                for (int i = 0; i < m; i++) _candW[i] = new DiceTerminal[_k];
            }
        }

        // ============================================================
        //  ① 配線: 全列挙 → 浅い評価で絞る → ロールアウトで決める
        // ============================================================

        /// <summary><see cref="CombatManager.WiringPolicy"/> へ差す。</summary>
        /// <summary>ゴースト (出目パーツ T3/T4) の接続先。 **貪欲パス中だけ非 null**。
        /// 列挙とロールアウトでは null なので、 探索の計算量に一切影響しない。</summary>
        private int[] _ghostNow;

        /// <summary>実体を決めてから、 ゴーストを**浅い評価だけ**で貪欲に足す。
        ///
        /// <para><b>ロールアウトは回さない。</b> コストは 端子数 × ゴースト可能なダイス数 の線形で、
        /// 上限 20 回の <see cref="ApplyTurn"/> ── 候補 12 本 × ロールアウト n 回に比べれば無視できる。
        /// **ゴースト可能なダイスが 0 本なら入口で return** するので、
        /// パーツ未取得の間は追加コストが厳密に 0。</para>
        ///
        /// <para><b>近似であることを明記しておく。</b> 実体の選択はゴーストを知らないまま行われる
        /// (ロールアウトが素の配線しか見ない)。 ゴーストは純粋な加算で、 どの実体配線を選んでも
        /// 「余った接続を一番効く端子へ流す」形になるので、 実体の順位はほぼ動かないと踏んでいる。
        /// 外れていた場合は**ゴースト込みで列挙し直す**しかないが、 それは速度制約に触る。</para></summary>
        public WiringPlan ChooseWiringPlan(int[] rolls, MutualTurnTelegraph tele)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            try { return ChooseWiringPlanCore(rolls, tele); }
            finally { ProfWiringTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0; ProfWiringCalls++; }
        }

        // ---- [計装 2026-09-19] 判断ごとの所要時間。 並列 worker で 1 ラン 7 秒かかる原因を切り分ける ----
        public static long ProfWiringTicks, ProfWiringCalls, ProfRerollTicks, ProfRerollCalls, ProfApplyTurn;
        public static void ResetProf() { ProfWiringTicks = ProfWiringCalls = ProfRerollTicks = ProfRerollCalls = ProfApplyTurn = 0; }
        public static string DescribeProf()
        {
            double f = System.Diagnostics.Stopwatch.Frequency;
            return $"[Super計装] 配線 {ProfWiringCalls} 回 計 {ProfWiringTicks / f:F1}s (平均 {1000 * ProfWiringTicks / f / Math.Max(1, ProfWiringCalls):F2}ms)"
                 + $" / リロール {ProfRerollCalls} 回 計 {ProfRerollTicks / f:F1}s (平均 {1000 * ProfRerollTicks / f / Math.Max(1, ProfRerollCalls):F2}ms)"
                 + $" / ApplyTurn {ProfApplyTurn:N0} 回";
        }

        private WiringPlan ChooseWiringPlanCore(int[] rolls, MutualTurnTelegraph tele)
        {
            var main = ChooseWiring(rolls, tele);
            if (main == null) return WiringPlan.Of(null);

            var ctx = PassiveSkillManager.Instance?.Context;
            var tiers = ctx?.equippedFaceTiers;
            var fidx = ctx?.playerDiceFaceIdx;
            if (tiers == null || fidx == null) return WiringPlan.Of(main);

            int[] order = null, cand = null;
            int cnt = 0;
            for (int i = 0; i < _k && i < fidx.Length; i++)
                if (GameLoop.DiceFaceParts.AllowsDualLink(tiers, fidx[i])) cnt++;
            if (cnt == 0) return WiringPlan.Of(main);   // 0 本 = 何もしない

            cand = new int[_k];
            for (int i = 0; i < _k; i++) cand[i] = WiringPlan.NoGhost;
            order = new int[cnt];
            int oi = 0;
            for (int i = 0; i < _k && i < fidx.Length; i++)
                if (GameLoop.DiceFaceParts.AllowsDualLink(tiers, fidx[i])) order[oi++] = i;
            // 出目の大きい順 (頭打ちのある評価では大きい寄与から埋める方が取りこぼしが小さい)
            Array.Sort(order, (x, y) => rolls[y].CompareTo(rolls[x]));

            var cm = CombatManager.Instance;
            var s0 = new SimState
            {
                pHp = cm.PlayerHP, eHp = cm.EnemyHP, turn = tele.turn,
                charge = ctx.GetCharge(), usedMask = MaskOf(ctx.usedRoles),
                stunNext = false, freeRerollNext = false,
            };
            int enemyAtk = Math.Max(0, tele.enemyAttackValue);
            // 2 体戦の 2 パケット目。 予告 (柱3) で開示済みなので、 方策はこれを見て配線できる。
            _secondAtk = Math.Max(0, tele.secondaryAttackValue);
            int atkBase = Math.Max(0, _pAtkPower - Math.Max(0, tele.playerAttackPenalty));
            int termKinds = _spec != null ? 4 : 3;

            float Shallow()
            {
                var s = s0;
                int outcome = ApplyTurn(ref s, rolls, enemyAtk, main, atkBase, _nowMods);
                return outcome == Ongoing ? StaticValue(s) : Terminal(outcome, s);
            }

            _ghostNow = cand;
            foreach (int i in order)
            {
                bool canStack = GameLoop.DiceFaceParts.AllowsSameTerminalStack(tiers, fidx[i]);
                int bestG = WiringPlan.NoGhost;
                float bestV = Shallow();
                for (int t = 0; t < termKinds; t++)
                {
                    if ((int)main[i] == t && !canStack) continue;   // T3 は同一端子へ重ねられない
                    cand[i] = t;
                    float v = Shallow();
                    if (v > bestV) { bestV = v; bestG = t; }
                }
                cand[i] = bestG;
            }
            _ghostNow = null;

            return new WiringPlan { main = main, ghost = cand };
        }

        public DiceTerminal[] ChooseWiring(int[] rolls, MutualTurnTelegraph tele)
        {
            wiringDecisions++;
            var cm = CombatManager.Instance;
            if (rolls == null || rolls.Length == 0 || cm == null) return null;
            Observe(cm);
            if (!Snapshot(cm, rolls.Length)) return null;
            ObserveTelegraph(tele, cm.CurrentEnemy != null ? cm.CurrentEnemy.id : null);
            _nowMods = ModsFromTelegraph(tele);
            _sealedNow = tele.sealedTerminal;

            var ctx = PassiveSkillManager.Instance.Context;
            var s0 = new SimState
            {
                pHp = cm.PlayerHP,
                eHp = cm.EnemyHP,
                turn = tele.turn,
                charge = ctx.GetCharge(),
                usedMask = MaskOf(ctx.usedRoles),
                stunNext = false,
                freeRerollNext = false,
            };

            int enemyAtk = Math.Max(0, tele.enemyAttackValue);
            // 2 体戦の 2 パケット目。 予告 (柱3) で開示済みなので、 方策はこれを見て配線できる。
            _secondAtk = Math.Max(0, tele.secondaryAttackValue);
            int atkBase = Math.Max(0, _pAtkPower - Math.Max(0, tele.playerAttackPenalty));

            // --- 全列挙して浅く評価し、 上位 candidateWirings 本だけ残す ---
            int m = Math.Max(1, candidateWirings);
            for (int i = 0; i < m; i++) _candScore[i] = float.NegativeInfinity;

            BuildCodes();
            PrepareHand(rolls);
            // 最良と同点の候補が何本あるか。 **フィルタが選別できていない量**そのもの。
            //   流し読みで数えるので、 途中でより良い値が出たら数え直す (上書きリセット)。
            float topShallow = float.NegativeInfinity;
            int tiedForTop = 0;
            // **厳密同値**も同時に数える。 フィルタ (挿入ソート) は厳密比較なので、
            //   列挙順が実際に効くのはこちらであって eps 同点ではない。 コストは比較 2 回。
            float exactTop = float.NegativeInfinity;
            int exactTied = 0;
            for (int ci = 0; ci < _codes.Length; ci++)
            {
                var _assign = _codes[ci];
                if (!Canonical(_assign, rolls)) continue;   // 同値の入れ替えは同一結果
                if (UsesSealed(_assign)) continue;          // 〈綻び〉封印された端子

                var s = s0;
                int outcome = ApplyTurn(ref s, rolls, enemyAtk, _assign, atkBase, _nowMods);
                float v = Terminal(outcome, s);
                if (outcome == Ongoing) v = StaticValue(s);

                if (v > topShallow + TieEps) { topShallow = v; tiedForTop = 1; }
                else if (v >= topShallow - TieEps) { tiedForTop++; if (v > topShallow) topShallow = v; }

                if (v > exactTop) { exactTop = v; exactTied = 1; }
                else if (v == exactTop) exactTied++;

                // 挿入ソートで上位 m 本を保持する (全候補を配列に溜めない)
                for (int i = 0; i < m; i++)
                {
                    if (v <= _candScore[i]) continue;
                    for (int j = m - 1; j > i; j--)
                    {
                        _candScore[j] = _candScore[j - 1];
                        Array.Copy(_candW[j - 1], _candW[j], _k);
                    }
                    _candScore[i] = v;
                    Array.Copy(_assign, _candW[i], _k);
                    break;
                }
            }
            if (_candScore[0] == float.NegativeInfinity) return null;

            // --- 同点だらけなら候補枠を広げる (既定 OFF) ---
            //   同点は「等価」ではなく **浅い評価の解像度が足りていない**だけなので、
            //   ここで 6 本に絞ると探索の入口が列挙順で決まってしまう。
            System.Threading.Interlocked.Add(ref SearchExactTiedSum, exactTied);
            if (exactTied > m) System.Threading.Interlocked.Increment(ref SearchExactTied);

            int use = m;
            if (tiedForTop > m)
            {
                System.Threading.Interlocked.Increment(ref SearchShallowTied);
                System.Threading.Interlocked.Add(ref SearchTiedSum, tiedForTop);
                if (saturationCensus) Census(rolls, enemyAtk, atkBase, s0, topShallow);
                if (tieExpandCandidates > m)
                {
                    use = CollectTied(rolls, enemyAtk, atkBase, s0, topShallow,
                                      Math.Min(tiedForTop, tieExpandCandidates), tiedForTop);
                    if (use > m) System.Threading.Interlocked.Increment(ref SearchExpanded);
                }
            }

            // --- 上位候補をロールアウトで深く評価する ---
            //   **共通乱数**: 候補ごとに同じ種から振り直す。 12 本しか回さなくても
            //   「同じ引きを与えたらどちらが強いか」の比較になるので順位が安定する。
            int nCand = 0;
            for (int i = 0; i < use; i++) if (_candScore[i] > float.NegativeInfinity) nCand++;
            if (nCand <= 1) { Array.Copy(_candW[0], _best, _k); Remember(cm, s0, rolls, _best, enemyAtk, atkBase); return _best; }

            int seed = NextSeed();
            int n = Math.Max(1, rolloutCount);
            var vals = new float[nCand];
            SyncWorkers(nCand);

            // **候補ごとに同じ種から振り直す** (共通乱数) ので、 並列に走らせても
            // 結果は逐次実行と 1 ビットも変わらない。 書き込み先も候補ごとに別。
            System.Threading.Tasks.Parallel.For(0, nCand,
                new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = Parallelism(nCand) },
                i =>
                {
                    var w = (_workers != null && i < _workers.Length) ? _workers[i] : this;
                    w._rng = new System.Random(seed);
                    float acc = 0f;
                    for (int r = 0; r < n; r++)
                    {
                        var s = s0;
                        int outcome = w.ApplyTurn(ref s, rolls, enemyAtk, _candW[i], atkBase, w._nowMods);
                        acc += (outcome != Ongoing) ? w.Terminal(outcome, s) : w.Rollout(s);
                    }
                    vals[i] = acc / n;
                });
            rolloutEvaluations += (long)nCand * n;

            float bestV = float.NegativeInfinity; int bestI = 0;
            for (int i = 0; i < nCand; i++)
                if (vals[i] > bestV) { bestV = vals[i]; bestI = i; }

            // --- [計装] 先読みが浅い評価を覆したか ---
            //   _candW[0] は挿入ソートの結果なので **浅い評価の 1 位**。 bestI != 0 は
            //   「ロールアウトが別の配線を選んだ」を意味する (候補はすべて相異なる割り当て)。
            //   差が付かなかった場合は `>` が更新しないので bestI == 0 に落ちる ──
            //   それを「覆さなかった」と数えると先読みを過小評価するので、 先に分離を見る。
            {
                float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
                for (int i = 0; i < nCand; i++)
                {
                    if (vals[i] < lo) lo = vals[i];
                    if (vals[i] > hi) hi = vals[i];
                }
                System.Threading.Interlocked.Increment(ref SearchDecisions);
                if (hi - lo <= TieEps) System.Threading.Interlocked.Increment(ref SearchNoSeparation);
                else if (bestI != 0) System.Threading.Interlocked.Increment(ref SearchOverturned);
            }

            Array.Copy(_candW[bestI], _best, _k);
            Remember(cm, s0, rolls, _best, enemyAtk, atkBase);
            return _best;
        }

        /// <summary>同点候補が**どこへ着地しているか**を数える。 <see cref="saturationCensus"/> 専用で、
        /// 判断そのものには一切触らない (読むだけ・戻り値なし)。
        ///
        /// <para>同点本数に対して**相異なる着地点が 1 に近ければ、 それらは本当に等価**
        /// ── 盤面が飽和していて限界のダイスに使い道が無い、 という結論になる。
        /// 逆に着地点が同点本数に近ければ、 別々の盤面を <see cref="StaticValue"/> が
        /// 同じ値へ潰しているだけで、 直すべきは評価関数の側。</para></summary>
        private void Census(int[] rolls, int enemyAtk, int atkBase, SimState s0, float top)
        {
            if (_censusSet == null) _censusSet = new HashSet<long>();
            if (_censusHp == null) _censusHp = new HashSet<int>();
            _censusSet.Clear();
            _censusHp.Clear();
            int tied = 0, blockSat = 0, chargeCap = 0, safeProj = 0;
            for (int ci = 0; ci < _codes.Length; ci++)
            {
                var a = _codes[ci];
                if (!Canonical(a, rolls)) continue;
                if (UsesSealed(a)) continue;

                var s = s0;
                int outcome = ApplyTurn(ref s, rolls, enemyAtk, a, atkBase, _nowMods);
                _lastSafeProjected = false;
                float v = outcome == Ongoing ? StaticValue(s) : Terminal(outcome, s);
                if (v < top - TieEps) continue;

                tied++;
                if (outcome == Ongoing && _lastSafeProjected) safeProj++;
                _censusHp.Add(s.pHp);
                // 素のブロック合計。 〈貫きの錐〉の減算前なので「どれだけ挿したか」の意味。
                int bSum = 0;
                for (int i = 0; i < _k; i++) if (a[i] == DiceTerminal.Block) bSum += rolls[i];
                if (bSum >= enemyAtk) blockSat++;
                if (s.charge >= _chargeMax) chargeCap++;

                long key = ((long)Math.Max(0, Math.Min(65535, s.pHp)) << 32)
                         | ((long)Math.Max(0, Math.Min(65535, s.eHp)) << 16)
                         | (long)Math.Max(0, Math.Min(65535, s.charge));
                _censusSet.Add(key);
            }
            if (tied <= 0) return;
            System.Threading.Interlocked.Increment(ref SatDecisions);
            System.Threading.Interlocked.Add(ref SatTiedTotal, tied);
            System.Threading.Interlocked.Add(ref SatDistinctLanding, _censusSet.Count);
            System.Threading.Interlocked.Add(ref SatBlockSaturated, blockSat);
            System.Threading.Interlocked.Add(ref SatChargeCapped, chargeCap);
            System.Threading.Interlocked.Add(ref SatSafeProjected, safeProj);
            System.Threading.Interlocked.Add(ref SatDistinctPHp, _censusHp.Count);
            if (_censusSet.Count <= 1) System.Threading.Interlocked.Increment(ref SatSingleLanding);
        }

        private HashSet<long> _censusSet;
        private HashSet<int> _censusHp;
        /// <summary>直近の <see cref="StaticValue"/> が「自分は安全」経路へ落ちたか。
        /// **インスタンスごとに単一スレッド**なのでワーカーと競合しない。</summary>
        private bool _lastSafeProjected;

        /// <summary>浅い評価が最良と同点の候補を、 **列挙順に偏らないよう等間隔で** 抜き出す。
        ///
        /// <para>先頭から詰めると <see cref="BuildCodes"/> の <c>code</c> 下位桁に偏る
        /// (＝後ろのダイスを特定の端子へ寄せた形ばかりが残る)。 等間隔なら偏りが消えるわけでは
        /// ないが、 少なくとも割り当て空間の一区画に寄ることはない。</para>
        ///
        /// <para>列挙をもう 1 周する。 1 周は 243 回の <see cref="ApplyTurn"/> で、
        /// ロールアウト (候補数 × 本数 × 深さ) に比べれば無視できる。
        /// **同点が m 本を超えたときしか呼ばない。**</para></summary>
        /// <returns>実際に集めた候補数 (<see cref="_candScore"/> / <see cref="_candW"/> の先頭から)。</returns>
        private int CollectTied(int[] rolls, int enemyAtk, int atkBase, SimState s0,
                                float top, int want, int tiedTotal)
        {
            int got = 0, t = 0;
            for (int ci = 0; ci < _codes.Length && got < want; ci++)
            {
                var a = _codes[ci];
                if (!Canonical(a, rolls)) continue;
                if (UsesSealed(a)) continue;

                var s = s0;
                int outcome = ApplyTurn(ref s, rolls, enemyAtk, a, atkBase, _nowMods);
                float v = outcome == Ongoing ? StaticValue(s) : Terminal(outcome, s);
                if (v < top - TieEps) continue;   // 同点ではない ＝ 対象外

                // tiedTotal 本から want 本を均等に拾う (t 番目を残すかどうかの判定)
                if (t == 0 || (long)t * want / tiedTotal != (long)(t - 1) * want / tiedTotal)
                {
                    _candScore[got] = v;
                    Array.Copy(a, _candW[got], _k);
                    got++;
                }
                t++;
            }
            return Math.Max(1, got);
        }

        /// <summary>較正のために「今ターン何を予測したか」を控える。 次の判断時に実測と突き合わせる。</summary>
        private void Remember(CombatManager cm, SimState s0, int[] rolls, DiceTerminal[] w,
                              int enemyAtk, int atkBase)
        {
            int aSum = 0, bSum = 0;
            for (int i = 0; i < _k; i++)
            {
                if (w[i] == DiceTerminal.Attack) aSum += rolls[i];
                else if (w[i] == DiceTerminal.Block) bSum += rolls[i];
            }
            bool skipAttack = true;
            for (int i = 0; i < _k; i++) if (w[i] == DiceTerminal.Attack) { skipAttack = false; break; }
            // [計装] 供給側: この判断で何本を充電端子へ挿したか。
            {
                int cDice = 0;
                for (int i = 0; i < _k; i++) if (w[i] == DiceTerminal.Charge) cDice++;
                System.Threading.Interlocked.Add(ref WireChargeDice, cDice);
                System.Threading.Interlocked.Increment(ref WireChargeDecisions);
            }
            if (_pierceMul > 0) bSum = Math.Max(0, bSum - enemyAtk * _pierceMul);

            _pendingPredAtk = skipAttack ? 0 : (atkBase + aSum);
            _pendingPredRaw = Math.Max(0, enemyAtk - bSum);
            _pendingEnemyHp = s0.eHp;
            _pendingPlayerHp = s0.pHp;
        }

        /// <summary>特殊端子の接続上限と〈不器用〉の端子上限を守っているか。
        /// **破る割り当ては評価すらしない** ── 本体側 (SanitizeWiring) が畳んだ形で
        /// 効用を出すと「挿したつもりの効果」で過大評価するため。</summary>
        private bool Legal(DiceTerminal[] w)
        {
            int spec = 0, distinct = 0;
            _termSeen[0] = _termSeen[1] = _termSeen[2] = _termSeen[3] = false;
            for (int i = 0; i < _k; i++)
            {
                int ti = (int)w[i];
                if (ti == (int)DiceTerminal.Special) spec++;
                if (!_termSeen[ti]) { _termSeen[ti] = true; distinct++; }
            }
            return spec <= _specialLimit && distinct <= _termCap;
        }

        // ============================================================
        //  ② リロール: 「振り直した後の盤面価値」の期待値で決める
        // ============================================================

        /// <summary><see cref="CombatManager.RerollPolicy"/> へ差す。
        ///
        /// 貪欲版は「同値 3 個以上を残す」のような**型**で打っていた。 型は平均的には正しいが、
        /// 「充電をここで使い切ると次ターン以降が回らない」を見られない。 ここでは
        /// **払った後の盤面価値**を直接比べる ── 充電の価値は先の盤面に自動で織り込まれる。</summary>
        public int[] ChooseReroll(int[] dice, MutualTurnTelegraph tele, int attempt)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            try { return ChooseRerollCore(dice, tele, attempt); }
            finally { ProfRerollTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0; ProfRerollCalls++; }
        }

        private int[] ChooseRerollCore(int[] dice, MutualTurnTelegraph tele, int attempt)
        {
            rerollDecisions++;
            if (dice == null || dice.Length == 0) return null;
            System.Threading.Interlocked.Increment(ref RrCalls);
            if (attempt > Math.Max(1, rerollMaxAttempts))
            { System.Threading.Interlocked.Increment(ref RrDeclineAttempt); return null; }
            var cm = CombatManager.Instance;
            if (cm == null || !Snapshot(cm, dice.Length)) return null;
            _nowMods = ModsFromTelegraph(tele);
            _sealedNow = tele.sealedTerminal;
            var ctx = PassiveSkillManager.Instance.Context;

            int charge = ctx.GetCharge();
            bool free = ctx.currentBuffs.TryGetValue(YachtRoleEffects.FreeRerollKey, out float f) && f > 0f;
            System.Threading.Interlocked.Add(ref RrChargeSum, charge);
            if (charge >= _chargeMax) System.Threading.Interlocked.Increment(ref RrAtCap);
            // 最小コストは `1 個 × attempt`。 それすら払えないなら 31 マスク回す意味がない。
            if (!free && charge < attempt)
            { System.Threading.Interlocked.Increment(ref RrDeclineAfford); return null; }

            var s0 = new SimState
            {
                pHp = cm.PlayerHP,
                eHp = cm.EnemyHP,
                turn = tele.turn,
                charge = charge,
                usedMask = MaskOf(ctx.usedRoles),
            };
            int enemyAtk = Math.Max(0, tele.enemyAttackValue);
            // 2 体戦の 2 パケット目。 予告 (柱3) で開示済みなので、 方策はこれを見て配線できる。
            _secondAtk = Math.Max(0, tele.secondaryAttackValue);
            int atkBase = Math.Max(0, _pAtkPower - Math.Max(0, tele.playerAttackPenalty));

            // 手札のメモ。 31 マスク × 8 標本 = 248 評価あるので、 同じ手札に何度も当たる。
            //   **鍵は (整列した手札 + 充電)**。 充電を外してはいけない ── 払うコストが
            //   マスクの大きさで変わり、 StaticValue はそれを読むため。
            //   s0 と enemyAtk はこの呼び出しの間だけ固定なので、 呼び出しをまたぐ再利用はしない。
            _tvMemo.Clear();

            // 振り直さない場合の価値 (基準)
            float baseV = TurnValue(s0, dice, enemyAtk, atkBase);
            // **今ターン撃破できる手札なら、 振り直しで改善する余地は定義上ゼロ。**
            //   通常戦は 2〜3 ターンで決着するので、 撃破ターンの探索が丸ごと消える。
            //
            //   **値の大小で判定してはいけない** (2026-08-10 に踏んだ罠)。 StaticValue は
            //   p × (1 + winHpWeight × HP率) を返すので、 **勝っていなくても 1.0 を超える**。
            //   `baseV >= 1f` で切ると優勢な局面すべてでリロール探索が消え、
            //   速度が 40 倍になる代わりに 5層クリアが 30% → 12% へ落ちた。
            //   撃破したかどうかは TurnValue が事実として持っている。 それを見る。
            if (_tvWon) { System.Threading.Interlocked.Increment(ref RrDeclineLethal); return null; }

            // 全部分集合 (2^k = 32) を試す。 k は 5 なので総当たりで足りる。
            int subsets = 1 << _k;
            int bestMask = 0; float bestV = baseV;
            int samples = Math.Max(1, rerollSamples);
            int seed = NextSeed();

            var maskV = new float[subsets];

            // ---- 速度 (2026-09-19): 結果を 1 ビットも変えずに評価回数を減らす 2 つの手 ----
            //   実測でリロール判断が Super の所要時間の 98% (1 回 10.7ms・1 ラン 約 4 秒) だった。
            //
            //   (1) **同じ目の組を振り直すマスクは同値。** 評価 (TurnValue) は出目の並びに依存せず
            //       (全割り当てを列挙する)、 各マスクは同じ種から引き直す (共通乱数) ので、
            //       振り直す目の多重集合が同じマスクは maskV が完全に一致する。 最初の 1 つだけ評価して
            //       後で写す。 選択は「厳密に大きい最初のマスク」なので、 写しが選ばれることはない。
            //       位置に依存する要素 (_ghostNow) は配線の仕上げ段でしか立たないので、 ここでは常に空。
            //   (2) **手札メモをマスクをまたいで使う** (逐次のときだけ)。 s0 / enemyAtk / atkBase は
            //       この呼び出しの間固定で、 充電はメモの鍵に入っている。 並列時はワーカーごとの
            //       メモなので従来どおり毎回クリアする (スレッド安全のため)。
            var rep = new int[subsets];
            var seenSig = new Dictionary<long, int>();
            for (int mask = 1; mask < subsets; mask++)
            {
                // 振り直す目の多重集合を鍵にする (整列して 5 ビットずつ詰める)
                int n = 0;
                for (int i = 0; i < _k; i++) if ((mask & (1 << i)) != 0) _keyBuf[n++] = dice[i];
                for (int i = 1; i < n; i++)
                {
                    int v = _keyBuf[i], j = i - 1;
                    while (j >= 0 && _keyBuf[j] > v) { _keyBuf[j + 1] = _keyBuf[j]; j--; }
                    _keyBuf[j + 1] = v;
                }
                long sig = n;
                for (int i = 0; i < n; i++) sig |= (long)(_keyBuf[i] & 31) << (4 + 5 * i);
                if (seenSig.TryGetValue(sig, out int first)) rep[mask] = first;
                else { seenSig[sig] = mask; rep[mask] = mask; }
            }
            bool serial = Parallelism(subsets) <= 1;
            var savedRng = _rng;
            if (serial) _tvMemo.Clear();

            SyncWorkers(subsets);
            System.Action<int> evalMask = mask =>
                {
                    maskV[mask] = float.NegativeInfinity;
                    if (rep[mask] != mask) return;   // 同値マスク: 後で写す
                    int cnt = 0; for (int i = 0; i < _k; i++) if ((mask & (1 << i)) != 0) cnt++;
                    int cost = free ? 0 : cnt * attempt;
                    if (cost > charge) return;

                    var w = serial ? this
                          : (_workers != null && mask < _workers.Length) ? _workers[mask] : this;
                    w._rng = new System.Random(seed);   // 共通乱数: 案どうしを同じ引きで比べる
                    if (!serial) w._tvMemo.Clear();
                    var buf = w._trialBuf;
                    var sAfter = s0; sAfter.charge = charge - cost;

                    // --- 小さい振り直しは全列挙する (厳密・標本より安い) ---
                    int fc = w.FaceCount;
                    if (cnt > 0 && cnt <= w.exactRerollMaxDice && fc > 0)
                    {
                        long total = 1;
                        for (int i = 0; i < cnt; i++) total *= fc;
                        // マスクに含まれるダイスの添字を集める (列挙の桁に対応させる)
                        int di = 0;
                        for (int i = 0; i < _k; i++)
                        {
                            if ((mask & (1 << i)) != 0) w._sortIdx[di++] = i;
                            else buf[i] = dice[i];
                        }
                        float sum = 0f;
                        for (long code = 0; code < total; code++)
                        {
                            long t = code;
                            for (int j = 0; j < cnt; j++) { buf[w._sortIdx[j]] = w.FaceAt((int)(t % fc)); t /= fc; }
                            sum += w.TurnValue(sAfter, buf, enemyAtk, atkBase);
                        }
                        maskV[mask] = sum / total;
                        return;
                    }

                    float acc = 0f;
                    for (int r = 0; r < samples; r++)
                    {
                        for (int i = 0; i < _k; i++)
                            buf[i] = ((mask & (1 << i)) != 0) ? w.RollFace() : dice[i];
                        acc += w.TurnValue(sAfter, buf, enemyAtk, atkBase);
                    }
                    maskV[mask] = acc / samples;
                };
            if (serial) for (int mask = 1; mask < subsets; mask++) evalMask(mask);
            else System.Threading.Tasks.Parallel.For(1, subsets,
                new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = Parallelism(subsets) },
                evalMask);
            _rng = savedRng;
            for (int mask = 1; mask < subsets; mask++)
                if (rep[mask] != mask) maskV[mask] = maskV[rep[mask]];

            float bestAny = float.NegativeInfinity;   // [計装] 断った時に基準とどれだけ離れていたか
            for (int mask = 1; mask < subsets; mask++)
            {
                if (maskV[mask] > bestAny) bestAny = maskV[mask];
                if (maskV[mask] > bestV) { bestV = maskV[mask]; bestMask = mask; }
            }

            if (bestMask == 0)
            {
                System.Threading.Interlocked.Increment(ref RrDeclineNoGain);
                if (bestAny > float.NegativeInfinity)
                    System.Threading.Interlocked.Add(ref RrNoGainDeficitMilli,
                        (long)Math.Round(Math.Max(0f, baseV - bestAny) * 1000f));
                return null;
            }
            var pick = new List<int>(_k);
            for (int i = 0; i < _k; i++) if ((bestMask & (1 << i)) != 0) pick.Add(i);
            System.Threading.Interlocked.Increment(ref RrAccept);
            System.Threading.Interlocked.Add(ref RrAcceptDiceSum, pick.Count);
            System.Threading.Interlocked.Add(ref RrAcceptCostSum, free ? 0 : pick.Count * attempt);
            return pick.ToArray();
        }

        /// <summary>その手札を最良に配線したときの盤面価値。 リロール判断の物差し。
        /// **ロールアウトは回さない** (32 案 × 8 標本 = 256 回引かれるため) ──
        /// 代わりに 1 ターン解決 + <see cref="StaticValue"/> で近似する。</summary>
        private float TurnValue(SimState s0, int[] dice, int enemyAtk, int atkBase)
        {
            // ---- メモ引き ----
            //   出目の並び順は結果に影響しない (全割り当てを列挙するため) ので、
            //   整列してから鍵にする。 その方が当たる。
            long key = 0;
            bool memo = _k <= 10;
            if (memo)
            {
                for (int i = 0; i < _k; i++) _keyBuf[i] = dice[i];
                for (int i = 1; i < _k; i++)
                {
                    int v = _keyBuf[i], j = i - 1;
                    while (j >= 0 && _keyBuf[j] > v) { _keyBuf[j + 1] = _keyBuf[j]; j--; }
                    _keyBuf[j + 1] = v;
                }
                for (int i = 0; i < _k; i++) key |= (long)(_keyBuf[i] & 31) << (5 * i);
                key |= (long)Math.Max(0, Math.Min(63, s0.charge)) << 52;
                key |= 1L << 62;
                // メモ命中時は撃破フラグを持っていないので、 安全側 (打ち切らない) に倒す。
                if (_tvMemo.TryGetValue(key, out float cached)) { _tvWon = false; return cached; }
            }

            bool won = false;
            float best = float.NegativeInfinity;
            BuildCodes();
            PrepareHand(dice);
            for (int ci = 0; ci < _codes.Length; ci++)
            {
                var _assign = _codes[ci];
                if (!Canonical(_assign, dice)) continue;
                if (UsesSealed(_assign)) continue;
                var s = s0;
                int outcome = ApplyTurn(ref s, dice, enemyAtk, _assign, atkBase, _nowMods);
                float v = outcome != Ongoing ? Terminal(outcome, s) : StaticValue(s);
                if (v > best) { best = v; won = outcome == Win; }
            }
            _tvWon = won;
            if (memo) _tvMemo[key] = best;
            return best;
        }

        // ============================================================
        //  ③ 役: 「空振りする役だけ見送る」を価値で判定する
        // ============================================================

        /// <summary><see cref="CombatManager.RolePolicy"/> へ差す。
        ///
        /// 配線探索 (<see cref="ApplyTurn"/>) が <see cref="WouldFire"/> で見積もっているので、
        /// **ここも同じ述語を使う**。 両者がずれると探索が選んだ配線と実際の発動が食い違う。</summary>
        public List<RoleKind> ChooseRoles(List<RoleKind> candidates, MutualTurnTelegraph tele,
                                          HashSet<RoleKind> used)
        {
            if (candidates == null || candidates.Count == 0) return null;
            var cm = CombatManager.Instance;
            int eHp = cm?.EnemyHP ?? 1;
            int enemyAtk = Math.Max(0, tele.enemyAttackValue);
            // 2 体戦の 2 パケット目。 予告 (柱3) で開示済みなので、 方策はこれを見て配線できる。
            _secondAtk = Math.Max(0, tele.secondaryAttackValue);
            // 今ターンの攻撃で倒し切るかは配線後に確定している。 予測は Remember が持っている値を使う。
            int predDmg = _pendingPredAtk > 0 ? (int)(_pendingPredAtk * _dmgMul) : 0;
            bool lethalAlready = predDmg >= eHp;

            var fire = new List<RoleKind>(candidates.Count);
            for (int i = 0; i < candidates.Count; i++)
                if (WouldFire(candidates[i], enemyAtk, lethalAlready, _isBoss)) fire.Add(candidates[i]);
            // 〈綻び〉は末尾から切り捨てるので、 **切り捨てられて惜しい順**に並べる。
            fire.Sort((a, b) => AutoRunner.RolePriorityOf(b).CompareTo(AutoRunner.RolePriorityOf(a)));
            return fire;
        }

        /// <summary>その役を今ターン切る価値があるか。 **1 戦闘 1 回**なので、
        /// 効果が丸ごと空振りする局面でだけ温存する。
        ///
        /// 温存を増やしすぎない: 見送れば必ずそのターンぶんの被弾を払う。 ここで弾くのは
        /// 「効果が定義上 0 になる」ケースだけに限る。</summary>
        private static bool WouldFire(RoleKind rk, int enemyAtk, bool lethalAlready, bool isBoss)
        {
            switch (rk)
            {
                // 敵が殴ってこないターンに被ダメ 0 / 半減 / 反射を切っても何も起きない
                case RoleKind.LargeRun:
                case RoleKind.Offset:
                    return enemyAtk > 0;
                // 今ターン倒し切るなら、 撃破系・妨害系はすべて無駄打ち
                case RoleKind.Yacht:
                    // 出目依存の割合削りになったので、 ボスでは重ねる価値がある (最大 60%)。
                    //   通常敵も即勝利ではなくなったが、 6 揃いなら 120% で結果的に確殺。
                    return !lethalAlready || isBoss;
                case RoleKind.MatchedStrike:
                    return !lethalAlready;   // 与ダメ+50% は撃破確定なら無駄打ち
                case RoleKind.Quad:
                    return !lethalAlready && enemyAtk > 0;
                default:
                    return true;
            }
        }

        // ============================================================
        //  前向きシミュレーション本体
        // ============================================================

        /// <summary>1 ターンを解決して状態を進める。 戻り値は <see cref="Ongoing"/> /
        /// <see cref="Win"/> / <see cref="Loss"/>。
        ///
        /// **本体の完全な再現ではない。** 再現すべきは
        ///   ① 配線候補どうしの順位 ② 戦闘が何ターン続くかの尺度
        /// の 2 つで、 絶対値のずれは <see cref="_dmgMul"/> / <see cref="_takenMul"/> が吸う。</summary>
        private int ApplyTurn(ref SimState s, int[] dice, int enemyAtk, DiceTerminal[] w, int atkBase,
                              in TurnMods m)
        {
            System.Threading.Interlocked.Increment(ref ProfApplyTurn);
            int aCount = 0, bCount = 0, aSum = 0, bSum = 0, cSum = 0, sCount = 0, sSum = 0;
            for (int i = 0; i < _k; i++)
            {
                int v = dice[i];
                switch (w[i])
                {
                    case DiceTerminal.Attack: _grpA[aCount++] = v; aSum += v; break;
                    case DiceTerminal.Block: _grpB[bCount++] = v; bSum += v; break;
                    case DiceTerminal.Special: sCount++; sSum += v; break;
                    default: cSum += v; break;
                }
            }

            // --- ゴースト接続 (出目パーツ T3/T4) ---
            //   **合計値にだけ乗せ、 役判定の組 (_grpA/_grpB) には入れない。**
            //   _ghostNow が非 null なのは貪欲パス中だけ ── 列挙とロールアウトでは
            //   null なので、 **探索の計算量は 1 命令も増えない**。
            if (_ghostNow != null)
            {
                for (int i = 0; i < _k && i < _ghostNow.Length; i++)
                {
                    int g = _ghostNow[i];
                    if (g == WiringPlan.NoGhost) continue;
                    int add = dice[i];
                    if ((int)w[i] == g) add += GameLoop.DiceFaceParts.T4StackBonus;
                    if (g == (int)DiceTerminal.Attack) aSum += add;
                    else if (g == (int)DiceTerminal.Block) bSum += add;
                    else if (g == (int)DiceTerminal.Special) sSum += add;
                    else cSum += add;
                }
            }

            // --- 特殊端子 (§6-5) ---
            //   軽減無視の追加ダメージ相当 (出血・反撃・工房) と回復に畳んで扱う。
            int specDmg = 0, specHeal = 0;
            if (_spec != null && sCount > 0)
                ApplySpecial(sSum, sCount, dice, w, ref aSum, ref bSum, ref cSum,
                             s, enemyAtk, ref specDmg, ref specHeal);

            // --- 〈貫きの錐〉 ---
            if (m.pierce > 0 && bSum > 0) bSum = Math.Max(0, bSum - enemyAtk * m.pierce);

            // --- 役 ---
            bool zero = false, half = false, kill = false, stun = false, freeRr = false;
            bool forceCrit = false;
            int fixedDmg = 0;
            float critAdd = 0f, penAdd = 0f;
            float outMul = 1f;              // 〈拮抗〉: 与ダメ倍率 (本体の outgoingDamageMultiplier 相当)
            _roleBuf.Clear();
            EnsureHandRoles(dice);                       // 手札役は配線に依存しない (下記)
            for (int i = 0; i < _handBuf.Count; i++) _roleBuf.Add(_handBuf[i]);
            int handEnd = _roleBuf.Count;
            int atkEnd = handEnd;
            if (!_suppressTerm)
            {
                CopyInto(_grpA, aCount, out var ga);
                CopyInto(_grpB, bCount, out var gb);
                YachtRoles.EvaluateTerminal(ga, _roleBuf);
                atkEnd = _roleBuf.Count;
                YachtRoles.EvaluateTerminal(gb, _roleBuf);
            }
            YachtRoles.EvaluateWiring(aSum, bSum, enemyAtk, s.eHp, _roleBuf);

            int firedMask = 0;
            bool lethalGuess = (atkBase + aSum) * _dmgMul >= s.eHp && aCount > 0;
            for (int i = 0; i < _roleBuf.Count; i++)
            {
                var rk = _roleBuf[i];
                int bit = 1 << (int)rk;
                if ((s.usedMask & bit) != 0 || (firedMask & bit) != 0) continue;
                if ((_sealedRoles & bit) != 0) continue;   // 〈凶運〉封印
                if (!WouldFire(rk, enemyAtk, lethalGuess, _isBoss)) continue;
                firedMask |= bit;

                // 挿入順が 手札 → 攻撃端子 → ブロック端子 → 配線 なので、
                // atkEnd 以降の端子役だけがブロック側で成立したもの。
                // 手札役・配線役は onAttack を見ないので、 どちらに転んでも影響しない。
                bool onAttack = i < atkEnd;
                int n = onAttack ? aCount : bCount;
                switch (rk)
                {
                    case RoleKind.AllDifferent: if (onAttack) aSum += n * 2; else bSum += n * 2; break;
                    case RoleKind.Pair: { int b = PairBonus(onAttack ? _grpA : _grpB, n); if (onAttack) aSum += b; else bSum += b; break; }   // 出目合計÷2
                    case RoleKind.AllEven:
                        if (onAttack) aSum += (int)Math.Round(SumOf(_grpA, aCount) * 0.3f);
                        else bSum += Math.Max(0, bSum - enemyAtk) / 2;   // 余剰の半分がシールド ≒ ブロック増
                        break;
                    case RoleKind.AllOdd: if (onAttack) critAdd += 0.25f; break;
                    case RoleKind.SmallRun: if (onAttack) penAdd += 0.30f; else bSum += 3; break;
                    case RoleKind.Triple: if (onAttack) forceCrit = true; else half = true; break;
                    case RoleKind.SkipRun: if (onAttack) fixedDmg += n * 3; break;
                    case RoleKind.TwoPair: s.charge += 8; break;
                    case RoleKind.MediumRun: s.charge += 12; freeRr = true; break;
                    case RoleKind.FullHouse:
                    {
                        int fh = (SumOf(dice, _k) + 1) / 2;    // 手札合計÷2 (正本と同じ)
                        if (aSum > 0) aSum += fh;
                        if (bSum > 0) bSum += fh;
                        break;
                    }
                    case RoleKind.LargeRun: zero = true; break;
                    case RoleKind.Quad: stun = true; break;
                    case RoleKind.Yacht:
                    {
                        // 出目依存の割合削り (正本と同じ)。 通常敵でも即勝利ではなくなった
                        //   ── 6 揃いなら 120% で結果的に確殺、 1 揃いなら 20% の削り。
                        int pct = SumOf(dice, _k)
                                * (_isBoss ? YachtRoleEffects.YachtPctPerPipBoss
                                           : YachtRoleEffects.YachtPctPerPipNormal);
                        fixedDmg += (int)Math.Ceiling(_eMaxHp * (pct / 100f));
                        break;
                    }
                    case RoleKind.Balance: aSum += 5; bSum += 5; break;
                    case RoleKind.Offset: zero = true; s.charge += 10; break;
                    case RoleKind.MatchedStrike: outMul += 0.5f; break;   // 拮抗: 与ダメ+50%
                }
            }
            s.usedMask |= firedMask;

            // --- 自攻撃 ---
            //   攻撃端子が 0 本なら **攻撃そのものを行わない** (素火力もパッシブ加算も出ない)。
            int dealt = 0;
            if (aCount > 0)
            {
                float p = forceCrit ? 1f : Clamp(_critBase + critAdd, 0f, 1f);
                float critFactor = 1f + (_critMul - 1f) * p;
                // 較正倍率には平均的な会心ぶんが既に入っているので、 素の分を割り戻してから掛ける。
                float baseCrit = 1f + (_critMul - 1f) * _critBase;
                dealt = (int)((atkBase + aSum) * _dmgMul * (critFactor / Math.Max(0.01f, baseCrit))
                              * (1f + penAdd * 0.5f) * outMul);
            }
            // === 7層ヴェスカ〈遺物学者〉の抽選 (§13-4) ===
            //   すべて予告に開示されている性質なので、 先読みに使うのは完全情報の範囲内。
            //   **貫通枠 (fixedDmg) より前に掛ける** ── 軽減無視は盾にも吸われない。
            int reflectBack = 0;
            if (aCount > 0 && dealt > 0)
            {
                if (m.halveDamage) dealt = (dealt + 1) / 2;
                else if (m.damageTakenMul > 0f && m.damageTakenMul < 1f)
                    dealt = (int)(dealt * m.damageTakenMul);
                if (m.dodgeChance > 0f) dealt = (int)(dealt * (1f - m.dodgeChance));
                if (m.shield > 0)
                {
                    int absorbed = Math.Min(m.shield, dealt);
                    dealt -= absorbed;
                    if (m.shieldReflectRate > 0f) reflectBack += (int)(absorbed * m.shieldReflectRate);
                }
            }
            dealt += fixedDmg + specDmg;
            if (kill) dealt = Math.Max(dealt, s.eHp);
            s.eHp -= dealt;
            if (s.eHp <= 0) return Win;
            if (specHeal > 0) s.pHp = Math.Min(_pMaxHp, s.pHp + specHeal);

            // --- 敵攻撃 ---
            int taken = reflectBack;
            if (!s.stunNext)
            {
                taken += (int)(Math.Max(0, enemyAtk - bSum) * _takenMul);
                // 2 体戦 (2026-08-28): 2 体目は**独立したパケット**で、 ブロックは各パケットから
                //   全額引かれる。 合計値ひとつに畳むと折れ線の形が変わるので、 必ず別項で足す
                //   ── 畳むと「ブロックがどちらか一方に届く」帯の評価が丸ごと消える。
                if (_secondAtk > 0)
                    taken += (int)(Math.Max(0, _secondAtk - bSum) * _takenMul);
                if (zero) taken = reflectBack;
                else if (half) taken = reflectBack + Math.Max(1, (taken - reflectBack) / 2);
            }
            // 烈炎: 前ターンまでに積んだスタック分を軽減無視で受ける。 ブロックへ厚く挿すと更に積む。
            //   **配線側がこれを見られないと一方的な税になる** ので、 積む/積まないを盤面に出す。
            if (s.blazeStacks > 0) taken += s.blazeStacks;
            if (m.blazePenalizesBlock
                && bCount >= InventorySystem.PassiveSkills.Effects.BlazeBrand.BlockDiceThreshold)
                s.blazeStacks++;
            // 処刑人の烙印: 攻撃終了時に HP が最大の 20% 以下なら 9999。 **予告されている。**
            if (m.executeArmed && (s.pHp - taken) <= (int)Math.Ceiling(_pMaxHp * 0.20f))
                taken = s.pHp;

            s.pHp -= taken;
            if (s.pHp <= 0) return Loss;

            // --- 充電と持ち越し ---
            s.charge = Math.Min(_chargeMax, s.charge + cSum + _chargePerTurn);
            s.stunNext = stun;
            s.freeRerollNext = freeRr;
            s.turn++;
            return Ongoing;
        }

        /// <summary>特殊端子の寄与。 効果表は <see cref="SpecialTerminals"/> が正本。
        /// 蓄積型 (冷却材・工房) は 1 ターンでは返らないので薄く見積もる。</summary>
        private void ApplySpecial(int sSum, int sCount, int[] dice, DiceTerminal[] w,
                                  ref int aSum, ref int bSum, ref int cSum, SimState s, int enemyAtk,
                                  ref int specDmg, ref int specHeal)
        {
            switch (_spec.kind)
            {
                case SpecialTerminalKind.HeavyStrike:
                    aSum += (int)Math.Ceiling(sSum * SpecialTerminals.HeavyStrikeMultiplier); break;
                case SpecialTerminalKind.FullGuard:
                    bSum += sSum * SpecialTerminals.FullGuardMultiplier; break;
                case SpecialTerminalKind.Bleed:
                    specDmg += (int)(sSum * 0.8f + sSum * sCount * 0.5f); break;
                case SpecialTerminalKind.Heal:
                    specHeal += sSum; break;
                case SpecialTerminalKind.Deathwish:
                {
                    int missing = Math.Max(0, _pMaxHp - s.pHp);
                    aSum += sSum + (int)(missing * SpecialTerminals.DeathwishMissingHpPct);
                    break;
                }
                case SpecialTerminalKind.VitalPoint:
                {
                    int mn = int.MaxValue;
                    for (int i = 0; i < _k; i++) if (dice[i] < mn) mn = dice[i];
                    bool allMin = true;
                    for (int i = 0; i < _k && allMin; i++)
                        if (w[i] == DiceTerminal.Special && dice[i] != mn) allMin = false;
                    if (allMin) aSum += sSum * SpecialTerminals.VitalPointMultiplier;
                    break;
                }
                case SpecialTerminalKind.Aim:
                    specDmg += (int)((int)Math.Ceiling(sSum / 2f) / 100f * aSum * (_critMul - 1f)); break;
                case SpecialTerminalKind.Battery:
                    cSum += sSum + sCount * SpecialTerminals.BatteryPerDice; break;
                case SpecialTerminalKind.Coolant:
                case SpecialTerminalKind.Foundry:
                    specDmg += (int)(sSum * 0.6f); break;
                case SpecialTerminalKind.Riposte:
                    specDmg += (int)(Math.Min(sSum, Math.Max(0, enemyAtk - bSum)) * 0.8f); break;
            }
        }

        /// <summary>決着まで打ち切りながら回す。 方策は**軽い貪欲** ──
        /// ここで全列挙をやると 1 判断あたり 10 万回の評価になり、 500 ラン回らない。
        /// ロールアウトに要るのは「その先どのくらい保つか」の尺度であって最適手ではない。</summary>
        private float Rollout(SimState s)
        {
            // **ボス戦は深く読む。** 7層の 4 連戦は 40 ターン級で、 20 で打ち切ると
            // 全ロールアウトが「決着せず部分点」に落ちて先読みが実質機能しない。
            // 通常戦 (2〜3 ターン) で同じ深さを使うのは無駄なので、 ここで分ける。
            int depth = Math.Max(1, _isBoss ? bossRolloutDepth : rolloutDepth);
            for (int d = 0; d < depth; d++)
            {
                // 先のターンの抽選結果は知りようがないので、 **観測した頻度から引き直す**。
                var fm = SampleMods();
                // 〈綻び〉の封印は輪番で周期も既知なので、 先のターンぶんも正確に再現できる。
                //   これは予知ではなく **公開された規則の適用**。
                _sealedNow = MetaProgression.MetaDebuffApplicator.GetSealedTerminal(
                    s.turn, _specialLimit > 0 ? 4 : 3);
                for (int i = 0; i < _k; i++) _simDice[i] = RollFace();

                // ロールアウト内の簡易リロール。 **これが無いと充電が強さへ変換されない** ──
                // 深い評価 (Rollout) が充電を無視したままだと、 配線判断は充電端子を選ばない。
                // 1 回目 (コスト = 個数 × 1) だけ・面平均を下回るダイスだけ、 払える時に振る。
                int below = 0;
                for (int i = 0; i < _k; i++) if (_simDice[i] < _meanFace) below++;
                if (below > 0 && s.charge >= below)
                {
                    s.charge -= below;
                    for (int i = 0; i < _k; i++) if (_simDice[i] < _meanFace) _simDice[i] = RollFace();
                }

                int eTotal = 0;
                for (int i = 0; i < _eDiceCount; i++) eTotal += _rng.Next(1, _eRollMax + 1);
                int enemyAtk = Math.Max(0, (int)Math.Round(
                    Escalation.GetMultiplier(_escProfile, s.turn) * (_eBaseAtk + eTotal) * HeavyAt(s.turn)));

                GreedyWiring(_simDice, enemyAtk, s);
                int outcome = ApplyTurn(ref s, _simDice, enemyAtk, _rolloutW, _pAtkPower, fm);
                if (outcome != Ongoing)
                {
                    System.Threading.Interlocked.Increment(ref SearchRolloutDecided[_isBoss ? 1 : 0]);
                    return Terminal(outcome, s);
                }
            }
            System.Threading.Interlocked.Increment(ref SearchRolloutCapped[_isBoss ? 1 : 0]);
            // 決着せず: 削り具合と HP 残量で部分点を与える (勾配を残すため 0 にはしない)
            float prog = 1f - Math.Min(1f, s.eHp / (float)_eMaxHp);
            return 0.10f + 0.35f * prog * (s.pHp / (float)_pMaxHp);
        }

        // ============================================================
        //  ロールアウト内方策の相対価格 (掃引対象)
        // ============================================================
        //  **全ロールアウトがここを通る** ── 深い評価の向きを丸ごと決めている。
        //  単位混在の手置き値で、 一度も掃引していない (2026-08-22 時点)。
        //
        //  `u = -ttk × A − min(taken, pHp) × B + charge × C`
        //  A は「1 ターン短縮の価値」を HP 換算した価格。 **現行 4 は低すぎる疑いがある** ──
        //  1 ターン伸びれば敵の攻撃を丸ごと 1 回余計に受ける。 実測の敵攻撃値は 11〜34 なので、
        //  1 ターンの実勢価格は 15〜30 HP 相当のはず。 4 はその 1/4〜1/8。

        /// <summary>ttk (撃破までのターン数) 1 ターンの価格。 HP 換算。</summary>
        public float rolloutTtkWeight = 4f;
        /// <summary>被ダメ 1 の価格。 **基準にする** ので通常は 1.0 のまま。</summary>
        public float rolloutTakenWeight = 1f;
        /// <summary>充電 1 の価格。</summary>
        public float rolloutChargeWeight = 0.3f;

        /// <summary>ロールアウト用の軽い配線。 出目降順で「攻撃 a 本 / ブロック b 本 / 残り充電」の
        /// 本数分割 (k=5 で 21 通り) だけを見る。 役は見ないが、 <see cref="ApplyTurn"/> が
        /// 結果として成立した役を拾うので、 役の消費そのものは追跡できている。</summary>
        private void GreedyWiring(int[] dice, int enemyAtk, SimState s)
        {
            // 出目降順に並べる。 挿入ソート ── k は 5 なので Array.Sort の
            // Comparison デリゲート確保 (毎ターン数千回) を避ける方が速い。
            for (int i = 0; i < _k; i++) _sortIdx[i] = i;
            for (int i = 1; i < _k; i++)
            {
                int key = _sortIdx[i], j = i - 1;
                while (j >= 0 && dice[_sortIdx[j]] < dice[key]) { _sortIdx[j + 1] = _sortIdx[j]; j--; }
                _sortIdx[j + 1] = key;
            }

            float best = float.NegativeInfinity;
            for (int a = 0; a <= _k; a++)
                for (int b = 0; b <= _k - a; b++)
                {
                    if (a == 0 && s.pHp > enemyAtk) continue;   // 殴らないターンは窮地のみ
                    int aSum = 0, bSum = 0, c = 0;
                    for (int i = 0; i < _k; i++)
                    {
                        int v = dice[_sortIdx[i]];
                        if (i < a) aSum += v; else if (i < a + b) bSum += v; else c += v;
                    }
                    int dmg = a > 0 ? (int)((_pAtkPower + aSum) * _dmgMul) : 0;
                    int taken = (int)(Math.Max(0, enemyAtk - bSum) * _takenMul);
                    int ttk = dmg > 0 ? (s.eHp + dmg - 1) / dmg : 99;
                    float u = -ttk * rolloutTtkWeight
                            - Math.Min(taken, s.pHp) * rolloutTakenWeight
                            + c * rolloutChargeWeight;
                    if (dmg >= s.eHp) u += 100f;
                    if (taken >= s.pHp) u -= 1000f;
                    if (u > best)
                    {
                        best = u;
                        for (int i = 0; i < _k; i++)
                            _rolloutW[_sortIdx[i]] = i < a ? DiceTerminal.Attack
                                                   : i < a + b ? DiceTerminal.Block : DiceTerminal.Charge;
                    }
                }
            if (best == float.NegativeInfinity)
                for (int i = 0; i < _k; i++) _rolloutW[i] = DiceTerminal.Block;
        }

        // ============================================================
        //  評価
        // ============================================================

        /// <summary>決着した盤面の価値。 **勝てば同じ、 ではない** ── 残 HP はそのまま
        /// 次の戦闘の資源なので、 ラン全体で見ると勝ち方の差が効く。</summary>
        private float Terminal(int outcome, SimState s)
        {
            if (outcome == Win) return 1f + winHpWeight * Math.Max(0, s.pHp) / (float)_pMaxHp;
            if (outcome == Loss) return 0f;
            return StaticValue(s);
        }

        /// <summary>未決着の盤面を閉じた式で見積もる。 ロールアウトを回す前の足切りと、
        /// リロール判断の物差しに使う。
        ///
        /// 「あと何ターンで倒せるか」対「あと何ターンで死ぬか」の競争として見る。
        /// エスカレーションは**その先の平均ターン**で引く ── 現在ターンで引くと
        /// 「あと 8 ターンかかる戦い」の被弾を軒並み過小評価する。</summary>
        private float StaticValue(SimState s)
        {
            if (s.eHp <= 0) return 1f + winHpWeight * Math.Max(0, s.pHp) / (float)_pMaxHp;
            if (s.pHp <= 0) return 0f;

            float expAtkSum = 0.6f * _k * _meanFace;
            float expBlkSum = 0.3f * _k * _meanFace;

            // ===== 充電の値付け (2026-08-10) =====
            //   **充電そのものを効用へ足してはいけない。** ここが返すのは勝率であって、
            //   単位の違う数を足すと確率でなくなる。 充電が実際に何を買うのかを辿って、
            //   **出目の底上げ**という同じ単位へ換算してから足す:
            //       充電 → リロール → 面平均未満のダイスを引き直す → 出目合計が上がる
            //
            //   これが無いと (1) 充電端子へ挿す価値が 0 なので配線が充電を避け、
            //   (2) リロールのコストも 0 なので払える限り振り直す ── どちらも実測で出た。
            //
            //   **使い切れない充電は 0 の価値**なので、 残りターン数で割ってから上限を掛ける。
            //   1 回目のリロールは `個数 × 1` なので、 1 ターンで意味を持つのは全本数まで。
            float dps0 = Math.Max(0.5f, (_pAtkPower + expAtkSum) * _dmgMul);
            float turnsLeft = Math.Max(1f, Math.Min(s.eHp / dps0, rolloutDepth));
            float spendPerTurn = Math.Min(Math.Max(0, s.charge) / turnsLeft, _k);
            float diceGain = chargeRerollGain * _meanFace * spendPerTurn;
            expAtkSum += diceGain * 0.6f;    // 配分の期待値に合わせて攻撃/ブロックへ按分する
            expBlkSum += diceGain * 0.3f;

            float myDps = Math.Max(0.5f, (_pAtkPower + expAtkSum) * _dmgMul);
            float ttk = s.eHp / myDps;

            int midTurn = s.turn + (int)Math.Min(12f, ttk * 0.5f);
            float eAtk = Escalation.GetMultiplier(_escProfile, midTurn)
                       * (_eBaseAtk + _eDiceCount * (_eRollMax + 1) * 0.5f) * HeavyAvg;
            float hisDps = Math.Max(0f, (eAtk - expBlkSum)) * _takenMul;
            // **「いま防げている」は「ずっと防げる」ではない。**
            //   ブロックはダイス由来でほぼ一定なのに、 敵攻撃はエスカレーションで伸び続ける
            //   ── 安全宣言は必ず期限付きである。 999 のままだと x が巨大になり
            //   p が 1 に飽和して、 **戻り値から敵HP (＝削った量) が消える**。
            //   8 ターンの通常戦では実害が無いが、 7層は 4 連戦 40 ターン級で倍率が ×2.3 まで
            //   伸びるので、 そこで削る価値を 0 と評価するのが最も危ない。
            //
            //   実測 (2026-08-22・遺物あり・7層のみ): Super は Optimal と
            //   **実効倍率が同一 (5.414 対 5.460)** なのに atkBase が 12.7% 低く
            //   (攻撃端子出目 22.40 対 25.01)、 その差を充電端子へ回していた。
            //   結果 7層のターン数が 8.6% 増え、 エスカレーションを余計に浴びて負ける。
            float ttdCap = ttdCapTurns > 0f ? ttdCapTurns : 999f;
            float ttd = hisDps > 0.01f ? s.pHp / hisDps : ttdCap;
            if (ttd > ttdCap) ttd = ttdCap;
            // [計装] 「自分は安全」と見積もった局面か。 このとき x が巨大になり p が 1 に飽和して、
            //   戻り値から **敵HP (＝与えたダメージ) が消える**。 その頻度を Census が読む。
            _lastSafeProjected = hisDps <= 0.01f;

            // 差が 0 のとき 0.5、 2 ターン差でおよそ 0.8 になる緩い S 字
            float x = (ttd - ttk) * 0.7f;
            float p = 0.5f + 0.5f * (x / (1f + Math.Abs(x)));
            return p * (1f + winHpWeight * s.pHp / (float)_pMaxHp);
        }

        // ============================================================
        //  小物
        // ============================================================

        // ============================================================
        //  並列実行 (無損失・決定的)
        // ============================================================
        //  配線候補どうし・リロール案どうしは**完全に独立**で、 しかも共通乱数のため
        //  それぞれが自分の種から振り直す ── 実行順序が結果に影響しない。
        //  さらに Snapshot() が盤面を値型フィールドへ写し取っているので、 探索中は
        //  CombatManager にも PassiveSkillManager にも触らない (YachtRoles は純粋関数)。
        //  **だから Unity のメインスレッド制約に引っかからない。**
        //
        //  スクラッチはインスタンス状態なので、 **AI ごと複製**してワーカーに配る。
        //  引数を引き回す形にしなかったのは、 触る関数が 8 つに及び、 取り違えたときに
        //  「静かに壊れる」形になるため。

        [NonSerialized] private SuperCombatAI[] _workers;
        [NonSerialized] private bool _isWorker;

        /// <summary>並列度。 0 なら論理コア数 − 1 (メインスレッドぶんを残す)。</summary>
        public int maxParallelism = 0;

        /// <summary>ワーカーを用意し、 読み取り専用のスナップショットを配る。
        /// **可変状態は配らない** ── 較正 (_dmgMul 等) は値をコピーするだけで、
        /// ワーカー側は更新しない (Observe を呼ばない)。</summary>
        private void SyncWorkers(int need)
        {
            if (_isWorker || need <= 1) return;
            if (_workers == null || _workers.Length < need)
            {
                var next = new SuperCombatAI[need];
                if (_workers != null) Array.Copy(_workers, next, _workers.Length);
                for (int i = 0; i < need; i++)
                    if (next[i] == null) next[i] = new SuperCombatAI { _isWorker = true };
                _workers = next;
            }
            for (int i = 0; i < need; i++) CopySnapshotTo(_workers[i]);
        }

        private void CopySnapshotTo(SuperCombatAI w)
        {
            w.rolloutCount = rolloutCount; w.candidateWirings = candidateWirings;
            w.tieExpandCandidates = tieExpandCandidates;
            w.chargeRerollGain = chargeRerollGain;
            w.rolloutDepth = rolloutDepth; w.bossRolloutDepth = bossRolloutDepth;
            w.ttdCapTurns = ttdCapTurns;
            w.rerollSamples = rerollSamples; w.rerollMaxAttempts = rerollMaxAttempts;
            w.exactRerollMaxDice = exactRerollMaxDice;
            w.rolloutTtkWeight = rolloutTtkWeight;
            w.rolloutTakenWeight = rolloutTakenWeight;
            w.rolloutChargeWeight = rolloutChargeWeight;
            w.winHpWeight = winHpWeight;
            w._k = _k; w._pMaxHp = _pMaxHp; w._pAtkPower = _pAtkPower; w._faces = _faces;
            w._diceMax = _diceMax; w._meanFace = _meanFace;
            w._critBase = _critBase; w._critMul = _critMul;
            w._eBaseAtk = _eBaseAtk; w._eDiceCount = _eDiceCount; w._eRollMax = _eRollMax;
            w._eMaxHp = _eMaxHp; w._escProfile = _escProfile;
            w._heavyPeriod = _heavyPeriod; w._heavyMul = _heavyMul; w._heavyWindupMul = _heavyWindupMul;
            w._suppressTerm = _suppressTerm; w._isBoss = _isBoss; w._sealedRoles = _sealedRoles;
            w._termCap = _termCap; w._specialLimit = _specialLimit;
            w._chargeMax = _chargeMax; w._chargePerTurn = _chargePerTurn;
            w._spec = _spec; w._pierceMul = _pierceMul;
            w._sealPeriod = _sealPeriod; w._sealedNow = _sealedNow;
            w._dmgMul = _dmgMul; w._takenMul = _takenMul;
            w._nowMods = _nowMods;
            w._secondAtk = _secondAtk;   // 2 体戦: 転記し忘れるとワーカーだけ 2 体目を見落とす
            w._obsTurns = _obsTurns; w._obsHalve = _obsHalve; w._obsExecute = _obsExecute;
            w._obsShieldTurns = _obsShieldTurns; w._obsBlaze = _obsBlaze;
            w._obsShieldSum = _obsShieldSum; w._obsDodgeSum = _obsDodgeSum;
            w._obsReflectSum = _obsReflectSum;
            w.EnsureBuffers();
            w.BuildCodes();
        }

        private int Parallelism(int need)
        {
            // [撤回] 2026-08-15 に −2 (描画用にもう 1 コア空ける) を試して**倍以上遅くなった**。
            //   描画が詰まる原因はコアの奪い合いではなく、 **メインスレッドがバッチ本体だから**
            //   ── Parallel.For は呼び出し元 (＝メインスレッド) をブロックし、
            //   コルーチンの yield は 1 ラン に 1 回しか来ない。 Super の 1 ラン は 0.5 秒級なので
            //   Editor は毎秒 1〜2 フレームしか描けない。 並列度を下げると 1 ラン が長くなり、
            //   **FPS もスループットも両方悪化する**。 描画を戻したいならヘッドレスか yield 粒度の話。
            int cap = maxParallelism > 0 ? maxParallelism : Math.Max(1, Environment.ProcessorCount - 1);
            return Math.Min(cap, need);
        }

        // ============================================================
        //  列挙の下ごしらえ (無損失)
        // ============================================================

        /// <summary>合法な割り当ての表。 **ダイスの出目に依存しない** ── 端子上限〈不器用〉と
        /// 特殊端子の接続制限だけで決まるので、 スナップショットごとに 1 回作れば足りる。
        /// 呼び出しのたびに 243 回の <c>% / /</c> と合法判定を回すのをやめるため。</summary>
        private DiceTerminal[][] _codes;
        private long _codesKey = -1;

        private void BuildCodes()
        {
            int termKinds = _specialLimit > 0 ? 4 : 3;
            long key = ((long)_k << 24) | ((long)termKinds << 16) | ((long)_specialLimit << 8) | (uint)_termCap;
            if (key == _codesKey && _codes != null) return;
            _codesKey = key;

            int total = 1; for (int i = 0; i < _k; i++) total *= termKinds;
            var list = new List<DiceTerminal[]>(total);
            var tmp = new DiceTerminal[_k];
            for (int code = 0; code < total; code++)
            {
                int t = code;
                for (int i = 0; i < _k; i++) { tmp[i] = (DiceTerminal)(t % termKinds); t /= termKinds; }
                if (!Legal(tmp)) continue;
                var keep = new DiceTerminal[_k];
                Array.Copy(tmp, keep, _k);
                list.Add(keep);
            }
            _codes = list.ToArray();
        }

        /// <summary>同値ダイスの入れ替えでしかない割り当てを弾く。 **結果は完全に同一**なので、
        /// 評価する意味がない ── 2 と 2 のどちらを攻撃へ置いても、 端子の組も合計も変わらない。
        ///
        /// 役システムは対・束が出やすいので当たりが多い。 対が 1 組あるだけで候補が 3 分の 2 に、
        /// 束なら 6 分の 1 になる。 判定は「同値が隣り合う位置で端子番号が非減少」だけ。</summary>
        /// <summary>〈綻び〉: 封印された端子を使う配線は選べない。
        /// **提案しても本体が追い出す**ので、 候補から外さないと探索がずれる。</summary>
        private bool UsesSealed(DiceTerminal[] w)
        {
            if (_sealedNow < 0) return false;
            for (int i = 0; i < _k; i++) if ((int)w[i] == _sealedNow) return true;
            return false;
        }

        private bool Canonical(DiceTerminal[] w, int[] dice)
        {
            for (int i = 1; i < _k; i++)
            {
                int a = _valSort[i - 1], b = _valSort[i];
                if (dice[a] == dice[b] && (int)w[b] < (int)w[a]) return false;
            }
            return true;
        }

        /// <summary>出目降順に整列した添字。 <see cref="Canonical"/> は同値が隣接している前提。</summary>
        private int[] _valSort;
        private long _valSortKey = -1;

        private void PrepareHand(int[] dice)
        {
            EnsureHandRoles(dice);
            if (_handKey == _valSortKey) return;
            _valSortKey = _handKey;
            for (int i = 0; i < _k; i++) _valSort[i] = i;
            for (int i = 1; i < _k; i++)
            {
                int key = _valSort[i], j = i - 1;
                while (j >= 0 && dice[_valSort[j]] < dice[key]) { _valSort[j + 1] = _valSort[j]; j--; }
                _valSort[j + 1] = key;
            }
        }

        /// <summary>手札役を <see cref="_handBuf"/> へ用意する。 **手札が変わった時だけ**評価し直す。
        ///
        /// 手札役は配線に一切依存しないのに、 初版は 243 通りの割り当てごとに評価し直していた。
        /// 役判定の計算量の約 6 割が手札役 (<c>MaxSameCount</c> 2 回 + <c>LongestRun</c> 2 回) なので、
        /// ここだけで探索が 2 倍以上速くなる。 **結果は 1 ビットも変わらない。**
        ///
        /// 呼び出し側の規律に頼らず、 手札そのものを鍵にして自動判定する ──
        /// 「呼び忘れると手札役が静かに消える」形にすると、 いつか必ず踏む。</summary>
        private void EnsureHandRoles(int[] dice)
        {
            // 出目は高々 2 桁なので 5 ビットずつ詰める。 k が大きい時は諦めて毎回評価する。
            long key = 0;
            if (_k <= 12)
            {
                for (int i = 0; i < _k; i++) key |= (long)(dice[i] & 31) << (5 * i);
                key |= 1L << 62;                       // 鍵ゼロと「未計算」を区別する
                if (key == _handKey) return;
            }
            _handKey = key;
            _handBuf.Clear();
            YachtRoles.EvaluateHand(dice, _handBuf);
        }
        private long _handKey;
        /// <summary><see cref="TurnValue"/> の最良手が今ターン撃破したか。 メモ命中時は false (安全側)。</summary>
        private bool _tvWon;
        /// <summary>今ターンの盤面修飾 (予告そのまま)。 <see cref="Rollout"/> の先のターンでは使わない。</summary>
        private TurnMods _nowMods;

        /// <summary>**本 AI 専用の乱数**からダイスを 1 個振る。 GameRng は触らない。</summary>
        private int RollFace()
            => _faces != null ? _faces[_rng.Next(0, _faces.Length)] : _rng.Next(1, _diceMax + 1);

        private int NextSeed() => _rolloutSeed = (_rolloutSeed * 1103515245 + 12345) & 0x7FFFFFFF;

        private static int MaskOf(HashSet<RoleKind> used)
        {
            int m = 0;
            if (used != null) foreach (var r in used) m |= 1 << (int)r;
            return m;
        }

        private static int SumOf(int[] v, int n)
        {
            int s = 0; for (int i = 0; i < n; i++) s += v[i];
            return s;
        }

        /// <summary>〈対〉の底上げ量。 **正本は <see cref="YachtRoleEffects"/>.PairBonus** ──
        /// ここはロールアウト用の写しなので、 向こうを変えたら必ずこちらも変える。
        /// ずれると探索が実際と違う効果を最大化する (全ターンで目的関数が狂う)。</summary>
        private static int PairBonus(int[] group, int n)
        {
            int sum = 0;
            for (int i = 0; i < n; i++)
            {
                int c = 0;
                for (int j = 0; j < n; j++) if (group[j] == group[i]) c++;
                if (c >= 2) sum += group[i];
            }
            return (sum + 1) / 2;
        }

        // 端子役の判定へ渡す「ちょうどその本数の配列」。 長さで判定が変わる (分布系の最小本数) ため、
        // 使い回しの固定長バッファをそのまま渡すことはできない。
        private readonly int[][] _grpCache = new int[16][];
        private void CopyInto(int[] src, int n, out int[] dst)
        {
            if (_grpCache[n] == null) _grpCache[n] = new int[n];
            dst = _grpCache[n];
            Array.Copy(src, dst, n);
        }
    }
}
