using System.Collections.Generic;
using MetaProgression;
using UnityEngine;

namespace UI.Game2D
{
    /// <summary>
    /// 整備パネル (docs/GAME.md §15-1) の窓 (<b>仮置き</b>・2026-11-04)。 タイトルの「SKILL TREE」から開く。
    /// トークンでポイントを買い、 トラックへ 1 段ずつ配る (拠点では無料で戻せる・全部戻せる)。 ゲームの処理は <see cref="MetaProgressManager"/> の既存の口だけ。
    /// 頁: 数値系 8 本 (max 10・1 段 1pt) / 宣言系 6 本 (種火 4 + 端子 2・max 3・1 段 3pt)。 挑戦 (§15-2) は「挑戦」ボタンの窓 (<see cref="Game2DChallengePanel"/>) で選ぶ。
    /// <para>旧スキルツリーの BUFF 側は 2026-07-25 に廃止した 58 段の一直線トラックのままで、 今の配点はどこからもできなかった。</para>
    /// </summary>
    public static class Game2DMetaPanel
    {
        private struct Track { public MetaPanelKind kind; public string name, desc; }

        private static readonly Track[] Numeric =
        {
            new Track { kind = MetaPanelKind.Shell,   name = "外殻", desc = "最大 HP +4/段 (r10 は +8)。 r10: ラストスタンド (その場で蘇生・最大 HP 半減の代償つき)。" },
            new Track { kind = MetaPanelKind.Output,  name = "出力", desc = "与ダメ +7%/段。 r10〈戦意〉: 戦闘に勝つたび与ダメ +1.5% (そのラン中)。" },
            new Track { kind = MetaPanelKind.Guard,   name = "剛胆", desc = "エリートが増える +5%/段・エリートの報酬ゴールド +10%/段。 r10: エリートへの与ダメ +10%・残りシールドの 30% を次戦へ。" },
            new Track { kind = MetaPanelKind.Vault,   name = "金庫", desc = "開幕ゴールド +5/段。 r10: 宝箱のゴールドが復活・ゴールドをランを跨いで預ける (上限 150G)。" },
            new Track { kind = MetaPanelKind.Plunder, name = "強奪", desc = "ボス撃破の追加ゴールド +1/段・通常の敵のパッシブドロップ +2%/段。 r10: ショップ強盗を解禁。" },
            new Track { kind = MetaPanelKind.Trade,   name = "商才", desc = "ショップの特売枠 +1/段 (15/30/50% 引き)。 r10: 99% 引きの枠が出る。" },
            new Track { kind = MetaPanelKind.Supply,  name = "宝物庫", desc = "r3/6/9 で開幕パッシブ +1 本・段が上がるほどレア度が上寄り。 r10: 開幕パッシブが選択式。" },
            new Track { kind = MetaPanelKind.Armor,   name = "装甲", desc = "防御 +2/段・被ダメージ −1/段。 r10〈不動〉: 1 回の攻撃で受けるダメージは最大 HP の 35% まで。 防御は割合の軽減 (積むほど伸びは鈍る)、 被ダメージ −N は防御の後に引く。" },
        };

        private static readonly Track[] Declare =
        {
            new Track { kind = MetaPanelKind.SparkCharge, name = "種火・充電", desc = "r1: 充電の品が出やすい (+60%)。 r2: 充電の上限 +10・戦闘開始時の充電 +10。 r3: 充電の品 (BRONZE) を 1 つ開幕で持つ。" },
            new Track { kind = MetaPanelKind.SparkRinkai, name = "種火・臨界", desc = "r1: 臨界の品が出やすい (+60%)。 r2: 戦闘の終わりの臨界の 50% を次の戦闘へ。 r3: 臨界の品を 1 つ開幕で持つ。" },
            new Track { kind = MetaPanelKind.SparkPoison, name = "種火・毒",   desc = "r1: 毒の品が出やすい (+60%)。 r2: 毎ターン最初に与える毒 +2。 r3: 毒の品を 1 つ開幕で持つ。" },
            new Track { kind = MetaPanelKind.SparkBleed,  name = "種火・出血", desc = "r1: 出血の品が出やすい (+60%)。 r2: 出血 3 以上の敵の攻撃値 −2。 r3: 出血の品を 1 つ開幕で持つ。" },
            new Track { kind = MetaPanelKind.SparkShield, name = "種火・シールド", desc = "r1: シールドの品が出やすい (+60%)。 r2: ターン開始の減衰で残るシールド +10%。 r3: シールドの品を 1 つ開幕で持つ。" },
            new Track { kind = MetaPanelKind.AttackTerm,  name = "攻撃端子",   desc = "攻撃端子へつないだダイス 1 本ごとに、 攻撃端子の配線合計 +1/段。" },
            new Track { kind = MetaPanelKind.BlockTerm,   name = "防御端子",   desc = "防御端子へつないだダイス 1 本ごとに、 防御端子の配線合計 +1/段。" },
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
                case MetaPanelKind.Supply:  return "開幕パッシブ " + MetaPanel.SupplyStartingPassives(r) + " 本" + (r >= 10 ? "・選べる" : "");
                case MetaPanelKind.Armor:   return "防御 +" + MetaPanel.ArmorDefense(r) + "・被ダメ −" + MetaPanel.ArmorFlatReduction(r) + (r >= 10 ? "・不動" : "");
                case MetaPanelKind.AttackTerm: return "つないだ 1 本ごと +" + MetaPanel.AttackTerminalPerDice(r);
                case MetaPanelKind.BlockTerm:  return "つないだ 1 本ごと +" + MetaPanel.BlockTerminalPerDice(r);
            }
            // 種火 6 本: r1 出やすい / r2 固有 / r3 開幕に 1 つ
            string r2 = k == MetaPanelKind.SparkCharge ? "上限 +" + MetaPanel.ChargeCapBonus(r)
                      : k == MetaPanelKind.SparkRinkai ? "持ち越し " + (MetaPanel.RinkaiCarryoverRatio(r) * 100f).ToString("0") + "%"
                      : k == MetaPanelKind.SparkPoison ? "毎ターン最初の付与 +" + MetaPanel.PoisonApplyBonus(r)
                      : k == MetaPanelKind.SparkShield ? "残るシールド +" + MetaPanel.ShieldKeepBonusPct(r) + "%"
                      : "攻撃 -2";
            return "出やすい" + (r >= 2 ? "・" + r2 : "") + (MetaPanel.SparkStartingItemUnlocked(r) ? $"・開幕に {MetaPanel.SparkStartingItemCount} つ" : "");
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
            string per = _page == 1 ? "1 段 3pt。 " : "1 段 1pt。 ";
            d.footer = per + "バーを押す・引いて段を決める (戻すのは無料)" + (mpm.IsPointCapReached ? "" : "。 pt はトークン " + cost + " で 1 つ");
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
