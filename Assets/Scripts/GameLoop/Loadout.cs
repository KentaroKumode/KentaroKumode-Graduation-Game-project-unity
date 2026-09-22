using InventorySystem;

namespace GameLoop
{
    /// <summary>
    /// 取得した武器/ダイスを RunState に「自動装備」する軽量ロジック（期待値ベース）。
    ///
    /// 戦闘モデル: 毎ターン両者がダイス合計を振り、高い方がロール勝利→相手にダメージ。
    /// よって装備の優劣は「1ロールの期待合計」で評価するのが妥当。
    ///
    ///   - ダイス: 出目テーブル(diceFaces)を差し替える。1ダイスの期待値 = mean(faces)。
    ///   - 武器  : 振る個数(count)・攻撃力(attackPower)・会心率(critRatePct)＋家系パッシブ。
    ///
    /// <para><b>武器EV = count × 面平均 + attackPower + critRatePct/5 + 学習済み準パワー×係数</b>
    /// （2026-09-05 改訂）。</para>
    ///
    /// <para><b>旧式は `count × 面平均 + 会心` だった。</b> 全武器が count=5 で
    /// 面も共通 (2026-08-17 以降、面は DiceFaceParts が供給し武器の diceMax は読まれない) なので、
    /// 第 1 項は全武器で同値 ── <b>実質 会心率だけで武器を選んでいた</b>。
    /// attackPower も家系パッシブも一切見ていない。 実測 (250,000 ラン) では会心率の順位が
    /// そのまま採用率になり、 職業が 4 種均等配分にもかかわらず最終武器は
    /// <b>短剣40.6% / 斧35.4% / 剣5.1% / 盾1.0%</b> ── 盾の価値はほぼ全部パッシブ側にあるのに、
    /// 評価式からは見えないため構造的に選ばれなかった (＝盾と剣は測定不能だった)。</para>
    ///
    /// <para><b>段の扱いも家系を見る。</b> 「上位 Tier は素の期待値以上」の近道は
    /// <b>同じ家系の中でだけ</b>成り立つ ── 強化素材の節約が理由なので、 家系を乗り換えると
    /// その論拠が消える。 旧実装は家系を見ずに段だけで乗り換えていたため、
    /// ショップに並んだ他家系の T3 を拾うたびに全員が同じ家系へ収束していた。</para>
    ///
    /// ダイス は「1ダイス面平均」が現在値を上回る時のみ装備。
    /// </summary>
    public static class Loadout
    {
        private const float Eps = 0.01f;

        // デフォルト（武器未装備）の戦闘値: 2d6 / 会心1
        private const int DefWeaponCount = 2;
        private const int DefWeaponDiceMax = 6;
        private const int DefWeaponCrit = 1;

        /// <summary>武器なら期待値が上なら、ダイスなら面平均が上なら自動装備する。</summary>
        public static void TryAutoEquip(RunState run, string itemId)
        {
            if (run == null || string.IsNullOrEmpty(itemId)) return;
            var item = ItemDatabase.Instance?.GetItem(itemId);
            if (item == null) return;

            if (item.category == ItemCategory.Weapon && item.hasWeaponStats && item.weaponDice != null)
            {
                // 計測モード: 家系の乗り換えを一切させない (WouldUpgrade と同じ判定をここにも置く
                //   ── 装備は購入と別経路で走るので、 片方だけだと素通りする)
                {
                    string cf = WeaponFamily(run.equippedWeaponId);
                    if (LockWeaponFamily && cf != null && !SameLineage(cf, WeaponFamily(itemId))) return;
                }
                if (WeaponEV(item, run) > CurrentWeaponEV(run) + Eps)
                    run.equippedWeaponId = itemId;
            }
            else if (item.category == ItemCategory.Dice && item.diceFaces != null && item.diceFaces.Length > 0)
            {
                if (Mean(item.diceFaces) > CurrentPerDieMean(run) + Eps)
                    run.equippedDiceId = itemId;
            }
        }

        /// <summary>その武器/ダイスを取得したら **実際に装備に至るか**。 副作用なし。
        ///
        /// <see cref="TryAutoEquip"/> と同じ判定を、 買う前に問い合わせるための口。
        /// 2026-08-10: BOT が武器枠・ダイス枠を**無条件に全部買っていた**ため、
        /// 装備されないダイスに金を捨てていた (1 ラン 8.14 個購入・大半が死に金)。
        /// ダイスはインベントリ容量を消費しないので、 買っても何も起きずゴールドだけ消える。
        /// **人間なら「今のより良いか」を見てから買う。** 購入側はここを見ること。
        ///
        /// 武器/ダイス以外は false ── この判定の対象外という意味で、
        /// 「買ってはいけない」ではない (呼び出し側が種別で分岐すること)。</summary>
        public static bool WouldUpgrade(RunState run, string itemId)
        {
            if (run == null || string.IsNullOrEmpty(itemId)) return false;
            var item = ItemDatabase.Instance?.GetItem(itemId);
            if (item == null) return false;

            if (item.category == ItemCategory.Weapon && item.hasWeaponStats && item.weaponDice != null)
            {
                // **上位 Tier は素の期待値以上の価値がある。** そこへ到達するまでに要る
                //   強化素材 (T1→T2 で 2、 →T3 で計 6、 →T4 で計 12) を丸ごと節約できるため。
                //   期待値だけで判定すると、 育て直しの土台になる上位 Tier を買い逃す。
                //   段階は GameManager.OverallStage と同じ数え方 ((tier-1)*2 + plus)。
                //   買った武器は plus=0 から始まるので、 その分も考慮して厳密に上回る時だけ。
                //
                //   **ただし同じ家系の中でだけ** (2026-09-05)。 素材節約が論拠なので、
                //   家系を乗り換えるとその論拠が消える ── 旧実装は家系を見ずに段だけで
                //   乗り換えており、 ショップに並んだ他家系の T3 を拾うたびに全員が
                //   同じ家系へ収束していた (最終武器 短剣40.6% / 盾1.0%、 職業は均等配分なのに)。
                //   家系を跨ぐ乗り換えは下の EV 比較に委ねる。
                string curFam = WeaponFamily(run.equippedWeaponId);
                string newFam = WeaponFamily(itemId);
                bool sameFamily = SameLineage(curFam, newFam);
                if (sameFamily && WeaponStage(itemId, 0) > WeaponStage(run.equippedWeaponId, run.weaponPlus))
                    return true;
                // 家系未確定 (デフォルト武器のまま) なら従来どおり段だけで拾ってよい
                if (curFam == null && WeaponStage(itemId, 0) > WeaponStage(run.equippedWeaponId, run.weaponPlus))
                    return true;
                // 計測モード: 家系の乗り換えを一切させない (4 家系を同じ厚みで測るため)
                if (LockWeaponFamily && curFam != null && !sameFamily) return false;
                return WeaponEV(item, run) > CurrentWeaponEV(run) + Eps;
            }
            if (item.category == ItemCategory.Dice && item.diceFaces != null && item.diceFaces.Length > 0)
                return DiceWorthBuying(item, run);
            return false;
        }

        /// <summary>ダイスを**買う**価値があるか。 装備判定 (面平均だけ) より厳しい。
        ///
        /// 2026-08-10 の実測が発端。 挑戦デバフ〈売り渋り〉で陳列が 2 枠減り、
        /// **ダイス購入が 6.16 回 → 0 回になったら 7層クリアが +9.4pt (p&lt;0.001)** 上がった。
        /// 浮いた金がパッシブ +2.8 個・消耗品 +2.2 個に化けている ── つまり
        /// **ダイスは同じ金額のパッシブに負けている**。 それでも買っていたのは、
        /// 種別ごとに独立したゲートで貪欲に買っており、 **機会費用を見ていなかった**ため。
        ///
        /// 3 つの条件を課す:
        ///   ① 面平均の改善が <see cref="DiceMinMeanGain"/> 以上 (小刻みな買い替えを止める)
        ///   ② 階系 (小階/中階/大階) を失う持ち替えは <see cref="DiceRunLossMeanGain"/> 以上を要求。
        ///      奇/偶/天 は**階系が原理的に不能**なのに平均が高いので、 面平均だけで選ぶと
        ///      必ずここへ登り、 大階 (被ダメ0)・中階 (充電+12+無料リロール) を無自覚に捨てる
        ///   ③ 端子役が成立しないダイス (無銘) は、 端子役 7 種の放棄に見合う差を要求</summary>
        public const float DiceMinMeanGain = 1.0f;
        public const float DiceRunLossMeanGain = 2.0f;

        private static bool DiceWorthBuying(CompleteItemData item, RunState run)
        {
            float cur = CurrentPerDieMean(run);
            float gain = Mean(item.diceFaces) - cur;
            if (gain < DiceMinMeanGain) return false;

            // ② 階系を失う持ち替えか (今は組めるのに、 新しい方では組めない)
            bool curRuns = CurrentCanFormRuns(run);
            bool newRuns = LongestStep1Run(item.diceFaces) >= 3;
            if (curRuns && !newRuns && gain < DiceRunLossMeanGain) return false;

            // ③ 端子役を殺すダイスは更に上乗せを要求
            if (item.suppressTerminalRoles && gain < DiceRunLossMeanGain) return false;
            return true;
        }

        /// <summary>今の面構成で 3 連番 (小階) 以上を組めるか。 装備ダイスが無ければ武器の素面で見る。</summary>
        private static bool CurrentCanFormRuns(RunState run)
        {
            var d = string.IsNullOrEmpty(run.equippedDiceId)
                  ? null : ItemDatabase.Instance?.GetItem(run.equippedDiceId);
            if (d?.diceFaces != null && d.diceFaces.Length > 0)
                return LongestStep1Run(d.diceFaces) >= 3;
            return CurrentWeaponDiceMax(run) >= 3;   // 素の面は 1..N の連番なので N>=3 で可
        }

        /// <summary>面集合に含まれる最長の 1 刻み連番。</summary>
        private static int LongestStep1Run(int[] faces)
        {
            if (faces == null || faces.Length == 0) return 0;
            int best = 1;
            for (int i = 0; i < faces.Length; i++)
            {
                bool hasPrev = false;
                for (int j = 0; j < faces.Length; j++) if (faces[j] == faces[i] - 1) { hasPrev = true; break; }
                if (hasPrev) continue;
                int len = 1, next = faces[i] + 1;
                while (true)
                {
                    bool found = false;
                    for (int j = 0; j < faces.Length; j++) if (faces[j] == next) { found = true; break; }
                    if (!found) break;
                    len++; next++;
                }
                if (len > best) best = len;
            }
            return best;
        }

        /// <summary>武器 ID と + 段階から総合強化段階を出す。 進行武器でなければ -1。
        /// <b>id を綴りで切らない</b> (2026-09-22) ── items.json の tier を読む。</summary>
        private static int WeaponStage(string weaponId, int plus)
        {
            if (string.IsNullOrEmpty(weaponId)) return -1;
            var d = InventorySystem.ItemDatabase.Instance?.GetItem(weaponId);
            if (d == null || d.tier <= 0) return -1;
            return (d.tier - 1) * 2 + (plus > 0 ? 1 : 0);
        }

        // ===== 期待値計算 =====

        /// <summary>1ダイスあたりの現在期待値。カスタムダイス装備中はその面平均、
        /// 無ければ装備中(or デフォルト)武器の素の面平均 (diceMax+1)/2。</summary>
        private static float CurrentPerDieMean(RunState run)
        {
            float dm = EquippedDiceMean(run);
            if (dm > 0f) return dm;
            return (CurrentWeaponDiceMax(run) + 1) / 2f;
        }

        /// <summary>装備中カスタムダイスの面平均。無ければ 0。</summary>
        private static float EquippedDiceMean(RunState run)
        {
            if (string.IsNullOrEmpty(run.equippedDiceId)) return 0f;
            var d = ItemDatabase.Instance?.GetItem(run.equippedDiceId);
            return (d?.diceFaces != null && d.diceFaces.Length > 0) ? Mean(d.diceFaces) : 0f;
        }

        private static int CurrentWeaponDiceMax(RunState run)
        {
            var w = string.IsNullOrEmpty(run.equippedWeaponId)
                ? null : ItemDatabase.Instance?.GetItem(run.equippedWeaponId);
            return (w != null && w.hasWeaponStats && w.weaponDice != null)
                ? w.weaponDice.maxValue : DefWeaponDiceMax;
        }

        /// <summary>学習済み準パワーを攻撃力スケールへ換算する係数。
        ///
        /// <para><b>大きく取る。</b> 素の attackPower(1〜8) / 会心率(5〜40%) は合計 13 点ぶんの
        /// 幅があるのに対し、 武器間の準パワー差は 1.3 程度しかない。 係数が小さいと
        /// <b>生のステータスが学習値を 5:1 で押し潰す</b> ── そして生の数値は
        /// <b>防御とパッシブの価値を一切表現できない</b> (盾は攻撃1〜4/会心1〜2 で必ず負ける)。
        /// 学習値は実測された寄与そのものなので、 代理指標に劣後させる理由がない。</para>
        ///
        /// <para>0 にすると旧来の「ステータスだけ」に戻る。 <b>暫定値・要測定</b>。</para></summary>
        private const float LearnedWeaponWeight = 6.0f;

        /// <summary><b>家系の乗り換えを禁じる</b> (既定 OFF)。 計測用。
        ///
        /// <para>職業は 4 種均等配分なので、 これを立てると各家系が約 25% のランで最後まで
        /// 担がれる ＝ <b>4 家系すべてが同じ厚みで測定できる</b>。 立てないと BOT の
        /// 乗り換えが偏り、 実測で 盾 1.0%〜4.7% ── 統計が溜まらず「弱いのか
        /// 選ばれていないだけなのか」を分離できない (循環)。</para>
        ///
        /// <para><b>ゲーム性の変更ではなく測定条件</b>。 製品挙動を測るときは OFF に戻すこと。</para></summary>
        public static bool LockWeaponFamily = false;

        /// <summary>素の面平均。 面は DiceFaceParts が供給するので武器の diceMax は使わない
        /// (2026-08-17 以降 死にデータ)。 素の 6 面 [1..6] の期待値。</summary>
        private const float BaseFaceMean = 3.5f;

        /// <summary>武器の期待値。 <b>attackPower と家系パッシブを含む</b> (クラス解説参照)。</summary>
        private static float WeaponEV(CompleteItemData w, RunState run)
        {
            if (w == null || !w.hasWeaponStats || w.weaponDice == null) return -1f;
            float perDie = System.Math.Max(BaseFaceMean, EquippedDiceMean(run));
            // 会心は 5% = 1 点で評価する (旧「分子」1 点と同じ桁)。
            float statEv = w.weaponDice.count * perDie + w.attackPower + w.critRatePct / 5f;
            return statEv + LearnedWeaponBonus(w.id);
        }

        /// <summary>家系パッシブの価値。 素のステータスには現れないので学習値から引く。
        ///
        /// <para><b>家系の終端 (T4) の学習値で代表させる。</b> T1 の盾を拾うことは
        /// 「盾の家系に乗る」ことであり、 判断すべきはチェーン全体の価値だから ──
        /// 上の段ルール (同家系なら上位段へ自動で乗る) と同じ理屈である。
        /// T1〜T3 は <c>ExcludedFromLift</c> で学習値を持たない (チェーン下流の効果が
        /// lift に流れ込むため) ので、 終端を見ないと<b>家系パッシブが評価に一切入らない</b>。</para>
        ///
        /// <para>**未学習なら 0** ── 学習ゼロのバッチでは素の EV だけで比較する
        /// (<c>PowerScore</c> は未学習に <c>Unlearned</c> を返すので、 そのまま足すと
        /// 全武器に同じ下駄を履かせることになり比較の役に立たない)。</para></summary>
        private static float LearnedWeaponBonus(string id)
        {
            if (string.IsNullOrEmpty(id)) return 0f;
            string fam = WeaponFamily(id);
            string key = fam != null ? fam + "_t4" : id;   // 家系の終端で代表
            if (!AutoTest.LearnedPriorityProvider.HasLearned(key)) return 0f;
            return AutoTest.LearnedPriorityProvider.PowerScore(key) * LearnedWeaponWeight;
        }

        private static float CurrentWeaponEV(RunState run)
        {
            if (!string.IsNullOrEmpty(run.equippedWeaponId))
            {
                var cur = ItemDatabase.Instance?.GetItem(run.equippedWeaponId);
                if (cur != null && cur.hasWeaponStats && cur.weaponDice != null)
                    return WeaponEV(cur, run);
            }
            // デフォルト武器 2d6/会心1 (attackPower なし)
            float perDie = System.Math.Max(BaseFaceMean, EquippedDiceMean(run));
            return DefWeaponCount * perDie + DefWeaponCrit;
        }

        /// <summary>武器 id の家系プレフィックス ("血塗りの戦斧" → "axe")。 非階梯武器なら null。</summary>
        private static string WeaponFamily(string weaponId)
        {
            if (string.IsNullOrEmpty(weaponId)) return null;
            var d = InventorySystem.ItemDatabase.Instance?.GetItem(weaponId);
            return string.IsNullOrEmpty(d?.family) ? null : d.family;
        }

        /// <summary>2 つの武器が「同じ家系の系譜」か。 複合武器の廃止 (2026-09-21) で
        /// 家系は純 4 系のみになったので、 <b>プレフィックスの一致がそのまま系譜の一致</b>。</summary>
        private static bool SameLineage(string famA, string famB)
            => famA != null && famB != null && famA == famB;

        private static float Mean(int[] faces)
        {
            if (faces == null || faces.Length == 0) return 0f;
            long s = 0;
            for (int i = 0; i < faces.Length; i++) s += faces[i];
            return (float)s / faces.Length;
        }
    }
}
