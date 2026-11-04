using System.Collections.Generic;
using MetaProgression;

namespace UI.Game2D
{
    /// <summary>
    /// 挑戦デバフ (docs/GAME.md §15-2) を選ぶ窓 (<b>仮置き</b>・2026-11-04)。 整備パネル (<see cref="Game2DMetaPanel"/>) の「挑戦」から開く。
    /// 行: 名前 → 点数の列にそろえた分離式ボタン (1pt / 2pt / 3pt / 4pt) → 今の効果。 ボタンは軸に<b>存在する Tier</b> の列だけに置く
    /// (〈厚い皮膚〉は 2pt と 3pt)。 トグル式: 押したら押されたまま、 もう一度押すか同じ軸の別の点を押すと外れる。 頁: A〜C の軸 / D・E の軸 / Tier4 (カテゴリの軸で 6pt 払うと選べる・割ったら外す)。
    /// <para>軸と点は <see cref="ChallengeCatalog"/>、 今の効果の数字は <see cref="MetaDebuffApplicator"/> が今の構成から返す値をそのまま出す (写しを持たない)。
    /// 旧スキルツリーの DEBUFF 側 (星座の 10 ノードで軸を順送り) は 2026-11-04 に削除した。</para>
    /// </summary>
    public static class Game2DChallengePanel
    {
        private static int _page;
        private static Game2DModal _modal;

        private static readonly ChallengeCategory[][] Pages =
        {
            new[] { ChallengeCategory.A生存圧, ChallengeCategory.B敵強化, ChallengeCategory.C戦闘則 },
            new[] { ChallengeCategory.D経済, ChallengeCategory.E崩壊 },
        };
        private const int T4Page = 2, PageCount = 3;

        /// <summary>軸の説明 (下の欄)。 数字は書かない ── 今の値は行の「今の効果」に出る。</summary>
        private static readonly Dictionary<ChallengeAxis, string> AxisDesc = new Dictionary<ChallengeAxis, string>
        {
            { ChallengeAxis.長引く負傷,   "受けたダメージの一定の割合だけ、 ラン中の最大 HP が減っていく。" },
            { ChallengeAxis.穴の空いた鞄, "消耗品を同時に持てる数に上限が付く。" },
            { ChallengeAxis.遅い回復,     "あらゆる回復量が減る。" },
            { ChallengeAxis.厚い皮膚,     "すべての敵の最大 HP が増える。" },
            { ChallengeAxis.狂暴化,       "すべての敵の攻撃が強くなる。" },
            { ChallengeAxis.綻び,         "一定のターンごとに端子が 1 つ封印される (どれが塞がるかは予告に出る)。" },
            { ChallengeAxis.見放された運, "戦闘の最初の数ターン、 ダイス 1 本の出目が 1 になる (振り直しで戻せる)。" },
            { ChallengeAxis.搾取経済,     "ショップの値段が上がる。" },
            { ChallengeAxis.通行料,       "層を移るたびに、 所持金の一部を失う。" },
            { ChallengeAxis.宿屋連合,     "前哨基地で休むたびに GOLD を払う。" },
            { ChallengeAxis.絶望的な戦闘, "物資の上限が下がる。 2pt 以上は、 深い層の戦闘のあとに物資がさらに減る。" },
            { ChallengeAxis.天変地異,     "ボスの攻撃が、 ターンを追うごとに強くなっていく。" },
        };

        private static readonly Dictionary<ChallengeT4, string> T4Desc = new Dictionary<ChallengeT4, string>
        {
            { ChallengeT4.破綻,       "休憩・秘宝・交換のマスが、 着いてみると何も無いことがある。" },
            { ChallengeT4.鋼の皮膚,   "各戦闘で 1 度、 敵が致命傷を HP1 で耐える。 ボスの最大 HP が増える。" },
            { ChallengeT4.凶運,       "ランの始めに、 役がいくつか封印される。" },
            { ChallengeT4.破産,       "開幕の GOLD が減り、 売値が下がる。" },
            { ChallengeT4.最後の審判, "戦闘の累計ターンが一定を超えると、 受けるダメージが増える。" },
        };

        /// <summary>整備パネルから挑戦の頁へ切り替える (同じ窓の中身を差し替える)。</summary>
        public static void Show(Game2DModal modal)
        {
            _modal = modal; _page = 0;
            _modal.Replace(Build());
        }

        private static string Letter(ChallengeCategory c) => c.ToString().Substring(0, 1);

        private static ScreenDef Build()
        {
            var mpm = MetaProgressManager.Instance; var st = mpm.State; var ch = st.Challenge;
            int score = st.ChallengeScore;
            var d = new ScreenDef
            {
                id = "challenge_panel", jp = true, w = 472, cols = 1, rowH = 15, segments = true, sliderNameW = 96, segW = 30,
                title = _page == T4Page ? "挑戦 ― Tier4" : "挑戦 ― " + string.Join("・", System.Array.ConvertAll(Pages[_page], Letter)),
                sub = "挑戦スコア " + score + "/" + ChallengeCatalog.TotalMaxScore + "  (" + ChallengeCatalog.ScoreBandName(score) + ")",
            };
            var axes = new List<AxisDef>(); var t4s = new List<T4Def>();
            if (_page == T4Page) t4s.AddRange(ChallengeCatalog.T4s);
            else foreach (var cat in Pages[_page]) axes.AddRange(ChallengeCatalog.AxesOf(cat));

            foreach (var a in axes)
            {
                int tier = ch.Tier(a.axis);
                var segs = new string[ChallengeCatalog.AxisMaxPoints];
                foreach (int t in a.tiers) segs[t - 1] = t + "pt";      // 列 = 点数 (1〜3pt)。 無い段の列は空ける
                d.Items.Add(new ModalItem
                {
                    name = Letter(a.category) + " " + a.displayName, segs = segs, segOn = tier - 1,
                    effect = tier > 0 ? AxisEffect(a.axis) : "なし",
                    desc = AxisDesc.TryGetValue(a.axis, out var ds) ? ds + "  (" + TierList(a) + ")" : TierList(a),
                });
            }
            foreach (var t in t4s)
            {
                bool on = ch.Has(t.t4), open = ChallengeResolver.CanSelectT4(st.challenge, t.t4);
                d.Items.Add(new ModalItem
                {
                    name = Letter(t.category) + " " + t.displayName, disabled = !open,
                    segs = T4Segs(t), segOn = on ? 0 : -1,
                    effect = on ? T4Effect(t.t4) : open ? "なし" : Letter(t.category) + " の軸で " + ChallengeCatalog.CategoryBaseMax + "pt 払うと選べる",
                    desc = (T4Desc.TryGetValue(t.t4, out var ds) ? ds : "") + "  " + Letter(t.category) + " の軸で " + ChallengeCatalog.CategoryBaseMax + "pt 払うと選べる。",
                });
            }

            d.buttons.Add(new ModalBtn { label = "次の頁 " + (_page + 1) + "/" + PageCount, action = "page", primary = true });
            d.buttons.Add(new ModalBtn { label = "全解除", action = "clear" });
            d.buttons.Add(new ModalBtn { label = "整備へ", action = "back" });
            d.buttons.Add(new ModalBtn { label = "閉じる", action = "close" });
            d.footer = "点を押して選ぶ (もう一度押すと外す)。 合計が挑戦スコア (" + ChallengeCatalog.TotalMaxScore + "pt まで)";
            d.onAction = (act, sel) =>
            {
                switch (act)
                {
                    case "close": Game2DMetaPanel.Close(); return;
                    case "back": Game2DMetaPanel.Show(); return;
                    case "page": _page = (_page + 1) % PageCount; break;
                    case "clear": mpm.ClearChallenge(); _modal.ShowToast("挑戦を全部外した"); break;
                    case "seg":
                    {
                        // トグル: 押されている列をもう一度押すと外す。 同じ軸の別の列を押すとそちらへ移る
                        int col = Game2DModal.SegTarget, cur = sel >= 0 && sel < d.Items.Count ? d.Items[sel].segOn : -1;
                        int goal = col == cur ? 0 : col + 1;      // goal = 点数 (0 = 外す)
                        if (sel >= 0 && sel < axes.Count)
                        {
                            var a = axes[sel];
                            int tier = goal;
                            mpm.SetChallengeTier(a.axis, tier);
                            // Tier4 を選んでいるカテゴリの軸を下げて 6pt を割ったら、 その Tier4 を外す (§15-2)。 残すと解決で黙って無効になる
                            foreach (var t in ChallengeCatalog.T4s)
                                if (t.category == a.category && st.challenge.HasT4(t.t4) && !ChallengeResolver.CanSelectT4(st.challenge, t.t4))
                                { mpm.SetChallengeT4(t.t4, false); _modal.ShowToast("〈" + t.displayName + "〉を外した (" + Letter(t.category) + " が " + ChallengeCatalog.CategoryBaseMax + "pt を割った)"); }
                        }
                        else if (sel >= 0 && sel < t4s.Count)
                        {
                            var t = t4s[sel];
                            if (goal > 0 && !ChallengeResolver.CanSelectT4(st.challenge, t.t4)) { _modal.ShowToast(Letter(t.category) + " の軸で " + ChallengeCatalog.CategoryBaseMax + "pt 払うと選べる"); break; }
                            mpm.SetChallengeT4(t.t4, goal > 0);
                        }
                        Game2DSfx.Play("ui_click");
                        break;
                    }
                }
                _modal.Replace(Build());
            };
            return d;
        }

        /// <summary>Tier4 の行のボタン (4pt の 1 列)。 基礎軸の行は 1〜3pt の 3 列 ── 点数で列をそろえるので、 軸をまたいで縦に並ぶ。</summary>
        private static string[] T4Segs(T4Def t) => new[] { t.points + "pt" };

        private static string TierList(AxisDef a) => string.Join(" / ", System.Array.ConvertAll(a.tiers, t => t + "pt"));

        /// <summary>今の構成での軸の効果 (短く)。 数字は MetaDebuffApplicator が今の構成から返す値。</summary>
        private static string AxisEffect(ChallengeAxis a)
        {
            switch (a)
            {
                case ChallengeAxis.長引く負傷:   return "被ダメの " + (MetaDebuffApplicator.GetLingeringWoundRatio() * 100f).ToString("0") + "% 最大 HP 減";
                case ChallengeAxis.穴の空いた鞄: return "消耗品は " + MetaDebuffApplicator.GetConsumableCarryCap() + " 個まで";
                case ChallengeAxis.遅い回復:     return "回復 ×" + MetaDebuffApplicator.GetHealMultiplier().ToString("0.00");
                case ChallengeAxis.厚い皮膚:     return "敵の HP ×" + MetaDebuffApplicator.GetEnemyHpMultiplier().ToString("0.###");
                case ChallengeAxis.狂暴化:       return "敵の攻撃 ×" + MetaDebuffApplicator.GetEnemyDamageMultiplier().ToString("0.00");
                case ChallengeAxis.綻び:         return MetaDebuffApplicator.GetTerminalSealPeriod() + "T ごとに端子を封印";
                case ChallengeAxis.見放された運:
                {
                    int n = 0; while (n < 50 && MetaDebuffApplicator.GetForsakenLuckDiceCount(n + 1) > 0) n++;
                    return "最初の " + n + "T ダイス 1 本が 1";
                }
                case ChallengeAxis.搾取経済:     return "店の値段 ×" + MetaDebuffApplicator.GetShopPriceMultiplier().ToString("0.00");
                case ChallengeAxis.通行料:       return "層を移ると所持金 −" + (MetaDebuffApplicator.GetFloorTollRatio() * 100f).ToString("0") + "%";
                case ChallengeAxis.宿屋連合:     return "休むたび −" + MetaDebuffApplicator.GetRestGoldCost() + "G";
                case ChallengeAxis.絶望的な戦闘: return "物資の上限 −" + (-MetaDebuffApplicator.GetProvisionCapDelta());
                case ChallengeAxis.天変地異:     return "ボスの攻撃 毎ターン +" + ((MetaDebuffApplicator.GetBossAttackMultiplier(2) - 1f) * 100f).ToString("0.#") + "%";
            }
            return "";
        }

        private static string T4Effect(ChallengeT4 t)
        {
            switch (t)
            {
                case ChallengeT4.破綻:       return "有利マスが " + (MetaDebuffApplicator.GetVoidTileChance() * 100f).ToString("0") + "% で空振り";
                case ChallengeT4.鋼の皮膚:   return "致命傷を 1 度耐える・ボス HP ×" + MetaDebuffApplicator.GetBossHpMultiplier().ToString("0.##");
                case ChallengeT4.凶運:       return "役を " + MetaDebuffApplicator.SealedRoleCount + " つ封印";
                case ChallengeT4.破産:       return "開幕 −" + MetaDebuffApplicator.GetStartingGoldPenalty() + "G・売値 ×" + MetaDebuffApplicator.GetSellPriceMultiplier().ToString("0.##");
                case ChallengeT4.最後の審判: return MetaDebuffApplicator.GetJudgmentTurnThreshold() + "T 以降 被ダメ増";
            }
            return "";
        }
    }
}
