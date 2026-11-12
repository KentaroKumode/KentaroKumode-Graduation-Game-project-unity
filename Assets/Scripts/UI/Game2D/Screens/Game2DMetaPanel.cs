using System.Collections.Generic;
using System.Globalization;
using MetaProgression;
using UnityEngine;

namespace UI.Game2D
{
    /// <summary>
    /// 整備パネル (docs/GAME.md §15-1) の窓 (<b>仮置き</b>・2026-11-04)。 タイトルの「SKILL TREE」から開く。
    /// トークンでポイントを買い、 トラックへ 1 段ずつ配る (拠点では無料で戻せる・全部戻せる)。 ゲームの処理は <see cref="MetaProgressManager"/> の既存の口だけ。
    /// 頁: 数値系 8 本 (max 10・1 段 1 点) / 宣言系 7 本 (種火 5 本・1 段 2 点 + 端子 2 本・1 段 3 点・どれも max 3)。 費用の正本は <see cref="MetaPanel.RankCost"/>。 挑戦 (§15-2) は「挑戦」ボタンの窓 (<see cref="Game2DChallengePanel"/>) で選ぶ。
    /// <para>旧スキルツリーの BUFF 側は 2026-07-25 に廃止した 58 段の一直線トラックのままで、 今の配点はどこからもできなかった。</para>
    /// </summary>
    public static class Game2DMetaPanel
    {
        private struct Track { public MetaPanelKind kind; public string name, desc; }

        // **説明文の数字は MetaPanel の式・定数から組む** (2026-11-12・#125)。 手書きの数字は調整のたびに古くなり、
        //   種火 r1 (+60% のまま・実際は +100%)・毒/出血 r2・r3 の品の数・金庫 r10 が画面とコードで食い違っていた。
        private static string N(float v) => v.ToString("0.#", CultureInfo.InvariantCulture);
        /// <summary>1 段ごとの増分 (r1 の値)。</summary>
        private static string Per(System.Func<int, float> f) => N(f(1));

        /// <summary>宝物庫で開幕パッシブが 1 本増える段 (例 "r3/6/9")。</summary>
        private static string SupplySteps()
        {
            var steps = new List<string>();
            for (int r = 1; r <= 10; r++)
                if (MetaPanel.SupplyStartingPassives(r) > MetaPanel.SupplyStartingPassives(r - 1)) steps.Add(r.ToString());
            return "r" + string.Join("/", steps);
        }

        private static string SaleTiers(int rank) => string.Join("/", MetaPanel.TradeSaleTiers(rank));

        /// <summary>種火 r1・r3 の共通の文。 r2 は呼ぶ側が渡す。</summary>
        private static string SparkDesc(string kw, string r2)
            => "r1: " + kw + "の品が出やすい (+" + N((MetaPanel.SparkAppearanceWeightMul(1) - 1f) * 100f) + "%)。 r2: " + r2
             + "。 r3: " + kw + "の品を " + MetaPanel.SparkStartingItemCount + " つ開幕で持つ。";

        private static readonly Track[] Numeric =
        {
            new Track { kind = MetaPanelKind.Shell,   name = "外殻", desc = "最大 HP +" + MetaPanel.ShellHp(1) + "/段 (r10 は +" + (MetaPanel.ShellHp(10) - MetaPanel.ShellHp(9)) + ")。 r10: ラストスタンド (その場で蘇生・最大 HP 半減の代償つき)。" },
            new Track { kind = MetaPanelKind.Output,  name = "出力", desc = "与ダメ +" + MetaPanel.OutputPct(1) + "%/段。 r10〈戦意〉: 戦闘に勝つたび与ダメ +" + N(MetaPanel.BattleSpiritPctPerWin) + "% (そのラン中)。" },
            new Track { kind = MetaPanelKind.Guard,   name = "剛胆", desc = "エリートが増える +" + Per(MetaPanel.ValorEliteUpgradePct) + "%/段・エリートの報酬ゴールド +" + Per(MetaPanel.ValorEliteGoldPct) + "%/段。 r10: エリートへの与ダメ +" + MetaPanel.ValorEliteSlayerPct + "%・残りシールドの " + N(MetaPanel.GuardShieldCarryRatio * 100f) + "% を次戦へ。" },
            new Track { kind = MetaPanelKind.Vault,   name = "金庫", desc = "開幕ゴールド +" + MetaPanel.VaultGold(1) + "/段。 r10: 宝箱のゴールドが復活・層を越えるたびに所持金が " + N(VaultBank.Multiplier) + " 倍 (" + VaultBank.Cap + "G までの分)。" },
            new Track { kind = MetaPanelKind.Plunder, name = "強奪", desc = "ボス撃破の追加ゴールド +" + MetaPanel.PlunderBossGold(1) + "/段・通常の敵の秘宝ドロップ +" + Per(MetaPanel.PlunderPassiveDropPct) + "%/段。 r10: ショップ強盗を解禁。" },
            new Track { kind = MetaPanelKind.Trade,   name = "商才", desc = "ショップの特売枠 +" + MetaPanel.TradeSaleSlots(1) + "/段 (" + SaleTiers(1) + "% 引き)。 r10: " + MetaPanel.TradeSaleTiers(10)[MetaPanel.TradeSaleTiers(10).Length - 1] + "% 引きの枠が出る。" },
            new Track { kind = MetaPanelKind.Supply,  name = "宝物庫", desc = SupplySteps() + " で開幕の秘宝 +1 個・段が上がるほどレア度が上寄り。 r10: 開幕の秘宝が選択式。" },
            new Track { kind = MetaPanelKind.Armor,   name = "装甲", desc = "防御 +" + MetaPanel.ArmorDefense(1) + "/段・被ダメージ −1/" + MetaPanel.ArmorFlatEvery + " 段。 r10〈不動〉: 1 回の攻撃で受けるダメージは最大 HP の " + N(MetaPanel.ArmorHitCapPct * 100f) + "% まで。 防御は割合の軽減 (積むほど伸びは鈍る)、 被ダメージ −N は防御の後に引く。" },
        };

        private static readonly Track[] Declare =
        {
            new Track { kind = MetaPanelKind.SparkCharge, name = "種火・充電", desc = SparkDesc("充電", "充電の上限 +" + MetaPanel.ChargeCapBonus(2) + "・戦闘開始時の充電 +" + MetaPanel.ChargeOpening(2)) },
            new Track { kind = MetaPanelKind.SparkRinkai, name = "種火・臨界", desc = SparkDesc("臨界", "戦闘の終わりの臨界の " + N(MetaPanel.RinkaiCarryoverRatio(2) * 100f) + "% を次の戦闘へ") },
            new Track { kind = MetaPanelKind.SparkPoison, name = "種火・毒",   desc = SparkDesc("毒", "毎ターン最初に与える毒 +" + MetaPanel.PoisonApplyBonus(2)) },
            new Track { kind = MetaPanelKind.SparkBleed,  name = "種火・出血", desc = SparkDesc("出血", "出血 " + MetaPanel.BleedWeakenThreshold + " 以上の敵の攻撃値 −" + MetaPanel.BleedWeakenAmount) },
            new Track { kind = MetaPanelKind.SparkShield, name = "種火・シールド", desc = SparkDesc("シールド", "ターン開始の減衰で残るシールド +" + MetaPanel.ShieldKeepBonusPct(2) + "%") },
            new Track { kind = MetaPanelKind.AttackTerm,  name = "攻撃端子",   desc = "攻撃の端子を挿したダイス 1 個ごとに、 攻撃端子の配線合計 +" + MetaPanel.AttackTerminalPerDice(1) + "/段。" },
            new Track { kind = MetaPanelKind.BlockTerm,   name = "防御端子",   desc = "防御の端子を挿したダイス 1 個ごとに、 防御端子の配線合計 +" + MetaPanel.BlockTerminalPerDice(1) + "/段。" },
        };

        private static int _page;
        private static Game2DModal _modal;

        /// <summary>窓を開く。 開けなければ false (モーダルが無い・メタ進行が無い)。</summary>
        public static bool Open()
        {
            if (_modal == null) _modal = Object.FindObjectOfType<Game2DModal>();
            if (_modal == null || MetaProgressManager.Instance == null || MetaProgressManager.Instance.State == null) return false;
            _page = 0;
            SpriteButton.InputLocked = true;      // 後ろのタイトルのボタンを押さない
            _modal.Open(Build());
            Game2DTutorial.Request(TutorialTopics.MetaPanel);      // 手引き「整備パネル」: 初めて開いた時 (issue #86)
            return true;
        }

        /// <summary>整備パネルの頁へ戻す (挑戦の窓の「整備へ」)。</summary>
        internal static void Show() => _modal.Replace(Build());

        internal static void Close()
        {
            SpriteButton.InputLocked = false;
            _modal.Close();
        }

        /// <summary>今の段の効果 (短く)。 数字は MetaPanel の式から取る (説明文の写しから書かない)。</summary>
        private static string Effect(MetaPanelKind k, int r)
        {
            if (r <= 0) return "なし";
            string ex = r >= 10 ? "・極点" : "";
            switch (k)
            {
                case MetaPanelKind.Shell:   return "最大 HP +" + MetaPanel.ShellHp(r) + (r >= 10 ? "・ラストスタンド" : "");
                case MetaPanelKind.Output:  return "与ダメ +" + MetaPanel.OutputPct(r) + "%" + (r >= 10 ? "・戦意" : "");
                case MetaPanelKind.Guard:   return "エリート化 " + MetaPanel.ValorEliteUpgradePct(r).ToString("0") + "%・報酬 +" + MetaPanel.ValorEliteGoldPct(r).ToString("0") + "%" + ex;
                case MetaPanelKind.Vault:   return "開幕 +" + MetaPanel.VaultGold(r) + "G" + (r >= 10 ? "・貸金庫" : "");
                case MetaPanelKind.Plunder: return "ボス +" + MetaPanel.PlunderBossGold(r) + "G・ドロップ +" + MetaPanel.PlunderPassiveDropPct(r).ToString("0") + "%" + (r >= 10 ? "・強盗" : "");
                case MetaPanelKind.Trade:   return "特売の枠 " + MetaPanel.TradeSaleSlots(r) + (r >= 10 ? "・99% 引き" : "");
                case MetaPanelKind.Supply:  return "開幕の秘宝 " + MetaPanel.SupplyStartingPassives(r) + " 個" + (r >= 10 ? "・選べる" : "");
                case MetaPanelKind.Armor:   return "防御 +" + MetaPanel.ArmorDefense(r) + "・被ダメ −" + MetaPanel.ArmorFlatReduction(r) + (r >= 10 ? "・不動" : "");
                case MetaPanelKind.AttackTerm: return "挿した 1 個ごと +" + MetaPanel.AttackTerminalPerDice(r);
                case MetaPanelKind.BlockTerm:  return "挿した 1 個ごと +" + MetaPanel.BlockTerminalPerDice(r);
            }
            // 種火 6 本: r1 出やすい / r2 固有 / r3 開幕に 1 つ
            string r2 = k == MetaPanelKind.SparkCharge ? "上限 +" + MetaPanel.ChargeCapBonus(r)
                      : k == MetaPanelKind.SparkRinkai ? "持ち越し " + (MetaPanel.RinkaiCarryoverRatio(r) * 100f).ToString("0") + "%"
                      : k == MetaPanelKind.SparkPoison ? "毎ターン最初の付与 +" + MetaPanel.PoisonApplyBonus(r)
                      : k == MetaPanelKind.SparkShield ? "残るシールド +" + MetaPanel.ShieldKeepBonusPct(r) + "%"
                      : "攻撃 −" + MetaPanel.BleedWeakenAmount;
            return "出やすい" + (r >= 2 ? "・" + r2 : "") + (MetaPanel.SparkStartingItemUnlocked(r) ? $"・開幕に {MetaPanel.SparkStartingItemCount} つ" : "");
        }

        /// <summary>頁の 1 段あたりの費用の一文。 費用は <see cref="MetaPanel.RankCost"/> から取る (写しを持たない)。
        /// 頁の中で費用が揃っていれば「1 段 N 点。」、 割れていれば「1 段: 種火 2 点・攻撃端子/防御端子 3 点。」。</summary>
        private static string CostLine(Track[] tracks)
        {
            var groups = new List<(int cost, List<string> names)>();
            foreach (var t in tracks)
            {
                int c = MetaPanel.RankCost(t.kind);
                string label = MetaPanel.IsSpark(t.kind) ? "種火" : t.name;
                var g = groups.Find(x => x.cost == c);
                if (g.names == null) { g = (c, new List<string>()); groups.Add(g); }
                if (!g.names.Contains(label)) g.names.Add(label);
            }
            if (groups.Count == 1) return "1 段 " + groups[0].cost + " 点。 ";
            var parts = new List<string>();
            foreach (var g in groups) parts.Add(string.Join("/", g.names) + " " + g.cost + " 点");
            return "1 段: " + string.Join("・", parts) + "。 ";
        }

        private static ScreenDef Build()
        {
            var mpm = MetaProgressManager.Instance; var st = mpm.State;
            var tracks = _page == 0 ? Numeric : Declare;
            int unassigned = st.UnassignedPoints(), cost = mpm.NextPointCost;
            var d = new ScreenDef
            {
                id = "meta_panel", jp = true, w = 472, cols = 1, rowH = 15, sliders = true, sliderNameW = 74,
                title = _page == 0 ? "整備パネル ― 数値" : "整備パネル ― 宣言",
                sub = "トークン " + st.tokens + "  未配分 " + unassigned + "pt  (" + st.panelPointsPurchased + "/" + MetaPanel.MaxPoints + ")",
            };
            foreach (var t in tracks)
            {
                int rank = st.GetRank(t.kind);
                d.Items.Add(new ModalItem { name = t.name, count = rank, max = t.kind.MaxRank(), desc = t.desc, effect = Effect(t.kind, rank) });
            }
            d.buttons.Add(new ModalBtn { label = mpm.IsPointCapReached ? "pt 上限" : "pt を買う " + cost, action = "buy", primary = true });
            d.buttons.Add(new ModalBtn { label = _page == 0 ? "宣言へ" : "数値へ", action = "page" });
            d.buttons.Add(new ModalBtn { label = "全戻し", action = "respec" });
            d.buttons.Add(new ModalBtn { label = "挑戦", action = "challenge" });
            d.buttons.Add(new ModalBtn { label = "遺物", action = "relic" });
            d.buttons.Add(new ModalBtn { label = "閉じる", action = "close" });
            d.footer = CostLine(tracks) + "バーを押す・引いて段を決める (戻すのは無料)" + (mpm.IsPointCapReached ? "" : "。 1 点はトークン " + cost + " で買える");
            d.onAction = (a, sel) =>
            {
                var kind = sel >= 0 && sel < tracks.Length ? tracks[sel].kind : (MetaPanelKind?)null;
                switch (a)
                {
                    case "close": Close(); return;
                    case "challenge": Game2DChallengePanel.Show(_modal); return;
                    case "relic": Game2DRelicPanel.Show(_modal); return;
                    case "inc":
                        if (kind == null) break;
                        if (mpm.TryAssignRank(kind.Value)) Game2DSfx.Play("ui_confirm");
                        else _modal.ShowToast(st.GetRank(kind.Value) >= kind.Value.MaxRank() ? "これ以上は上げられない" : "未配分のポイントが足りない");
                        break;
                    case "dec": if (kind != null && !mpm.TryRefundRank(kind.Value)) _modal.ShowToast("これ以上は下げられない"); break;
                    case "slide":
                    {
                        if (kind == null) break;
                        int goal = Game2DModal.SlideTarget, guard = 0;
                        while (st.GetRank(kind.Value) > goal && guard++ < 16) if (!mpm.TryRefundRank(kind.Value)) break;
                        bool short_ = false;
                        while (st.GetRank(kind.Value) < goal && guard++ < 32) if (!mpm.TryAssignRank(kind.Value)) { short_ = true; break; }
                        if (short_) _modal.ShowToast("未配分の pt が足りない (「pt を買う」で増やす)");
                        else Game2DSfx.Play("ui_click");
                        break;
                    }
                    case "buy":
                        if (mpm.TryPurchasePoint()) Game2DSfx.Play("ui_confirm");
                        else _modal.ShowToast(mpm.IsPointCapReached ? "ポイントは上限" : "トークンが足りない (次 " + cost + ")");
                        break;
                    case "page": _page = 1 - _page; break;
                    case "respec": mpm.RespecAllRanks(); _modal.ShowToast("全部の段を戻した"); break;
                }
                _modal.Replace(Build());
            };
            return d;
        }
    }
}
