using UnityEngine;
using GameLoop;

namespace MetaProgression
{
    /// <summary>
    /// 挑戦デバフ (docs/GAME.md §15-2 **v3.0**) を各システムへ届ける中央ヘルパー。
    ///
    /// 2026-08-04: 旧 19 軸 100 点案の v1 数値を破棄し、 **10 軸 50 点方式**へ全面移行した。
    /// **呼び出し元を無変更に保つため、 廃止した軸のメソッドは残して中立値を返す。**
    /// （削除すると 8 ファイルが同時に壊れ、 移行の可否判断ができなくなる）
    ///
    /// 効果値 (×0.90 / −10% など) を持つのは**このファイルだけ**。
    /// <see cref="ChallengeCatalog"/> は「どの Tier が存在するか」しか知らない。
    ///
    /// **v3.0 の設計制約** (docs/challenge-debuff-plan.md §3):
    ///   - 宝箱・前哨基地は**削除しない**。 低効率でも経路を残す。
    ///   - 5 層撃破による停止・減衰は行わない。 選択効果はラン終了まで継続する。
    ///   - 敵の無限回復 / 毎T ランダム封印 / 完全ランダム進行は採用しない。
    /// </summary>
    public static class MetaDebuffApplicator
    {
        /// <summary>解決済みの挑戦構成。 メタ未初期化なら空構成。</summary>
        public static ResolvedChallenge C
            => MetaProgressManager.Instance?.State?.Challenge ?? ResolvedChallenge.None;

        /// <summary>現在の挑戦スコア (0..50)。 ラン結果へ記録する。</summary>
        public static int Score => C.Score;

        private static int Tier(ChallengeAxis axis) => C.Tier(axis);

        /// <summary>Tier 1..3 に対応する値を引く。 tier=0 なら none。
        ///
        /// **引数位置 = Tier 番号 = 点数** (v4.1)。 段を飛ばしている軸
        /// (例: 〈厚い皮膚〉は T2/T3 のみ) は、 **到達不能なスロットに none と同じ値**を
        /// 置くこと ── 手が滑ったときに「効果なし」として現れ、 黙って別 Tier の値が
        /// 出ることがない。 カタログの `tiers` と引数位置は目視で照合できる。</summary>
        private static float Pick(ChallengeAxis axis, float none, float t1, float t2, float t3)
        {
            switch (Tier(axis)) { case 1: return t1; case 2: return t2; case 3: return t3; default: return none; }
        }

        private static int PickI(ChallengeAxis axis, int none, int t1, int t2, int t3)
        {
            switch (Tier(axis)) { case 1: return t1; case 2: return t2; case 3: return t3; default: return none; }
        }

        // ============================================================
        //  A. 生存圧 — 脆弱な肉体 / 練度不足  (T4: 破綻)
        // ============================================================

        /// <summary>最大 HP の倍率。 T1 ×0.94 / T2 ×0.91 / T3 ×0.89。
        /// **v1 の定数減算 (−5/−15/−30) から倍率へ変更した** ── 定数だと整備パネルで
        /// 最大 HP を伸ばすほど相対的に薄まり、 高メタほどデバフが効かなくなるため。
        /// **HP が 1 を下回らないよう適用側で clamp すること。**        ///
        /// **2026-08-09 (ADR-0010 後)**: 「技量で答えられない軸」として緩和した。
        /// 最大HP・与ダメ・会心率の乗算は、 役システムで厚くした天井をそのまま削るので
        /// **技量帯を閉じてしまう**。 難易度は配線・役・リロールで対処できる圧へ寄せる (§15-2)。</summary>
        public static float GetMaxHpMultiplier()
            => 1f;   // v4.0: 最大HP 乗算は廃止 (〈長引く負傷〉へ差し替え)

        /// <summary>[廃止] v1 の定数減算。 v3.0 は <see cref="GetMaxHpMultiplier"/> が本体なので常に 0。</summary>
        public static int GetMaxHpDelta() => 0;

        /// <summary>プレイヤー与ダメージ倍率。 T1 ×0.94 / T2 ×0.91 / T3 ×0.89。
        /// 2026-08-05: 10/20/30 → 10/15/20 へ圧縮。 2026-08-09: さらに緩和 (下記)。
        /// **T1 をほぼ据え置く**ので低スコア帯の階段は変わらず、 高スコア帯だけが緩む。        ///
        /// **2026-08-09 (ADR-0010 後)**: 「技量で答えられない軸」として緩和した。
        /// 最大HP・与ダメ・会心率の乗算は、 役システムで厚くした天井をそのまま削るので
        /// **技量帯を閉じてしまう**。 難易度は配線・役・リロールで対処できる圧へ寄せる (§15-2)。</summary>
        public static float GetPlayerDamageMultiplier()
            => 1f;   // v4.0: 与ダメ 乗算は廃止 (〈不器用〉〈穴の空いた鞄〉へ差し替え)

        /// <summary>[廃止] 旧経路の互換。 % 乗算が本体なので常に 0。</summary>
        public static int GetPlayerDamageReduction() => 0;

        // ============================================================
        //  v4.0 で新設した「数える制約」(2026-08-10)
        //  数値乗算はプレイヤーの天井をそのまま削って技量帯を閉じるので、
        //  難易度は **できることの数** を削る形へ寄せた (docs/GAME.md §15-2)。
        // ============================================================

        // [削除] LingeringWoundThreshold / LingeringWoundRunCap (2026-08-17)
        //   2026-08-10 のリワークで〈長引く負傷〉は「1 戦闘で閾値超の被弾 → 固定減・上限あり」から
        //   **「受けた累計ダメージの N% を最大HP から引く」** へ変わり、 閾値も上限も持たなくなった。
        //   それでも定数だけが残り、 AutoRunner が「閾値 40% ・上限 20」とログへ出し続けていたため、
        //   実測の平均損失 32.4 と矛盾する表示になっていた (上限が壊れていたのではなく、存在しなかった)。
        //   **効いていない定数を残すと、 次に読む人がそれを仕様だと信じる。** 消す。
        //   実効値は GetLingeringWoundRatio() ただ 1 つ。

        /// <summary>〈長引く負傷〉の最大HP恒久減。 T1 −2 / T2 −3 / T3 −4。</summary>
        /// <summary>〈長引く負傷〉: **ラン中に受けた累計ダメージのこの割合だけ最大HP を失う**。
        /// T1 4% / T2 8% / T3 12%。 0 = 無効。
        ///
        /// 2026-08-10 リワーク。 旧効果は「1 戦闘で最大HP の N% 超を被弾したら固定 −2/−3/−4」で、
        /// 閾値 60% では 300 ラン で 7F差分 ±0.3 / p=1.000 の完全な死に軸だった。 閾値を 40% へ
        /// 下げて発動率 98% まで上げても、 1 回あたりが小さく 1000 ラン で −1.6〜−2.2pt 止まり。
        /// **発動していないのではなく、 発動が結果に届いていなかった。**
        ///
        /// 新効果は閾値も回数も持たない。 受けたダメージがそのまま比例で最大HP を削るので、
        /// 「削られながら勝つ」を繰り返すほど確実に重くなる ── 軸の意図そのままの形になる。</summary>
        /// 2026-08-17: 4/8/12% → **3/6/9%**。 1000 ラン単軸スイープで T1 が 1pt あたり −5.60pt と
        /// 全 23 段中で最悪の費用対効果だった (2 位 宿屋連合 −4.00 / 中央値 −2.7 前後)。
        public static float GetLingeringWoundRatio()
            => Pick(ChallengeAxis.長引く負傷, 0f, 0.03f, 0.06f, 0.09f);

        /// <summary>〈穴の空いた鞄〉: 消耗品の同時所持上限。 T1 5 個 / T2 3 個。 0 = 制限なし。
        /// 3/2 個は1000ランで −3.7/−7.5ptと、1ptあたり約−3.7ptで重すぎたため1枠ずつ緩和。</summary>
        public static int GetConsumableCarryCap()
            => PickI(ChallengeAxis.穴の空いた鞄, 0, 5, 3, /*T3 なし*/ 0);

        /// <summary>〈綻び〉: **N ターンごとに端子が 1 つ封印される**。 0 = 無効。
        /// 返すのは周期 N (T1 3ターン / T2 2ターン / T3 毎ターン)。
        ///
        /// 2026-08-11、 旧〈不器用〉から軸ごと差し替え。 過去 2 案はどちらも死んだ:
        ///   ① 端子の種類数の上限 → 特殊端子が無ければ端子は 3 種しかなく、 3 への制限は
        ///      何も禁じていなかった (p=0.607)
        ///   ② 1 ターンに動かせる配線の本数 → 基準 26.6% では効いたのに、 BOT を強くして
        ///      基準 30.9% にしたら消えた (p=0.68)。 強いビルドは元々組み替えていない
        ///
        /// **失敗の共通点は「強いビルドほど素通りする」こと。** 封印は端子そのものを
        /// 使えなくするので、 どんなビルドでも必ず選択肢が 1 つ減る。
        ///
        /// **封印先は輪番で、 予告に載せる** (ADR-0009 柱3 完全情報テレグラフ)。 乱数で決めると
        /// 「読んで組んだ最善手が運で壊れる」形になり、 予告を読む意味が薄れる。
        /// 輪番なら「次は攻撃が塞がる」と分かるので、 **前もって組み立てる**判断になる。</summary>
        /// 2026-08-11: 4/3/2でも −4.1/−6.3/−8.3ptと全段が重かったため、8/5/3へ緩和。
        public static int GetTerminalSealPeriod() => PickI(ChallengeAxis.綻び, 0, 8, 5, 3);

        /// <summary>そのターンに封印される端子。 -1 = 封印なし。
        /// <paramref name="terminalKinds"/> は現在使える端子の種類数 (特殊端子の有無で 3 or 4)。</summary>
        public static int GetSealedTerminal(int turn, int terminalKinds)
        {
            int period = GetTerminalSealPeriod();
            if (period <= 0 || turn <= 0 || terminalKinds <= 1) return -1;
            if (turn % period != 0) return -1;
            // 何度目の封印か → 輪番。 攻撃 → ブロック → 充電 (→ 特殊) の順に塞がる。
            int nth = turn / period;
            return (nth - 1) % terminalKinds;
        }

        /// <summary>〈通行料〉: **層を移動するたびに所持金の10%を失う**。 0 = 無効。
        ///
        /// 2026-08-10 リワーク (旧〈売り渋り〉: 陳列 −2枠 ＋ ランダム2枠 ×1.5)。
        /// 旧効果は 1000 ラン で **−0.1pt / p=1.000** の完全な無効だった ── 陳列 12 枠に対し
        /// BOT は 1 ラン 20 個買うので、 2 枠減っても代わりの品が並ぶだけで何も起きない。
        ///
        /// 新効果は**退蔵への課税**。 実測で BOT は平均 57.9G を使い残しており、
        /// 貯めた金が層を跨ぐと目減りするなら「いま買うか、 次の店まで待つか」の判断が生まれる。
        /// 定額ではなく割合なので、 貯めるほど損が大きい ＝ 名前どおりの通行料になる。
        ///
        /// **逆効果の可能性を承知で入れている。** 今日 2 度、 デバフが BOT の下手を矯正して
        /// 勝率を上げた (不器用 +6.0 / 旧売り渋り +9.4)。 本軸は「余らせるな」という矯正でもあるので、
        /// 余らせているのが下手なら勝率が上がりうる。 **実測で符号を確認すること。**</summary>
        public static float GetFloorTollRatio()
            => Pick(ChallengeAxis.通行料, 0f, 0.10f, /*T2 なし*/ 0f, /*T3 なし*/ 0f);

        /// <summary>〈宿屋連合〉: 前哨基地で休憩するたびに失う GOLD。
        /// 2026-08-10: 5 → 2。 1pt で −10.0pt (1pt当り −10.00) と、 3pt 軸に匹敵する重さだった。
        /// 実測では支出Gが 81→68 に落ち、 装備力の低下が全軸中で最大 (67.5→62.7)。</summary>
        /// 2026-08-10 経済リスケール: 支出側 ×5 (2 → 10)。
        public static int GetRestGoldCost() => C.Has(ChallengeAxis.宿屋連合) ? RestGoldToll : 0;

        /// <summary>前哨基地の宿代。 **発動箇所は前哨基地のみ** (GameManager の回復処理 1 箇所)。
        /// 2026-08-10: 10 → 3 → 5。 3G では 1000 ラン で −0.9pt / p=0.494 の死に軸だった。 実測で 1pt にして単独 −13.0pt (1pt当り −13.00) と全軸で最悪の
        /// 費用対効果だった。 コメントの「5 → 2」はデノミ巻き戻し前の記述で、 実値と食い違っていた。</summary>
        /// 2026-08-17: 5 → **4**。 単軸スイープで 2pt −8.0pt (1pt当り −4.00) と割高だったため。
        public const int RestGoldToll = 4;

        /// <summary>[廃止] T4-A 破綻の HP 切り詰め。 常に 1 (無効)。
        /// 2026-08-11: 「各層最初の戦闘で HP を 86% へ」は 4pt にして **−0.6pt** の実質無料だった。
        /// 切り詰めも回復減も**プレイヤーが何も判断しない純粋な減算**で、 同カテゴリの基礎軸
        /// (被ダメ比例・所持枠・回復倍率) と質が重なり、 T4 が「もう一段」でしかなかった。
        /// 下記 <see cref="GetBreakdownThreshold"/> の**不可逆な状態変化**へ差し替え。</summary>
        public static float GetFloorEntryHpCapRatio() => 1f;

        /// <summary>[廃止] T4-A 破綻の「HP 閾値割れで最大HP 半減」。 常に 0 (無効)。
        ///
        /// 2026-08-11 実測で棄却。 1 回限り版で 1000 ラン 中 590 回、 複数回版で 981 回
        /// 発動してなお T4 コストは **−2.2pt / −2.7pt** しか出なかった。 発動率を
        /// 1.7 倍にして 0.5pt しか動かない ── **HP 20% を割る局面は既に負けが決まっており、
        /// そこを殴っても勝敗の分岐にいない**。 回数でも閾値でもなく、 効く場所が違った。
        /// 下記 <see cref="GetVoidTileChance"/> の**有利マス空白化**へ差し替え。</summary>
        public static float GetBreakdownThreshold() => 0f;

        /// <summary>T4-A〈破綻〉: 有利マス (休憩/秘宝/交換) を踏んだとき、15%で **その恩恵が
        /// 丸ごと無かったことになる**確率。 0 = 無効。
        ///
        /// マップ生成時ではなく**踏んだ瞬間**に判定する。 盤上の見た目は普通の有利マスの
        /// ままなので、 プレイヤーは「そこへ向かう」という判断を必ず先に済ませており、
        /// 空白だと分かるのは到着してから ── 迂回で避けられない代わりに、
        /// **どのマスを踏むかの期待値計算そのものが目減りする**形になる。
        ///
        /// ショップは対象外。 ショップ潰しは D経済 (破産/搾取経済) の領分で、
        /// A生存圧 の T4 が同じ質の罰を二重に持つことになる。
        ///
        /// 全層・全ビルドに等分に掛かる (層で偏らせない・特定ビルドを狙い撃たない)。
        /// **初期値・要較正**: 1 ラン に有利マスが何個出るかを計装していないので 0.25 は仮値。
        /// <see cref="VoidTileChecks"/> / <see cref="VoidTileTriggers"/> の実測を見て動かす。</summary>
        public static float GetVoidTileChance() => C.Has(ChallengeT4.破綻) ? 0.15f : 0f;

        /// <summary>〈破綻〉が有利マスで判定した回数と、 実際に空白化した回数。</summary>
        public static long VoidTileChecks, VoidTileTriggers;

        /// <summary>T4-A 破綻: 全ての回復量の倍率。 2026-08-09: −25% → −8%。</summary>
        /// <summary>**あらゆる回復に掛かる倍率**。 消耗品・前哨基地・休憩・戦闘中の回復すべて。
        /// 〈遅い回復〉(T1 ×0.88) と T4-A〈破綻〉(×0.92) の積。
        ///
        /// 〈遅い回復〉は旧〈浅い眠り〉(2026-08-11 差し替え)。 旧効果は「回復スポットの回復量に
        /// 最大HP 比の上限を掛ける」だったが、 3 度測って一度も効かなかった:
        ///   上限 70% → −2.0 (p=0.070) / 50% → −0.7 (p=0.562) / 回復量を 4.6 倍にしてなお +0.8 (p=0.416)。
        /// 計装で原因が確定した ── 回復量が「最大HP の 30%」なので **HP 40% で寄ると
        /// ちょうど 70% に着地**し、 上限とぴったり重なって一度も噛まなかった (発動率 2.4%)。
        /// さらに回復スポット自体が主力ではなく、 回復の大半は消耗品 (1 ラン 10 個) 経由だった。
        /// **上限ではなく倍率にし、 対象をスポットから全回復へ広げる**のが正しい形。</summary>
        /// 2026-08-17: ×0.86 → **×0.90**。 単軸スイープで 1pt −3.4pt (1pt当り −3.40) と割高だったため。
        public static float GetHealMultiplier()
            => Pick(ChallengeAxis.遅い回復, 1f, 0.90f, /*T2 なし*/ 1f, /*T3 なし*/ 1f);

        // ============================================================
        //  B. 敵強化 — 厚い皮膚 / 狂暴化  (T4: 鋼の皮膚)
        // ============================================================

        /// <summary>敵の最大 HP 倍率。 **T2 ×1.045 / T3 ×1.10** (T1 は無い軸)。
        /// 2026-08-10: ×1.15/×1.30 から緩和。 単独 T3 −16.7pt でカテゴリ B が 6pt −28.0 と
        /// 全カテゴリ中で突出していたため。 敵HP は +1% あたり −0.55pt と素直に線形。</summary>
        public static float GetEnemyHpMultiplier()
            => Pick(ChallengeAxis.厚い皮膚, 1f, /*T1 なし*/ 1f, 1.045f, 1.10f);

        /// <summary>敵ダメージ倍率。 T1 +3% / T2 +6% / T3 +10%。
        /// **ボス限定分はここに含めない** ── <see cref="GetBossDamageBonus"/> を別に足す。</summary>
        public static float GetEnemyDamageMultiplier()
            => Pick(ChallengeAxis.狂暴化, 1f, 1.02f, 1.05f, 1.08f);

        /// <summary>T4-B 鋼の皮膚: 敵が各戦闘 1 回だけ致命傷を HP1 で耐えるか。
        /// **発動ターンの敵攻撃は行わない** (plan の Tier4-B 定義)。</summary>
        public static bool EnemySurvivesFirstLethal() => C.Has(ChallengeT4.鋼の皮膚);

        /// <summary>T4-B 鋼の皮膚: **ボスの最大HP 倍率** (2026-08-11 追加)。 1 = 無効。
        /// 通常敵には掛からない ── 〈厚い皮膚〉が全敵に掛かるので、 T4 は**層の主だけ**を厚くして
        /// 効果の質を分ける。 1HP 踏みとどまりだけでは 4pt にして −0.8pt しかなかった
        /// (削り切る前提の打ち手には 1 戦闘 1 回の追撃を強いるだけで、 ほぼ無害だった)。</summary>
        /// 2026-08-11: ×1.20 → ×1.40 → **×1.20 へ差し戻し**。 ×1.40 は T4 コスト −9.8pt と
        /// 目安には収まったが、 カテゴリ B が 10pt で −25.8 (7層クリア 5.6%・3F止まり 17.1%) と
        /// 全カテゴリ中で突出し、 B を選んだ時点でランが成立しなくなっていた。
        public static float GetBossHpMultiplier() => C.Has(ChallengeT4.鋼の皮膚) ? 1.20f : 1f;

        // ============================================================
        //  C. 戦闘則 — 俊敏 / 見放された運  (T4: 凶運)
        // ============================================================

        /// <summary>プレイヤーの会心率から差し引く量 (0.06 = 6 ポイント)。 T1 −6 / T2 −9 / T3 −12。
        ///
        /// 引くのは加算合計 (`ResolveCritRate` に渡す前) の側。 逓減は 2026-09-15 に撤去したので
        /// 実効率から引くのと同値だが、 下限 0 のクランプを 1 箇所に寄せるため加算側で引く。
        ///
        /// 2026-08-09: T1 −10/−15/−20 → −6/−9/−12。 会心率は天井そのものなので技量で答えられない。
        ///
        /// 2026-08-04: 旧「敵の回避」から差し替え。 回避は単独 T3 で対照群比 ×0.39 と
        /// 10 軸中最重量級で、 加算配点に対して効果が乗算に効きすぎていた (§15-2 V8 実測)。</summary>
        public static float GetPlayerCritRatePenalty()
            => 0f;   // v4.0: 会心率減は廃止 (天井を削るだけで技量で答えられない)

        /// <summary>[廃止: 俊敏の初撃必中回避] 常に false。 会心率ペナルティへ差し替え済み。</summary>
        public static bool EnemyDodgesFirstHit() => false;

        /// <summary>[廃止: 俊敏の回避率] 常に 0。 会心率ペナルティへ差し替え済み。</summary>
        public static float GetEnemyDodgeChance() => 0f;

        /// <summary>このターン、 1 に潰されるダイスの本数 (常に 0 or 1 本)。
        /// **T1 = 最初の 2T / T3 = 最初の 5T**。
        ///
        /// 2026-08-10: T3 を「全ターン継続」から 8T へ。 実測で単独 −13.0pt (1pt当り −4.33) と
        /// 同じ 3pt の軸より重く、 カテゴリ C が 6pt で −23.3 と突出していたため。
        /// 全ターン継続は T4〈凶運〉の専有にして、 基礎軸との差を段でなく**性質**で作る。</summary>
        public static int GetForsakenLuckDiceCount(int currentTurn)
        {
            int t = Tier(ChallengeAxis.見放された運);
            if (t <= 0) return 0;
            int window = t >= 3 ? 5 : 2;                        // T3 = 5T / T1 = 2T
            return currentTurn <= window ? 1 : 0;
        }

        /// <summary>T4-C〈凶運〉: **ラン開始時に全役からランダムで封印する** (本数は
        /// <see cref="SealedRoleCount"/>)。 戻り値はビットマスク。
        ///
        /// 2026-08-11 リワーク。 旧効果は〈見放された運〉のダイス −1 を全ターン化するもので、
        /// **基礎軸の上位互換でしかなかった** (T4 が「もう一段」になっていた)。 実測 −5.5pt。
        ///
        /// 手札役 (二対・大束・極・満・中階・大階) は **引きで決まる役**、 端子役と配線役は
        /// **置き方で決まる役**。 凶運は前者だけを潰す ── 「運が見放す」という名前と
        /// ADR-0010 の役の分類がそのまま一致し、 技量で取り返せる部分は残る。
        ///
        /// **ラン開始時に 1 回だけ引き、 ラン中は固定**。 戦闘ごとに引き直すと
        /// 「運が悪い戦闘」の寄せ集めになり、 どの役を軸に組むかという判断が生まれない。</summary>
        public static int RollSealedRoles(RunState run)
        {
            if (run == null || !C.Has(ChallengeT4.凶運)) return 0;
            var roles = new System.Collections.Generic.List<CombatSystem.RoleKind>(CombatSystem.YachtRoles.All);
            int mask = 0;
            for (int i = 0; i < SealedRoleCount && roles.Count > 0; i++)
            {
                int pick = GameLoop.GameRng.RangeAuto("challenge.kyoun", 0, roles.Count);
                mask |= 1 << (int)roles[pick];
                roles.RemoveAt(pick);
            }
            return mask;
        }

        /// <summary>〈凶運〉が全16役から封印する数。</summary>
        public const int SealedRoleCount = 2;

        // ============================================================
        //  D. 経済 — 搾取経済 / 補給断絶  (T4: 破産)
        // ============================================================

        /// <summary>[廃止: 搾取経済の GOLD 獲得減] 常に 1。
        /// 2026-08-04: 獲得減と価格増の二重取りで単独 T3 が対照群比 ×0.39 に達していたため、
        /// **価格側だけに一本化**した (§15-2 V8 実測)。</summary>
        public static float GetGoldGainMultiplier() => 1f;

        /// <summary>ショップ価格倍率。 T2 +12% / T3 +15%。
        /// v1 の「偽の硬貨 + 不足する物資」の二軸を **搾取経済 1 軸へ統合**した。
        /// **この軸の効果はこれ 1 本だけ** ── GOLD 獲得側は上記のとおり廃止済み。</summary>
        /// <summary>**計測専用の一時上書き。** 0 以下で無効 (通常はこれ)。
        /// 価格倍率の弾性曲線を測るために、 Tier を経由せず直接値を差し込む。
        /// 実測で ×1.15 と ×1.35 の 7F差分が −11.0 / −11.7 とほぼ同じで、
        /// 第一段で弾性を使い切っている疑いがある ── 何点か振らないと折れ点が分からない。
        /// **本番経路では絶対に触らないこと** (AutoRunner のスイープだけが設定する)。</summary>
        public static float ShopPriceOverride = 0f;

        public static float GetShopPriceMultiplier()
            => ShopPriceOverride > 0f
             ? ShopPriceOverride
             : Pick(ChallengeAxis.搾取経済, 1f, /*T1 なし*/ 1f, 1.12f, 1.15f);

        /// <summary>[廃止: 補給断絶の宝箱減量] 常に 1。
        /// 2026-08-05: 補給断絶を **回復阻害の 1 本だけ**に絞った。 宝箱と前哨基地の
        /// 二重取りに加えて T1→T3 が ×0.80→×0.40 と半減する劣悪な傾きで、
        /// 高スコア帯の急落 (20→30pt で ×0.48・×0.35) の一因になっていた。</summary>
        public static float GetTreasureContentMultiplier() => 1f;

        /// <summary>[計装] 回復スポットで戻した HP を数えるだけ。 **上限は撤去済み**
        /// (2026-08-11・〈浅い眠り〉→〈遅い回復〉の差し替えに伴う)。 回復スポットが
        /// ゲーム要素として機能しているかを見るために計装だけ残してある。</summary>
        public static int ApplyHealSpotCap(int healedHP, int playerMaxHP, int beforeHP)
        {
            HealSpotCalls++;
            HealSpotRawHeal += UnityEngine.Mathf.Max(0, healedHP - beforeHP);
            return healedHP;
        }

        /// <summary>[計装] 回復スポットの利用実績。 0=呼ばれた回数 / 1=実際に戻した HP /
        /// 2=上限で削られた回数 / 3=上限で削られた HP。 レポートで浅い眠りの効き方を見る。</summary>
        public static long HealSpotCalls, HealSpotRawHeal, HealSpotCapped, HealSpotLostHeal;
        public static void ResetHealSpotStats()
            => HealSpotCalls = HealSpotRawHeal = HealSpotCapped = HealSpotLostHeal = 0;

        /// <summary>T4-D 破産: 開幕 GOLD から10差し引く (下限 0)。
        /// 2026-08-11: 「0 にする」→「20 差し引く」→ **12**。
        /// 実測 −8.3pt は目安 (−8〜−12) に収まっていたが、 カテゴリ D が 10pt で −20.8 と
        /// 全カテゴリ中で最も重く、 T4 側で薄く削って揃える。 **金庫に振った点が
        /// 完全な捨て点にならない**という破産の設計意図は、 額を減らしても保たれる。</summary>
        public static int GetStartingGoldPenalty() => C.Has(ChallengeT4.破産) ? 10 : 0;

        /// <summary>[互換] 旧 API。 開幕 GOLD を 0 にするか ── 現在は常に false
        /// (差し引き方式へ移行済み。 <see cref="GetStartingGoldPenalty"/> を使うこと)。</summary>
        public static bool IsStartingGoldZero() => false;

        /// <summary>T4-D 破産: 売却額 −10%。
        /// 2026-08-17: −20% → −10%。 T4 単独診断で 4pt −7.2pt (1pt当り −1.80) と、
        /// 基礎軸の中央値 (−2.7) より安いとはいえ T4 中では重い側だったため。</summary>
        public static float GetSellPriceMultiplier() => C.Has(ChallengeT4.破産) ? 0.90f : 1f;

        // ============================================================
        //  E. 崩壊 — 絶望的な戦闘 / 天変地異  (T4: 最後の審判)
        // ============================================================

        /// <summary>戦闘後の希望減少への加算量。T1は0、T2は4層以降、T3は3層以降で+1。
        /// 発火するのは <see cref="GameLoop.HopeSystem.ApplyCombatHpBalance"/> の
        /// **HP 収支マイナス側だけ** ── 無傷で勝った戦闘には乗らない。
        /// 2026-08-04: 旧 +1/+2/+3 から定数化。 Tier で伸びるのは希望上限側だけにして、
        /// 1000ランでT1が−4.5ptと初段へ効果が偏ったため、希望上限低下だけへ一本化した。</summary>
        public static int GetPostCombatHopeLoss(RunState run)
        {
            int tier = Tier(ChallengeAxis.絶望的な戦闘);
            if (run == null || tier < 2) return 0;
            // 低層から一律に削ると T1 と同じ初段負荷へ偏るため、上位 Tier は
            // 深層でのみ圧を加える。乱数を使わないのでペア比較のシード列も保つ。
            int startFloor = tier >= 3 ? 3 : 4;
            return run.currentFloor >= startFloor ? 1 : 0;
        }

        /// <summary>希望上限への加算量 (負値)。 T1 −4 / T2 −8 / T3 −12。
        /// **整備パネル適用後に足すこと** (§6-4)。 先に引くとパネル側の計算で薄まる。
        /// 2026-08-05: −5/−10/−15 から圧縮。 T1 据え置きで T3 だけを緩める同型の調整。</summary>
        /// 2026-08-10: −3/−7/−11 → **−5/−10/−15**。 単独 T3 −8.3pt でカテゴリ E が 6pt −14.0 と
        /// 最軽量級だったため。 段の刻みは実測で線形 (−4.3 / −6.3 / −8.3) なので比率で伸ばす。
        public static int GetHopeCapDelta() => PickI(ChallengeAxis.絶望的な戦闘, 0, -4, -8, -12);

        /// <summary>エスカレーション段階の到達を何ターン前倒しするか。 T1 = 1 / T2 = 1 / T3 = 2。
        ///
        /// 2026-08-09: 1/2/3 → 1/1/2。 **長いボス戦ほど重い**という性質が、 高難易度で
        /// 6〜7 層だけを不可能にしていた (5層クリアの 7層への転換率が 0.71 → 0.23)。
        /// この軸は「早く倒せば届かない」＝技量で答えられる圧なので**残す**が、 傾きを寝かせた。
        ///
        /// 2026-08-08: 旧「ボス戦の最初 N 撃 ×1.50 + ラストスタンド封印」から差し替え。
        /// 旧効果は 40 ターン級のボス戦で 3 撃だけ強化しても誤差にしかならず、
        /// 封印側もラストスタンド使用率が全ペルソナ 0% で無意味だったため、
        /// **単独 T3 が対照群 50.7% に対し 52.7% と実質無料**だった (§15-2 V8 実測)。
        ///
        /// 新効果は閾値 {5,10,15,20} を前倒しするだけで倍率カーブ自体は触らない。
        /// 全戦闘に効き、 長引くほど重くなるので**序盤偏重にならない**。
        /// 「世界の崩れが早まる」という軸の意味とも一致する。</summary>
        /// 2026-08-10 リワーク: エスカレーション前倒しは 3pt で −2.7pt / p=0.185 の死に段だった。
        /// 閾値を 2T 早めても、 実際の戦闘は平均 3 ターン弱で終わるので**そもそも届いていない**。
        /// **ボス攻撃力 ×1.50 へ差し替え。** ボス戦は長く、 全ターンに効くので確実に盤面へ出る。
        public static int GetEscalationTurnShift() => 0;

        /// <summary>〈天変地異〉: **ボスの攻撃値がターンごとに 1.75% ずつ増える**。 1 = 無効。
        ///
        /// 2026-08-10 再リワーク。 一律 ×1.50 は 3pt で −12.0pt と重すぎ、 しかも
        /// **1 ターン目から全開**なので「崩れが進む」という軸の意味と合わなかった。
        /// 新効果は ×1.00 から始まり、 ターンごとに +1.75%。
        /// 2026-08-11: 5% → 3%。 1000 ラン で −15.1pt (1pt当り −5.03) と、 2 位の狂暴化 (−3.13) の
        /// 1.6 倍という突出した重さだったため。
        ///
        /// **加算にする (1 + 0.0175×(turn−1))。 複利にしない** ── 7層の 4 連戦は 40 ターン級で、
        /// 1.05^40 = ×7.0 になり、 どう打っても死ぬだけの局面ができる。 加算なら 40 ターンで
        /// ×2.95 に収まり、 かつエスカレーション段階倍率 (最大 ×2.30) との積で効く。
        /// 通常敵には掛からない ── 「崩壊」は層の主に現れる、 という軸の意味づけに合わせる。</summary>
        public static float GetBossAttackMultiplier(int turn)
        {
            // 2026-08-17: 1.75% → **1.5%**。 単軸スイープで 3pt −11.6pt (1pt当り −3.87) と、
            //   長引く負傷に次ぐ重さだったため。 40 ターンで ×1.68 → ×1.55。
            float per = Pick(ChallengeAxis.天変地異, 0f, /*T1 なし*/ 0f, /*T2 なし*/ 0f, 0.015f);
            if (per <= 0f) return 1f;
            return 1f + per * System.Math.Max(0, turn - 1);
        }

        /// <summary>[廃止: 天変地異のボス初撃強化] 常に 0。 エスカレーション前倒しへ差し替え済み。</summary>
        public static int GetBossBoostedStrikeCount() => 0;

        /// <summary>強化対象の撃に掛かる倍率 (×1.50 固定)。</summary>
        public const float BossBoostedStrikeMultiplier = 1.50f;

        /// <summary>ボス戦でのみ上乗せする敵ダメージ倍率の加算分。
        /// **v3.0 では撃数制限つき**なので、 恒常加算ではなく
        /// <see cref="GetBossBoostedStrikeCount"/> と併用して撃ごとに判定する。
        /// 互換のため「強化対象の撃であれば」の加算値を返す。</summary>
        public static float GetBossDamageBonus()
            => GetBossBoostedStrikeCount() > 0 ? (BossBoostedStrikeMultiplier - 1f) : 0f;

        /// <summary>[廃止: 天変地異のラストスタンド封印] 常に false。
        /// ラストスタンドは外殻ノードを取らないペルソナでは一度も発動せず (使用率 0%)、
        /// 封印しても効果が測定不能だった。 差し替え後は封印しない。</summary>
        public static bool IsLastStandDisabled() => false;

        /// <summary>[廃止] T4-E 最後の審判の大罪付与。 常に false。
        /// 2026-08-11: 「3層・5層で大罪を 1 つ」は UI 未実装で抽選付与になっており、
        /// 効果が恒久デバフ任せで読めなかった (実測 −6.4pt)。 下記の**刻限**へ差し替え。</summary>
        public static bool HasFinalJudgment() => false;

        /// <summary>T4-E〈最後の審判〉: ラン累計の戦闘ターン数がこの値に達すると刻限が来る。 0 = 無効。
        ///
        /// **2026-08-17 に実測で較正: 125 → 300。** 旧 125 は計装が無い時期の仮値で、
        /// 実測 (0pt 遺物なし Super n=1000) では総ターン 平均 240 / 中央 256、
        /// **クリアしたランは 100% が 125T を超えていた** (最短 132T)。 つまり刻限は
        /// 「長引かせた罰」ではなく**全員に確実に掛かる被ダメ +30%**として働いており、
        /// 改善/悪化が 10/89 と一方通行だったのはこのため。
        /// 300T ならクリアランの 39% にしか掛からず、 設計意図どおりの分岐になる
        /// (参考: 260T→65% / 340T→19%)。</summary>
        public static int GetJudgmentTurnThreshold() => C.Has(ChallengeT4.最後の審判) ? 300 : 0;

        /// <summary>[互換用] 〈最後の審判〉は回復を阻害しない。</summary>
        public static float GetJudgmentHealMultiplier(RunState run) => 1f;

        /// <summary>[互換用] 旧・刻限後の回復阻害。現仕様では常に素通し。</summary>
        public static int ApplyJudgmentHealReduction(int amount, RunState run)
        {
            return Mathf.Max(0, amount);
        }

        /// <summary>T4-E〈最後の審判〉: 125戦闘ターン以降、あらゆる被ダメージを30%増加。
        /// 軽減・無効化などを解決した後の最終ダメージへ適用し、端数は切り上げる。</summary>
        public static int ApplyJudgmentDamageIncrease(int amount, RunState run, int currentHP = int.MaxValue)
        {
            if (amount <= 0) return 0;
            if (!IsPastJudgmentDeadline(run)) return amount;

            int increased = Mathf.Max(1, Mathf.CeilToInt(amount * 1.30f));
            int bonus = increased - amount;
            JudgmentTicks++;
            JudgmentHpLost += bonus;
            JudgmentMaxLoss = Mathf.Max(JudgmentMaxLoss, bonus);
            if (currentHP > 0 && increased >= currentHP) JudgmentKills++;
            return increased;
        }

        // ============================================================
        //  [計装] T4 の発動実績 (2026-08-11)
        // ============================================================
        //  **発動しているかを数えずに閾値を動かさない。** 今日 3 度、 数えないまま値を
        //  上下させて「効かないので上げる → まだ効かない」を繰り返した
        //  (長引く負傷 / 浅い眠り / 不器用)。 分布を見てから決める。

        /// <summary>全ランの累計戦闘ターン (刻限の分布を出す分子)。</summary>
        public static long JudgmentTurnsTotal;
        /// <summary>刻限を越えたランの数。</summary>
        public static long JudgmentRunsOverThreshold;
        /// <summary>刻限超過後に削った回数と HP。</summary>
        public static long JudgmentTicks, JudgmentHpLost;
        /// <summary>[旧仕様の計装] 刻限後の回復阻害により失われた HP。現仕様では常に0。</summary>
        public static long JudgmentHealPrevented;
        /// <summary>刻限の削り量が達した最大値。 **傾斜が実際に立ち上がったかを見る**
        /// ── 1 のままなら「10T 以内に決着している＝ 傾斜は一度も効いていない」。</summary>
        public static int JudgmentMaxLoss;
        /// <summary>〈破綻〉が発動した回数。</summary>
        public static long BreakdownTriggers;

        // --- 〈最後の審判〉が「なぜ効かないか」を分ける計装 (2026-08-11) ---
        //  削り量を 2 倍 (55,772 → 112,462 HP) にして 7F クリアが 18.8% → 18.8% と
        //  小数点以下まで動かなかった。 **弱いのではなく、 その HP が勝敗の経路に
        //  乗っていない。** 値をもう一度いじる前に、 候補を数字で潰す:
        //    ① 回復に吸われている        → 回復総量と削り総量の比を見る
        //    ② 既に負けたランだけを削る  → 刻限を越えたランの到達層分布を見る
        //    ③ 致命打になっていない      → 刻限の削りで死んだ回数を数える

        /// <summary>刻限の削りが**そのまま致命打になった**回数 (削って HP が 0 になった)。</summary>
        public static long JudgmentKills;
        /// <summary>刻限を越えたランの到達層分布 (添字 = 到達層 0..8)。
        /// **長いラン＝深くまで行くラン**のはずなので、 ここが浅い側に寄っていれば
        /// 「グラインドして負けるラン」だけを罰していて、 勝ち筋には触れていない。</summary>
        public static readonly long[] JudgmentReachByFloor = new long[9];
        /// <summary>刻限を越えた**後**に受けた総被ダメと、 回復した総量。
        /// 刻限の削り (JudgmentHpLost) と並べて読む ── 被ダメに埋もれているのか、
        /// 回復に打ち消されているのかが分かれる。</summary>
        public static long JudgmentPostDamageTaken, JudgmentPostHeal;
        /// <summary>刻限超過ランを死亡/生存に分けた収支。合算の生存者バイアスを見分ける。</summary>
        public static long JudgmentDeadlineDeaths, JudgmentDeadlineSurvivors;
        public static long JudgmentDeathDamage, JudgmentDeathHealRequested, JudgmentDeathHealActual;
        public static long JudgmentSurvivorDamage, JudgmentSurvivorHealRequested, JudgmentSurvivorHealActual;

        /// <summary>刻限が発動中かどうか。 計装の分岐に使う。</summary>
        public static bool IsPastJudgmentDeadline(RunState run)
        {
            int th = GetJudgmentTurnThreshold();
            return th > 0 && run != null && run.totalCombatTurns >= th;
        }

        /// <summary>刻限超過後の被ダメを積む。</summary>
        public static void NoteDamageTaken(int amount, RunState run)
        {
            if (amount <= 0 || !IsPastJudgmentDeadline(run)) return;
            JudgmentPostDamageTaken += amount;
            run.judgmentPostDamage += amount;
        }

        /// <summary>刻限超過後の要求回復量と、HP上限等を反映した実回復量を積む。</summary>
        public static void NoteHeal(int requested, int actual, RunState run)
        {
            if (requested <= 0 || !IsPastJudgmentDeadline(run)) return;
            JudgmentPostHeal += actual;
            run.judgmentHealRequested += requested;
            run.judgmentHealActual += actual;
        }

        /// <summary>ラン終了時に、 刻限を越えていたランの到達層を記録する。</summary>
        public static void NoteRunEnd(RunState run, int reachedFloor, bool died)
        {
            if (run == null) return;
            int th = GetJudgmentTurnThreshold();
            if (th <= 0 || run.totalCombatTurns < th) return;
            JudgmentReachByFloor[Mathf.Clamp(reachedFloor, 0, 8)]++;
            if (died)
            {
                JudgmentDeadlineDeaths++;
                JudgmentDeathDamage += run.judgmentPostDamage;
                JudgmentDeathHealRequested += run.judgmentHealRequested;
                JudgmentDeathHealActual += run.judgmentHealActual;
            }
            else
            {
                JudgmentDeadlineSurvivors++;
                JudgmentSurvivorDamage += run.judgmentPostDamage;
                JudgmentSurvivorHealRequested += run.judgmentHealRequested;
                JudgmentSurvivorHealActual += run.judgmentHealActual;
            }
        }

        public static void ResetT4Stats()
        {
            JudgmentTurnsTotal = JudgmentRunsOverThreshold = 0;
            JudgmentTicks = JudgmentHpLost = JudgmentHealPrevented = BreakdownTriggers = 0;
            JudgmentMaxLoss = 0;
            VoidTileChecks = VoidTileTriggers = 0;
            JudgmentKills = JudgmentPostDamageTaken = JudgmentPostHeal = 0;
            JudgmentDeadlineDeaths = JudgmentDeadlineSurvivors = 0;
            JudgmentDeathDamage = JudgmentDeathHealRequested = JudgmentDeathHealActual = 0;
            JudgmentSurvivorDamage = JudgmentSurvivorHealRequested = JudgmentSurvivorHealActual = 0;
            for (int i = 0; i < JudgmentReachByFloor.Length; i++) JudgmentReachByFloor[i] = 0;
        }

        /// <summary>3 層突入時に呼ぶ。 T4-E 最後の審判 なら大罪を 1 つ付与。
        /// **本来は「未所持の 3 候補から選択」させる** (plan Tier4-E)。 UI 未実装のため
        /// 現状は抽選で 1 個付与する。 UI 実装時にここを差し替える。</summary>
        public static void TryGrantOnFloor3(RunState run)
        {
            if (!HasFinalJudgment()) return;
            GrantPermanent(run, "最後の審判(3層)");
        }

        /// <summary>5 層突入時に呼ぶ。 T4-E 最後の審判 なら大罪を 1 つ付与。</summary>
        public static void TryGrantOnFloor5(RunState run)
        {
            if (!HasFinalJudgment()) return;
            GrantPermanent(run, "最後の審判(5層)");
        }

        /// <summary>[廃止] v1 の「1 層で追加付与」。 v3.0 は 3 層・5 層なので何もしない。</summary>
        public static void TryGrantOnFloor1(RunState run) { }

        private static void GrantPermanent(RunState run, string source)
        {
            if (run == null) return;
            string id = MetaPermanentDebuffPicker.Pick(run);
            if (string.IsNullOrEmpty(id))
            {
                Debug.LogWarning($"[挑戦] {source}: 大罪プールが空 or 全保持済みのため付与なし");
                return;
            }
            run.permanentDebuffs.Add(id);
            Debug.Log($"[挑戦] {source}: 大罪「{id}」を付与");
        }

        // ============================================================
        //  廃止した軸 — 呼び出し元互換のため中立値を返す
        //  復活には docs/challenge-debuff-plan.md §7 の 3 条件を満たすこと。
        // ============================================================

        /// <summary>[廃止: 戦場の霧] 常に制限なし (-1)。</summary>
        public static int GetMapSightLimit() => -1;

        /// <summary>[廃止: 偽の商人] 常に 0。</summary>
        public static float GetFalseMerchantChance() => 0f;

        /// <summary>[廃止: 空箱] 宝箱は消さない (plan §3)。 常に false。</summary>
        public static bool IsTreasureGone() => false;

        /// <summary>[廃止: 補給断絶 T3 の消失] 前哨基地は消さない (plan §3)。 常に false。</summary>
        public static bool IsForwardBaseDisabled() => false;

        /// <summary>[廃止: 焦燥] 常に 0 (無効)。</summary>
        public static int GetImpatienceTileThreshold() => 0;
        public const int ImpatienceHopeLoss = 5;

        /// <summary>[廃止: 反響] 常に 0。</summary>
        public static float GetEchoReflectRate() => 0f;

        /// <summary>[廃止: 組織的行動] マップ構成は変えない。 常に 0。</summary>
        public static float GetBattleTileWeightBonus() => 0f;
        /// <summary>[廃止: 組織的行動] 常に 0。</summary>
        public static float GetEliteTileWeightBonus() => 0f;

        /// <summary>[廃止: 絶望的な進軍] 配点方式に無い。 常に false。</summary>
        public static bool IsDespairMarchActive() => false;
    }
}
