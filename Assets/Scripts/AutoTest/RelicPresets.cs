using MetaProgression.Relics;

namespace AutoTest
{
    /// <summary>
    /// AutoRunner 用の遺物プリセット。 正本: docs/GAME.md §15-5。
    ///
    /// **なぜ要るか**: 遺物は持ち込み 1 個・コストなしの恒常ボーナスなので、
    /// これを固定しないと L3 ボスチューナーと tier list が「どのプレイヤーを基準にした値か」を
    /// 失う。 MetaAllocationPresets が整備パネルに対して果たしている役割と同じ。
    ///
    /// 天井 (TheoreticalBest) は **29pt ＝ メイン9 + サブ5×4** で、 §15-5 の理論最良
    /// (2026-08-08 の段上限引き上げに追従。 旧 18pt)。
    /// 実際には難度 30 の 5 層以上クリアでしか出ないので、 **上限の測定用**であって
    /// 「よくいるプレイヤー」ではない。 標準は Mid。
    /// </summary>
    public static class RelicPresets
    {
        public enum Preset
        {
            /// <summary>遺物なし。 バランスの床。 周回 0 のプレイヤー。</summary>
            None,
            /// <summary>難度 0 で 5 層クリアを何度か回した頃の標準形（3枠・合計 9）。
            /// **ボス調整の基準はこれ**。</summary>
            Mid,
            /// <summary>難度 25 で 7 層クリアあたりの上位形（5枠・合計 16）。</summary>
            High,
            /// <summary>理論最良 29pt（5枠・メイン9 + サブ5×4）。 天井の測定用。</summary>
            TheoreticalBest,
            /// <summary>理論最良に刻印を載せた形。 メインが段7 になる上振れの上限。</summary>
            TheoreticalBestCursed,
        }

        /// <summary>プリセットを実体化する。 **軸の選び方は固定**（乱数を使わない）──
        /// スイープの比較可能性を保つため、 プリセットは常に同じ遺物を返す。</summary>
        public static RolledRelic Build(Preset p)
        {
            switch (p)
            {
                case Preset.Mid:
                    // 3枠・合計9。 §15-5 の平均化バイアス下で最頻の形 (5,2,2)。
                    return Make(RelicCurse.None,
                        RelicAxis.Attack, 5,
                        RelicAxis.DamagePct, 2,
                        RelicAxis.DamageReductionPct, 2);

                case Preset.High:
                    // 5枠・合計16。 難度25・7層クリア帯で最も出やすい (6,3,3,2,2)。
                    // 2026-08-08: 会心率が引退したので 防御貫通 へ差し替え。
                    return Make(RelicCurse.None,
                        RelicAxis.Attack, 6,
                        RelicAxis.DamagePct, 3,
                        RelicAxis.DamageReductionPct, 3,
                        RelicAxis.ArmorPenPct, 2,
                        RelicAxis.MaxHp, 2);

                case Preset.TheoreticalBest:
                case Preset.TheoreticalBestCursed:
                    // 2026-08-08: 段上限の引き上げ (メイン6→9 / サブ3→5) に追従。
                    //   総点 18 → **29** (メイン9 + サブ5×4)。 期待与ダメ +108% → +174% 相当。
                    return Make(p == Preset.TheoreticalBestCursed ? RelicCurse.NoRoleFired : RelicCurse.None,
                        RelicAxis.Attack, 9,
                        RelicAxis.DamagePct, 5,
                        RelicAxis.DamageReductionPct, 5,
                        RelicAxis.ArmorPenPct, 5,     // 2026-08-08: 会心率 引退につき差し替え
                        RelicAxis.MaxHp, 5);

                default:
                    return null;   // None
            }
        }

        /// <summary>単軸スイープ用。 **メイン枠に指定軸・段N、 サブ 2 枠は全アーム共通の埋め草**。
        ///
        /// **枠 1 本の遺物は作れない。** <see cref="RolledRelic.IsValid"/> が枠数 3〜5 を要求し、
        /// <c>MetaProgressState.EquippedRelic</c> は不正な遺物に null を返す ＝ 効果が一切乗らない。
        /// 実測: 枠 1 本で組んだ版は 17 区分すべてが「遺物なし」で走り、 与ダメ+45% が
        /// 与ダメを 1 も動かさなかった (2026-08-08)。 サブ 2 枠を必ず埋めること。
        ///
        /// サブは全アームで同じ軸・同じ段 1 なので、 **アーム間の差はメイン軸だけに帰属する**。
        /// 埋め草が指定軸と衝突したら次の候補へ送る (同一軸の重複は IsValid が弾く)。
        ///
        /// 段は既定で 9 (通常の上限)。 実測では挑戦 25pt 以上のメインは段8・9 が 6 割強を
        /// 占めるので、 **段9 が「その軸を引き当てたときの実際の姿」**に最も近い。
        ///
        /// 高難易度限定軸 (Λ共鳴/渇き/刻限/背水) もここでは素通しで載る ──
        /// 出現制限は RelicRoller 側の抽選条件であって、 効果側の条件ではない。</summary>
        public static RolledRelic BuildSingleAxis(RelicAxis axis, int step = 9)
        {
            // 埋め草の候補。 開幕系は「戦闘開始時 1 回だけ」なので、 メイン軸の測定に
            // 与える干渉が最も小さい。 段 1 = サブの構造下限。
            var fillerPool = new[] { RelicAxis.OpeningBleed, RelicAxis.OpeningPoison, RelicAxis.Rinkai };
            var r = new RolledRelic { curse = (int)RelicCurse.None };
            r.axes.Add((int)axis); r.steps.Add(step);
            for (int i = 0; i < fillerPool.Length && r.axes.Count < 3; i++)
            {
                if (fillerPool[i] == axis) continue;
                r.axes.Add((int)fillerPool[i]); r.steps.Add(1);
            }
            if (!r.IsValid())
                UnityEngine.Debug.LogError($"[RelicPresets] 単軸遺物が不正: {r.Describe()} — 効果は乗らない");
            return r;
        }

        /// <summary>MetaProgressState へ流し込む。 AutoRunner のラン開始前に呼ぶ。</summary>
        public static void Apply(Preset p)
        {
            var st = MetaProgression.MetaProgressManager.Instance?.State;
            if (st == null) return;
            st.relics = new System.Collections.Generic.List<RolledRelic>();
            st.equippedRelicIndex = -1;
            var r = Build(p);
            if (r != null) st.AddRelic(r);
        }

        public static string Describe(Preset p)
        {
            var r = Build(p);
            return r == null ? "遺物なし" : r.Describe();
        }

        // 可変長の (軸, 段) ペアから 1 個組み立てる。 index 0 がメイン。
        private static RolledRelic Make(RelicCurse curse, params object[] pairs)
        {
            var r = new RolledRelic { curse = (int)curse };
            for (int i = 0; i + 1 < pairs.Length; i += 2)
            {
                r.axes.Add((int)(RelicAxis)pairs[i]);
                r.steps.Add((int)pairs[i + 1]);
            }
            return r;
        }
    }
}
