namespace AutoTest
{
    /// <summary>
    /// SuperV3 の実験スイッチ (2026-11-05)。 <b>SuperV3 = Optimal + このスイッチ</b>。
    /// 全部 OFF (既定) のとき、 Optimal と<b>1 ランも違わず</b>同じ結果になる (A/B の基準アーム。 検算は同じシードの完全一致で行う)。
    ///
    /// <para>改善案は 1 案 1 フィールドで足し、 Optimal の同じ関数の中で <c>if (v3.xxx)</c> として分岐させる。
    /// コピーを別に持つと、 Optimal を直すたびに片方が古くなる (Super がそうなった)。 A/B は
    /// <c>Tools/v3_arm.txt 風の設定</c> (<c>v3.xxx=True</c>) で 1 本ずつ入れて、 同じシード・同じ学習状態で対にして比べる。</para>
    ///
    /// <para>効いた案は既定を ON にして Optimal 本体へ畳み、 効かなかった案はここから消す (記録は design-rejects へ)。</para>
    /// </summary>
    [System.Serializable]
    public class V3Flags
    {
        // 実験ごとにフィールドを足す (全部 既定 OFF)。

        /// <summary>E1: リロールを「いまの手の最良効用」と「振り直した手の期待最良効用 − 充電の費用」の比較で決める。
        /// 既定の規則 (面平均を下回る合計なら振る) は手の中身を見ないので、 配線の良し悪しとずれる。
        /// 役が成立した手を捨てない規則と、 充電の予備 (RerollChargeReserve)・1 ターン 1 回は残す。</summary>
        public bool rerollEv;

        // ---- 効用の係数のスイッチ (1 = 既定の値のまま・0 = 無効)。 掃引して A/B にかける ----

        /// <summary>E4: 1 ターン延ばす罰 (<c>TimeCostPerTurn</c> = 4.0) の倍率。</summary>
        public float timeCostMul = 1f;
        /// <summary>E3: 充電の効用 (<c>wChg × 2</c>) の倍率。</summary>
        public float chargeMul = 1f;
        /// <summary>E5: このターンの被ダメージ 1 あたりの罰。 **2026-11-05 に既定を 4 へ畳んだ** (Optimal の基準そのもの・0 で畳む前の挙動)。
        /// 掃引では 0.5 → 34.3% / 1 → 39.8% / 2 → 41.7% / 4 → 43.2% / 8 → 43.4% / 16 → 44.6% / 32 → 44.9% (畳む前の基準 25.0%)。
        /// 『HP は戦闘をまたいで持ち越す資源』という最低限の知識で、 先読みではない。 (旧: 効用は HP を失うこと自体のコストを持たない
        /// (防ぎ切りの規則だけが穴埋めしている)。 ラン全体で HP は戦闘をまたいで持ち越すので、 失った分は後で響く。</summary>
        public float takenCost = 4f;
        /// <summary>[2026-11-10 A/B] 防ぎ切りの規則 (+1000) を、 予告の外のダメージがある戦闘では付けない。
        /// 戦闘の中で「前のターンに見込んだ被ダメより実際の HP の減りが大きかった」ら、 その戦闘には予告の外のダメージがあるとみなす
        /// (敵のパッシブの固定ダメージ・毒・軽減できないダメージ・2 体目など)。 ランをまたいで覚えない (決定性)。
        /// 計装: 全ターンで規則を選んでも被ダメ 0 で終わる戦闘は 42% だけ・2 層 13%・4 層 0% だった。</summary>
        public bool fbCheckUnavoid;   // 2026-11-10: −0.2 (有意でない)。 hpLedger の既定化で防ぎ切りの規則そのものを使わなくなった
        /// <summary>[2026-11-10 A/B] 戦闘の中の通貨を HP にそろえる: 効用 = −(今ターンの被ダメ + 撃破までの残りターンで受ける被ダメの見込み) + 既存の特殊な項。
        /// 与ダメは「撃破を早めて、 その先のターンの (エスカレーションで伸びる) 被ダメを消す」分だけ価値になる。
        /// 手で決めた重み (与ダメ・ブロック・時間の罰・被ダメの罰・防ぎ切りの +1000・撃破の +50) を使わない。
        /// 先のターンは「全部攻撃に挿す」前提で、 与ダメは今の手を全部攻撃にした時の値を面の平均で均した見込み。
        /// <para><b>2026-11-10 に既定を ON へ畳んだ</b> (Optimal・Super の基準そのもの)。 3 シード × 5000 ランの対で
        /// 割合 0.2 が +1.81 ± 0.49 (z +3.71)・0.5 が +1.14 ± 0.49・1.0 は −2.6 (先のターンを全部受ける前提は払いすぎ)。
        /// 終盤 (ヴェスカ) の死亡が減る。 false で畳む前の手置きの重み (与ダメ・ブロック・時間の罰・被ダメの罰 4・防ぎ切りの +1000) に戻る。</para></summary>
        public bool hpLedger = true;
        /// <summary>hpLedger の「先のターンの被ダメの見込み」に掛ける割合 (1 = 先も全部攻撃して全部受ける前提)。 既定 0.2 (2026-11-10)。</summary>
        public float ledgerFutureShare = 0.2f;

        /// <summary>E8: 2 ターン先読み。 各配線 w の値 = 今ターンの効用 U_now(w) + 次ターンの最良効用の期待値 E[max U_next]。
        /// 次ターンの盤面は w でこのターンを解いた後 (敵 HP・自 HP・シールド・不滅の刃の連続数・堅守の準備・敵の防御・ターン+1・次ターンの敵攻撃) で、
        /// 効用は同じ関数 (<c>OptimalUtility</c>)。 次の出目は面の多重集合の全列挙の期待値 (GameRng を使わない)。 割引なし・新しい重みなし。
        /// 今ターンで撃破・死亡する配線は先読みせず今の効用のまま。 リロールは次ターンの見積もりに入れない。</summary>
        public bool lookahead2;
        /// <summary>E8 をボス戦 (<c>BossIds.IsBoss</c>) だけに掛ける (既定 true)。 false なら全戦闘。</summary>
        public bool lookaheadBossOnly = true;
        /// <summary>S2: 大きい攻撃の予告に、 激しく守る。 リロールで守れる目を引きにいき (充電の予備も使う・1 ターン 2 回)、 被ダメの罰を 3 倍にする。</summary>
        public bool superDefense;
        /// <summary>S3: 消耗品を読んで使う。 護符は開幕に使わず (毎ターン半減するので大技の前に消える)、 予告を見て足りない分を埋める最小のものを使う。 致死なら回復薬。</summary>
        public bool superConsumables;
        /// <summary>S4: 航行。 次に向かう未知の点だけ偵察し、 見えた戦闘名の危険度で避ける (敗北率 10% 以上は基本的に避ける)。</summary>
        public bool superNav;
        /// <summary>S4: この敗北率以上の戦闘名は HP に関わらず避ける。</summary>
        public float avoidLoseRate = 0.10f;
        /// <summary>S6: 層の回り方の計画 (AutoRunner.Macro)。 クリアの対数オッズの増減で 3 手先まで比べ、 得にならなければ層を出る。</summary>
        public bool macroPlan;
        /// <summary>[診断] 丸めた序列の何が効くかの分解 (2026-11-06)。 同じ段の中で安い方を選ばない。</summary>
        public bool tierNoPriceTie;
        /// <summary>[診断] 足切りを通った候補の順番を ΔPower/G (手持ちを見た評価) だけで決める。</summary>
        public bool orderByEffOnly;
        /// <summary>A: 段階 3 (通常購入・最後の店) を値の高い順にし、 足切り (PowerCuts[3]) 未満は買わない。 旧は棚の左から値を見ずに全部。</summary>
        public bool phase3Ranked;
        /// <summary>軸に絞る買い方 (2026-11-07・Super のとき on)。 その軸の品を十分に持っていたら、 ① 数を参照・②【規則】・誓約を
        /// 最優先で買う (ITT は平均で測るので、 絞った構成でだけ価値が出るこれらの品を低く出す)。 誓約は、 封じる軸の品を
        /// ほとんど持っていないときだけ買い、 足りないうちは買わない。 しきい値は下の 4 つ。</summary>
        public bool axisCommitBuy;
        public int commitCountAt = 4, commitRuleAt = 6, commitOathAt = 8, commitOathMaxSealedOwned = 2;
        /// <summary>[診断] 同点の割り方を ID 順にする (意味の無い順)。</summary>
        public bool tieById;
        /// <summary>同じ目の出目パーツを持っていたら、 差し替えの差分 (新しい面 − 持っている面) で評価する (2026-11-06 に全 BOT の既定へ)。
        /// ITT の値は「その面を新しく得た時」の効果なので、 上位版を過大に見て重ね買いしていた (無段階の Super 16.3 → 22.3%・+11.23σ)。</summary>
        public bool partDelta = true;
        /// <summary>[診断] 出目パーツの準パワーから引く量。 3 段の強さが「パッシブとパーツの順番」かを確かめる。</summary>
        public float partPenalty;
        /// <summary>[診断] 足切りは丸める前の値で判定する (順位だけ丸める)。</summary>
        public bool tierGateRaw;
        /// <summary>[診断] 順位は丸める前の値で決める (足切りだけ丸める)。</summary>
        public bool tierOrderRaw;
        /// <summary>[2026-11-10] 配線の効用の主攻撃に与ダメの倍率を読む (攻撃+N・与ダメ%・会心の期待値・端子調律・〈電流〉の②・最低保証・狂暴化)。
        /// −1 = 技量に従う (Super で ON・それ以外は OFF) / 0 = OFF (旧の式: 素の攻撃力 + 攻撃の出目 − 防御) / 1 = ON。
        /// 実効値は <see cref="AutoRunner.ReadOutgoingMultEffective"/>。 配線の後で積まれる分 (OnWired・OnPreDealDamage・OnCriticalCheck・追撃・役・脆弱) は入らない。</summary>
        public int readOutgoingMult = -1;
        /// <summary>[2026-11-10 A/B] 配線の効用の主攻撃の見積もりに、 実績の比 (実際に入った主攻撃 ÷ 配線時点の見積もり) を掛ける。
        /// 比は同じ戦闘 (ヴェスカの連戦は通算) の攻撃したターンの累計 ÷ 累計。 その戦闘に実績が無いうちは、 そのランの直前までの戦闘の累計の比 (無ければ 1)。
        /// 配線の後で積まれる分 (OnWired の攻撃加算・OnPreDealDamage の倍率・会心・役の追撃) を、 式で読まずに実績で埋める。
        /// 掛けるのは主攻撃の項だけ (黎明・〈虚空〉の刻み・特殊端子の寄与には掛けない)。 hpLedger の「撃破までの見込み」(dFull) にも流れる。
        /// 実績は CombatStats.DamageBreakdown[7] の増分 (predTrace と同じ「実際」)。 ランをまたいで持ち越さない。</summary>
        public bool mainCorr;
        /// <summary>ヴェスカ〈停滞する時間〉を持つ敵に、 停滞を積む配線 (攻撃の本数 &lt; ブロックの本数) を選ばせない (2026-11-11・既定 ON)。 例外は AutoRunner.Combat の MutualWiringPolicy。</summary>
        public bool noStagWire = true;   // 2026-11-11 既定 ON (停滞の罰を帳簿が薄めて BOT が自分で p4 を重くしていた)
    }
}
