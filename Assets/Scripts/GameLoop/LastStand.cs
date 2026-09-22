using UnityEngine;

namespace GameLoop
{
    /// <summary>
    /// ラストスタンド: ランを通して1度だけ発動する救済システム。
    ///
    /// 仕様（2026-08-04 リワーク / 2026-09-12 代償を復活 / 2026-09-13 その場蘇生へ）:
    /// - **メタ〈外殻〉r10 の特典**。 既定では発動しない (MetaPanel.ShellLastStandUnlocked)
    /// - **最大HP を半減し、 半減後の 15% の HP で生還**。 <b>ラン中 1 回限り</b>
    /// - **戦闘から退却しない。 その場で蘇生して戦闘を続ける**
    ///   (<see cref="TryConsumeLastStandInCombat"/> ＝ HP が 0 になったターンの中で解決)
    /// - ちいさな灯火 → ラストスタンド → フルーレ・バレエ の順で消費される
    /// - 発動後のガード処理は一切なし。 以降は通常どおりダメージを受け、再度HP0ならゲームオーバー
    ///
    /// <para><b>なぜ「その場で蘇生」なのか (2026-09-13)。</b> 灯火とフルーレは
    /// 「戦闘を畳んでマップへ戻る」形の救済で、 だからこそボス戦では禁止されている
    /// ── ボスノードは収束ノードで出力接続が無く、 戻ると移動先が無い。
    /// ラストスタンドだけをボス戦で解禁したのに退却経路は共通のままだったので、
    /// <b>ボス戦で発動したランがマップ上で進行不能になっていた</b>
    /// (実測 300 ラン中 61 件 = 20.3% が DEADLOCK: 移動先なし)。
    /// 戦闘内で蘇生すれば退却が起きないので、 この詰みは構造ごと消える。</para>
    ///
    /// 移設の理由: 既定救済だと「一度は死ねる」が全ランの前提になり道中のリスク判断が緩む。
    /// 実測で 62.6% のランが発動しており、 救済ではなく標準装備になっていた。
    /// また最大HP半減は、 救済を受けた側をそのまま詰ませる方向にしか働いていなかった。
    /// </summary>
    public static class LastStand
    {
        // ============================================================
        //  [計装] 救済の発動元 (2026-08-11)
        // ============================================================
        //  T4〈最後の審判〉が **デバフなのに 7層クリアを +1.1pt 上げた** (p=0.027)。
        //  素の基準の上・契約なし・遺物なしで、 1 ラン 258 HP 削ってなお勝率が上がる。
        //  疑っているのは**ここ**: HP が 0 になると灯火/ラストスタンドが**全回復**で拾う。
        //  削って 0 にすること自体が全回復の引き金なので、
        //  「40 削って 93 まで戻る」＝**削られた方が得**になりうる。
        //  灯火とフルーレはボス戦では発動しないので、 持ったまま最終ボスへ行くと死に札。
        //  刻限が道中でそれを現金化している、 という仮説。

        /// <summary>0=灯火 / 1=ラストスタンド / 2=フルーレ の発動回数。</summary>
        public static readonly long[] RevivalCount = new long[3];
        /// <summary>救済で戻した HP の合計 (灯火・ラストスタンドのみ)。</summary>
        public static long RevivalHpRestored;
        public static void ResetStats()
        {
            RevivalCount[0] = RevivalCount[1] = RevivalCount[2] = 0;
            RevivalHpRestored = 0;
        }

        /// <summary>生還時の HP。 <b>半減した後の</b>最大HP に対する割合 (切り上げ・最低 1)。</summary>
        public const float RevivalHpPct = 0.15f;

        /// <summary>ラストスタンドが既に発動済みか確認する（保存対象）。</summary>
        public static bool IsActive(RunState run)
            => run != null && run.lastStandActive;

        /// <summary>ラストスタンドが未発動か確認する。</summary>
        public static bool IsAvailable(RunState run)
            => run != null && !run.lastStandActive;

        /// <summary>
        /// HP が 0 になった直前に呼ぶ。ちいさな灯火 → ラストスタンド の順に試す。
        /// 救済に成功した場合 true。失敗ならゲームオーバー処理に進む。
        /// </summary>
        /// <param name="isBossFight">ボスマスでの戦闘か。 灯火とフルーレ・バレエはボス戦では発動しない
        /// (ボスノードは収束ノードで出力接続が無く、 復活して戻ると進行不能になるため)。</param>
        ///
        /// <remarks><b>ラストスタンドはここに居ない。</b> 2026-09-13 に
        /// <see cref="TryConsumeLastStandInCombat"/> へ移した ── この関数は
        /// 「戦闘を畳んでマップへ戻る」救済だけを扱う。</remarks>
        public static bool TryConsumeRevival(RunState run, bool isBossFight = false)
        {
            if (run == null) return false;

            // 1. ちいさな灯火（一度きり全回復）
            if (!isBossFight && InventorySystem.PassiveItems.TorchRevival.TryConsume(run))
            {
                RevivalCount[0]++;
                RevivalHpRestored += run.playerHP;
                return true;
            }

            // 2. フルーレ・バレエ（[剣の舞]）: 敗北時の最終救済。
            //    このアイテムを廃棄し、最大HP=1 で生還する（灯火・ラストスタンドが尽きた後の捨て身）。
            //    ※昇華済み（グリッド外・永続）の場合は廃棄不能のため発動しない。
            if (!isBossFight && run.ownedPassiveItems != null
                && run.ownedPassiveItems.Contains(SwordDanceSet.FleuretBallet))
            {
                int idx = run.ownedPassiveItems.IndexOf(SwordDanceSet.FleuretBallet);
                InventorySystem.Helpers.PassiveAddHelper.RemoveAt(run, idx);
                run.playerMaxHP = 1;
                run.playerHP = 1;
                RevivalCount[2]++;
                Debug.Log("[フルーレ・バレエ] 敗北の救済: 自壊し最大HP1で生還");
                return true;
            }

            return false;
        }

        /// <summary><b>ラストスタンド: 戦闘の中で、 その場で蘇生する。</b>
        /// HP が 0 になったターンの被ダメージ適用が終わった直後に呼ぶ。
        /// 発動したら true ── 呼び出し側は HP を戦闘側へ同期して<b>戦闘を続ける</b>。
        ///
        /// <para><b>灯火に道を譲る。</b> 消費順は 灯火 → ラストスタンド → フルーレ で、
        /// 灯火は全回復、 こちらは最大HP半減の上で HP1。 先に安い方を焼くと損なので、
        /// <b>灯火がまだ効く状況 (所持していて、 かつボス戦でない) では発動しない</b>。
        /// その場合は従来どおり戦闘終了後の <see cref="TryConsumeRevival"/> が灯火を使う。</para>
        ///
        /// <para>メタデバフ Lv10 天変地異 が有効なら発動しない。</para></summary>
        /// <param name="isBossFight">ボスマスでの戦闘か。 灯火が使えるかの判定にだけ使う
        /// ── <b>ラストスタンド自体はボス戦でも発動する</b>。</param>
        public static bool TryConsumeLastStandInCombat(RunState run, bool isBossFight)
        {
            if (run == null || run.lastStandActive) return false;
            if (!MetaProgression.MetaBuffApplicator.IsLastStandUnlocked()) return false;
            if (MetaProgression.MetaDebuffApplicator.IsLastStandDisabled()) return false;
            // 灯火が先。 ボス戦では灯火が発動しないので、 そのときは譲らない。
            if (!isBossFight && run.ownedPassiveItems != null
                && run.ownedPassiveItems.Contains(
                    InventorySystem.PassiveItems.TorchRevival.TorchId))
                return false;

            run.lastStandActive = true;
            // **最大HP半減を復活 (2026-09-12)。** 2026-08-04 に廃止していたが、
            //   専用化 (外殻 r10 の特典化) と無償化を同時にやったため、
            //   **10 段目 1pt が +11.87pt の崖**になっていた (r9 vs r10 実測)。
            //   発動率も 62.6% → 53.6% としか下がっておらず、
            //   移設の目的 (「一度は死ねる」を前提から外す) は達成できていなかった。
            int before = run.playerMaxHP;
            run.playerMaxHP = Mathf.Max(1, run.playerMaxHP / 2);
            // **生還 HP は半減後の最大HP の 15% (切り上げ・最低 1)。**
            //   経緯: 半減(50%) → 25% → 1 → 15% (2026-09-13)。
            //   1 に落としたのは退却救済だった頃の話で、 その場蘇生へ移した後は
            //   「蘇生したターンの敵の一撃でそのまま落ちる」だけになり、
            //   極点として r10−r9 +0.91pt (CI ±1.1) ＝ ゼロと区別できなかった。
            //   15% なら一撃は耐えうるが立て直しは自力、 という位置。
            run.playerHP = Mathf.Max(1, Mathf.CeilToInt(run.playerMaxHP * RevivalHpPct));
            RevivalCount[1]++;
            RevivalHpRestored += run.playerHP;
            Debug.Log($"[ラストスタンド] その場で蘇生(外殻r10): 最大HP {before} → {run.playerMaxHP} へ半減、 "
                    + $"HP {run.playerHP} ({RevivalHpPct:P0}) で戦闘続行" + (isBossFight ? " (ボス戦)" : ""));
            return true;
        }

        /// <summary>
        /// 旧ガード処理の互換用パススルー（ラストスタンドは経済を制限しない）。
        /// 既存呼び出し箇所を壊さないため残置。常に amount をそのまま返す。
        /// </summary>
        public static int FilterGoldGain(RunState run, int amount) => amount;

        /// <summary>
        /// 旧ガード処理の互換用パススルー（ラストスタンドは最大HP増加を妨げない）。
        /// 既存呼び出し箇所を壊さないため残置。常に gain をそのまま返す。
        /// </summary>
        public static int FilterMaxHPGain(RunState run, int gain) => gain;
    }
}
