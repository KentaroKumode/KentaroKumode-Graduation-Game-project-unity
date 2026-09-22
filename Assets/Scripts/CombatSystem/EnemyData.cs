using System;
using System.Collections.Generic;

namespace CombatSystem
{
    /// <summary>
    /// 敵1体の定義データ（JSONからデシリアライズ）
    /// </summary>
    [Serializable]
    public class EnemyData
    {
        public string id;              // 内部識別名 (例: "goblin")
        public string displayName;     // 表示名 (例: "ゴブリン")
        public string description;     // フレーバーテキスト
        public int floor;              // 初登場階層 (1～7)
        /// <summary>エリートマス専用の敵か (2026-09-20)。 true はエリートマスにだけ、 false は通常マスにだけ出る。
        /// <b>エリートの強さは enemies.json の数値そのもの</b>。</summary>
        public bool elite;
        public int maxHP;              // 最大HP
        // ---- [廃止 2026-07-28] 敵のダイス表現 ----
        //   敵は「ダイスを振る」のをやめ、 **指定範囲の一様乱数を 1 回引く**方式へ移行した。
        //   個数×面という二段のツマミは調整のたびに期待値と分散が同時に動いてしまい、
        //   「波の大きさ」だけを指定したい要求に噛み合わなかった。
        //   下 3 つは旧データの読み込み互換のため残置。 attackRollMax > 0 のとき一切参照しない。
        public int diceCount;
        public int diceMaxValue;
        public int[] diceFaces;

        // ---- 攻撃ロール (2026-07-28〜): 毎ターン [attackRollMin, attackRollMax] の一様乱数を 1 回 ----
        /// <summary>攻撃ロールの下限 (含む)。 attackRollMax > 0 のとき有効。</summary>
        public int attackRollMin;
        /// <summary>攻撃ロールの上限 (含む)。 **0 以下なら旧ダイス方式にフォールバック**する。</summary>
        public int attackRollMax;

        /// <summary>攻撃ロール方式を使うか (新データか)。</summary>
        public bool UsesAttackRoll => attackRollMax > 0;
        /// <summary>1 ターンに引く個数。 新方式は常に 1 (LED 表示・推定勝率など「本数」を要る箇所用)。</summary>
        public int EffectiveRollCount => UsesAttackRoll ? 1 : diceCount;
        /// <summary>1 個あたりの取りうる最大値。 新方式は attackRollMax。</summary>
        public int EffectiveRollMax => UsesAttackRoll ? attackRollMax : diceMaxValue;
        /// <summary>攻撃ロールの期待値。 予告・診断・チューナーの基準に使う。</summary>
        public float AttackRollExpected => UsesAttackRoll
            ? (Math.Max(0, attackRollMin) + attackRollMax) / 2f
            : (diceFaces != null && diceFaces.Length > 0
                ? diceCount * SumOf(diceFaces) / (float)diceFaces.Length
                : diceCount * (1 + diceMaxValue) / 2f);

        private static int SumOf(int[] a) { int s = 0; for (int i = 0; i < a.Length; i++) s += a[i]; return s; }

        /// <summary>決定論的乱数の連番。 戦闘ターンと本数で一意にする。
        /// 敵側の抽選は CombatManager の RngIdx を使えないため、 ここで組み立てる。</summary>
        private static int RngSlot(int i)
        {
            int t = CombatSystem.CombatManager.Instance != null
                  ? CombatSystem.CombatManager.Instance.CurrentCombatTurn : 0;
            return Math.Max(0, t) * 100 + i;
        }
        public int criticalNumerator;  // クリティカル確率の分子 (0～9)
        public int threat;              // 脅威値（勝利時でもプレイヤーが受ける削りダメ基準）
        public float baseDefenseRate;   // 基礎防御（被ダメ%軽減 0～1。利刃で相殺される）
        public List<EnemyPassiveEntry> passiveSkills; // パッシブスキル一覧

        // ---- パリィ予兆 ----
        /// <summary>敵固有の自然予兆dot。0は種別既定値（雑魚20/エリート8/ボス0）を使う。</summary>
        public int naturalTelegraphDots;
        /// <summary>公開スキル「殺気遮断」。1レベルにつき実効予見-1。</summary>
        public int killingIntentSuppressionLevel;

        // ---- ADR-0009 相互攻撃モデル用パラメータ (JSON 未指定なら既定値を使用) ----
        public int baseAttack;          // 基礎攻撃値。0 なら既定 = max(1, threat)
        // [廃止 2026-07-28] 敵ダイスロールの攻撃寄与率β。
        //   隠し係数で出目を目減りさせると、 予告値と盤上のダイスが一致しなくなるため撤去した。
        //   ダイス合計は**そのまま**攻撃値へ加算される。 火力調整は署名ダイスの面を直接動かす
        //   (§13-2 の L3 学習レバー)。 フィールドは JSON 互換のため残置・未使用。
        public float attackDiceWeight;
        public string escalationProfile; // エスカレーション型 "std"|"rush"|"gentle"|"spike"。空なら "std"

        // ---- 大技サイクル (5 層以降のボス・2026-09-19) ----
        //   heavyPeriod ターンごと (T = N, 2N, 3N …) に大技、 その直前の N−1 ターンは溜め。
        //   周期は公開規則なので、 予告に「あと何ターンで大技か」を出す (柱3 完全情報)。
        //   狙い: 「溜めの間に倒し切る / 充電を貯めて大技でリロール / HP で受ける」という
        //   戦闘全体の資源配分をボス戦で問う (先読み BOT が 1 ターン最善に勝っている中身がこれ)。
        /// <summary>大技の周期 (ターン)。 0 = 大技なし。</summary>
        public int heavyPeriod;
        /// <summary>大技ターンの攻撃倍率 (段階倍率・ボス倍率と掛け合わせる)。</summary>
        public float heavyMul;
        /// <summary>溜めターン (大技以外のターン) の攻撃倍率。</summary>
        public float heavyWindupMul;

        /// <summary>ADR-0009: 基礎攻撃値 (段階スケール前)。未指定時は threat から導出。</summary>
        public int EffectiveBaseAttack => baseAttack > 0 ? baseAttack : Math.Max(1, threat);

        /// <summary>ADR-0009: 敵ダイスロールの攻撃寄与率β (0〜1)。</summary>
        /// <summary>[廃止 2026-07-28] 常に 1.0。 ダイス合計はそのまま攻撃値へ加算される。</summary>
        public float EffectiveAttackDiceWeight => 1.0f;

        /// <summary>ADR-0009: エスカレーションプロファイル名 (Escalation.GetMultiplier で解決)。</summary>
        public string EffectiveEscalationProfile =>
            string.IsNullOrEmpty(escalationProfile) ? Escalation.ProfileStd : escalationProfile;

        /// <summary>攻撃ロールを引く。 新方式は [min,max] の一様乱数 1 個、 旧方式はダイスの束。
        /// 返り値が配列なのは LED 表示と既存パッシブ (enemyDice を読むもの) の互換のため。
        /// <paramref name="extraDraws"/> は旧「ダイス数+N」効果の受け皿で、 同じ範囲から追加で引いて足す。
        /// <paramref name="maxOverride"/> は弱ロール等で上限を縮める場合の差し替え (0 以下で無視)。</summary>
        public int[] RollAttack(int extraDraws = 0, int maxOverride = 0)
        {
            if (UsesAttackRoll)
            {
                int hi = maxOverride > 0 ? maxOverride : attackRollMax;
                int lo = Math.Min(Math.Max(0, attackRollMin), hi);
                int n = Math.Max(1, 1 + Math.Max(0, extraDraws));
                var res = new int[n];
                for (int i = 0; i < n; i++)
                    res[i] = GameLoop.GameRng.Range(lo, hi + 1, "enemy.atkRoll." + id, RngSlot(i));
                return res;
            }
            return RollDice(extraDraws, maxOverride);
        }

        /// <summary>[旧方式] ダイスを振り、各出目の配列を返す。固有面ダイスがあればその面から抽選。</summary>
        public int[] RollDice(int extraDice = 0, int maxOverride = 0)
        {
            int count = Math.Max(0, diceCount + extraDice);
            var results = new int[count];
            bool useFaces = diceFaces != null && diceFaces.Length > 0;
            int mx = maxOverride > 0 ? maxOverride : diceMaxValue;
            for (int i = 0; i < count; i++)
            {
                results[i] = useFaces
                    ? diceFaces[GameLoop.GameRng.Range(0, diceFaces.Length, "enemy.dieFace." + id, RngSlot(i))]
                    : GameLoop.GameRng.Range(1, mx + 1, "enemy.die." + id, RngSlot(i));
            }
            return results;
        }

        /// <summary>表記文字列 (新: "1〜9" / 旧: "2d6" / 固有面なら "5d[2,3,3,6,7,8]")</summary>
        public string DiceNotation => UsesAttackRoll
            ? $"{Math.Max(0, attackRollMin)}〜{attackRollMax}"
            : (diceFaces != null && diceFaces.Length > 0
                ? $"{diceCount}d[{string.Join(",", diceFaces)}]"
                : $"{diceCount}d{diceMaxValue}");

        /// <summary>DB共有インスタンスを汚さずに改変するための複製。
        /// passiveSkills もリスト/要素ごと深くコピーする。</summary>
        public EnemyData Clone()
        {
            var c = (EnemyData)MemberwiseClone();
            if (diceFaces != null) c.diceFaces = (int[])diceFaces.Clone();
            if (passiveSkills != null)
            {
                c.passiveSkills = new List<EnemyPassiveEntry>(passiveSkills.Count);
                foreach (var p in passiveSkills)
                    c.passiveSkills.Add(p == null ? null : new EnemyPassiveEntry
                    {
                        internalName = p.internalName,
                        skillName = p.skillName,
                        description = p.description,
                        flavorText = p.flavorText,
                    });
            }
            return c;
        }
    }

    /// <summary>
    /// 敵のパッシブスキルエントリ（JSONデシリアライズ用）
    /// </summary>
    [Serializable]
    public class EnemyPassiveEntry
    {
        public string internalName;
        public string skillName;
        public string description;
        /// <summary>フレーバー文（2026-07-28 追加）。 効果の説明ではなく、 その効果を
        /// **外から見た者の声**。 枠と温度は敵ごとに変える（口伝 / 剣譜 / 診療録 / 検死 / 会話 …）。
        /// 深度が上がるほど地の文へ寄り、 7 層ヴェスカのみ全編が本人の発話で統一されている。</summary>
        public string flavorText;
    }

    /// <summary>
    /// enemies.json のルートラッパー
    /// </summary>
    [Serializable]
    public class EnemyDataList
    {
        public List<EnemyData> enemies;
    }
}
