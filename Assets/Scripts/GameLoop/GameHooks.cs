using System;
using System.Collections.Generic;

namespace GameLoop
{
    /// <summary>
    /// 本体が外 (BOT・計測) へ判断や記録を委ねる口。 <b>本体は AutoTest の型を参照しない</b> (2026-10-29)。
    ///
    /// <para>どの口も既定は null で、 本体はそのとき自分の既定の振る舞いだけで動く。
    /// BOT は AutoRunner の生成時に差し込み、 破棄時に外す (<c>AutoTest.BotHooks</c>)。
    /// したがって通常のプレイでは BOT の学習データも集計も一切効かない。</para>
    /// </summary>
    public static class GameHooks
    {
        /// <summary>BOT がランを動かしている間 true。 人の操作を受ける画面 (2D の戦闘など) はこの間は手を出さない ──
        /// BOT の <c>ExecuteTurn</c> と画面の「人が操作するターン」がぶつからないように。</summary>
        public static bool BotDriving;

        /// <summary>[測定] メタ進行 (<c>MetaProgressManager</c>) を PlayerPrefs へ保存しない・PlayerPrefs から読まない (2026-11-12・#119)。
        /// 並列ワーカー (-batchmode の Player) が起動時に立てる。 PlayerPrefs は全ワーカーで共有されるので、
        /// 前の測定が書いた挑戦の難易度などが次の測定のワーカーへ持ち込まれ、 バッチ開始時に読む学習の帯がワーカーごとに割れていた。
        /// 立てるとエディタ実行と同じく毎回まっさらな状態から始まる。 <see cref="Clear"/> では外さない (プロセス単位の設定)。</summary>
        public static bool MetaPersistenceDisabled;

        // ===== 判断 (プレイヤーの選択を代行する口) =====

        /// <summary>開幕パッシブの提示 (〈宝物庫〉r10 の 2〜3 択) から 1 つ選ぶ。 戻り値は添字。
        /// null なら先頭 (選択画面が付くまでの仮)。</summary>
        public static Func<IReadOnlyList<string>, int> ChooseStartingPassive;
        /// <summary>挑戦カードの 2 枚 ("デバフの id|バフの id") からどれを受けるか。 null なら人が遊んでいる時は画面で選び、 BOT は先頭。</summary>
        public static Func<IReadOnlyList<string>, int> ChooseChallengeCard;
        /// <summary>端子の山札 (ADR-0014): 戦闘後の報酬の端子 (2〜3 択の名前) からどれを山札に加えるか。 戻り値は添字、 負 = 受け取らない。
        /// null なら人が遊んでいる時は画面で選び、 BOT は先頭。</summary>
        public static Func<IReadOnlyList<string>, int> ChooseTerminalReward;
        /// <summary>[測定] 見せる層 (1..5) の入口で、 難易度の引き方の代わりに受けるカード (id の並び)。 null を返す層は何も受けない。
        /// カードを 1 枚ずつ測るための口 (AutoRunner.challengeForce)。</summary>
        public static Func<int, IReadOnlyList<string>> ForcedChallengeCards;

        /// <summary>挑戦カード〈猫の手〉(#5): 出目と予告から、 猫が先に挿す 1 組 <c>{ 出目の添字, 場の端子の添字 }</c> を返す。
        /// <c>{ -1, -1 }</c> = このターンは挿さない。 null を返す (か口が null) = 評価できない ── 本体は既定の挿し方
        /// (<see cref="CombatSystem.TerminalWiring.CatPawFallback"/>) で選ぶ。 場と盤面は <c>CombatManager.Instance</c> から読む (読むだけ)。
        /// <para><b>BOT の判断の口ではない</b> ── 札の効果そのもので、 人が遊ぶ時にも使う。 Super の評価 (AutoRunner の山札の挿し方の効用) を
        /// 本体へ二重に書かないため、 AutoTest 側 (<c>AutoTest.CatPawAdvisor</c>) が起動時に差す。 そのため <see cref="Clear"/> では外さない。</para></summary>
        public static Func<int[], CombatSystem.MutualTurnTelegraph, int[]> CatPawPick;

        /// <summary>自動装備の武器評価に足す点 (武器 id → 加点)。 null なら 0。</summary>
        public static Func<string, float> WeaponEvBonus;

        /// <summary>[測定] 整備パネル〈装甲〉の 1 段あたりの防御 / 開幕シールドを差し替える (負 = 差し替えない・2026-11-10)。</summary>
        public static int ArmorDefPerRankOverride = -1, ArmorShieldPerRankOverride = -1;
        /// <summary>[測定] 〈装甲〉の被ダメージ軽減: 何段ごとに −1 か (0 以下 = 差し替えない)。</summary>
        public static int ArmorFlatEveryOverride = 0;

        /// <summary>[測定] 武器の段の上限 (0 = 上限なし・2026-11-11)。 強化 (休憩) と自動装備がこの段を超えない。
        /// 段 4 の本当の価値 (段 3 のままヴェスカに着いたら) を測る口 (AutoRunner.weaponTierCap)。</summary>
        public static int WeaponTierCap;

        /// <summary>[測定] ヴェスカ連戦 (boss_layer7*) の間、 プレイヤーの出目を全部この値にする (0 = 触らない・2026-11-11)。
        /// 「出目が 1 しか出なかったら」の重さを測る口 (AutoRunner.vescaDiceForce)。 振り直した出目にも掛ける。</summary>
        public static int VescaDiceForce;

        /// <summary>[測定] 物資の素の上限を差し替える (0 = 差し替えない = <see cref="ProvisionRules.Max"/>)。 BOT が上限を振って測る口 (2026-11-09)。</summary>
        public static int ProvisionMaxOverride;

        /// <summary>[測定 2026-11-12・#35] 追跡者に勝った時のアイテムと物資を出さない (「捕まると得」かを確かめる腕)。</summary>
        public static bool PursuerNoReward;
        /// <summary>[測定 2026-11-12・#35] 追跡者の戦闘の攻撃倍率を差し替える (0 以下 = 差し替えない)。</summary>
        public static float PursuerAtkMulOverride;
        /// <summary>[測定 2026-11-12・#35] 追跡者の型ごとの HP 倍率に足す値 (0 = 触らない)。</summary>
        public static float PursuerHpAdd;
        /// <summary>[測定 2026-11-12・#35] 追跡者に勝った時の物資を差し替える (負 = 差し替えない)。</summary>
        public static int PursuerSupplyOverride = -1;

        /// <summary>[測定 2026-11-12・#95] 遺物の効果を止める接辞 (ビット = <c>(int)RelicStat</c>・0 = 何も止めない)。
        /// 主ステもサブも、 その接辞の分だけを 0 にする (段の値 1 つあたりの価値を、 理論値の持ち込みから 1 接辞ずつ抜いて測る口)。
        /// AutoRunner.relicStatOff が毎ランの始めに入れる。</summary>
        public static int RelicStatOffMask;
        /// <summary>[測定 2026-11-12・#95] 遺物のセット効果を止める (ビット = セット × 2 が 2 個の効果・セット × 2 + 1 が 3 個の効果・0 = 止めない)。
        /// AutoRunner.relicSetBonusOff が毎ランの始めに入れる。</summary>
        public static int RelicSetBonusOffMask;
        /// <summary>[測定 2026-11-12・#20/#41] 敵がプレイヤーの防御を削る固定値 (EnemyData.armorPen) を差し替える。
        /// 引数 (敵の id, データの値) → 使う値。 null = 差し替えない (本番)。 AutoRunner.enemyArmorPenSpec が毎ランの始めに入れる。</summary>
        public static Func<string, int, int> EnemyArmorPenOverride;

        /// <summary>[測定 2026-11-12・#102] 端子の調整を腕から元の値へ戻す口 (0 = 差し替えない = 今の値)。
        /// 〈治癒〉の倍率 (<see cref="CombatSystem.TerminalCards.HealMul"/>)。
        /// AutoRunner.terminalHealMul が毎ランの始めに入れ、 ラン終了で 0 へ戻す。 画面の説明文は今の値のまま (測定の口なので)。</summary>
        public static int TerminalHealMulOverride;

        /// <summary>敵の固定削り (データの値・<see cref="EnemyArmorPenOverride"/> があればその値・下限 0)。</summary>
        public static int EnemyArmorPen(string enemyId, int dataValue)
        {
            int b = Math.Max(0, dataValue);
            return EnemyArmorPenOverride != null ? Math.Max(0, EnemyArmorPenOverride(enemyId, b)) : b;
        }

        /// <summary>物資の素の上限 (差し替えがあればそれ)。</summary>
        public static int ProvisionBaseMax => ProvisionMaxOverride > 0 ? ProvisionMaxOverride : ProvisionRules.Max;

        // ===== 計測 (結果を変えない) =====

        /// <summary>ボス戦に入る時点の戦力値 (層別集計 <see cref="RunStats.BossEntryPower"/> に足す)。 null なら 0。</summary>
        public static Func<RunState, int> MeasureRunPower;

        /// <summary>会心率を決めた直後 (逓減前の加算合計, 実効率)。</summary>
        public static Action<float, float> OnCritRateResolved;

        /// <summary>全部の口を外す (BOT の口だけ。 〈猫の手〉の <see cref="CatPawPick"/> は札の効果なので残す)。</summary>
        public static void Clear()
        {
            BotDriving = false;
            ChooseStartingPassive = null;
            ChooseChallengeCard = null;
            ChooseTerminalReward = null;
            ForcedChallengeCards = null;
            WeaponEvBonus = null;
            MeasureRunPower = null;
            OnCritRateResolved = null;
            EnemyArmorPenOverride = null;
            RelicStatOffMask = 0;
            RelicSetBonusOffMask = 0;
            TerminalHealMulOverride = 0;
        }
    }
}
