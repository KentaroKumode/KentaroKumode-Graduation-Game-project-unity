using System.Globalization;
using MetaProgression;
using UnityEngine;

namespace UI.Game2D
{
    /// <summary>
    /// 整備盤 (docs/GAME.md §15-1・2026-11-23) の窓 (<b>仮置き・文字だけ</b>)。 タイトルの「SKILL TREE」から開く。
    /// 頁: マス (9 マスを中心 → 辺 → 角の順にトークンで開く) / パネル (買う・置く・外す)。 置き方は仮に「置ける所の最初」を使う
    /// (回転と位置を選ぶ盤の画面は別の仕事)。 ゲームの処理は <see cref="MetaProgressManager"/> の口だけ。
    /// パネルの名前・形・効果の数字は <see cref="MetaBoard"/> (Resources/meta_board_panels.json) から組む。 挑戦 (§23-25) は「挑戦」ボタンの窓。
    /// </summary>
    public static class Game2DMetaPanel
    {
        private static string N(float v) => v.ToString("0.#", CultureInfo.InvariantCulture);

        private static readonly string[] PageNames = { "マス", "パネル" };
        private static readonly string[] CellNames = { "左上", "上", "右上", "左", "中心", "右", "左下", "下", "右下" };

        /// <summary>モジュールの説明 (主方針・補助・種火の効果)。</summary>
        private static string ModuleDesc(MetaModule m) => m switch
        {
            MetaModule.LastStand    => "戦闘で倒れた時、 1 度だけその場で立ち上がる (ボス戦でも)。 代償に最大 HP が半分になる",
            MetaModule.BattleSpirit => "戦闘に勝つたび与ダメ +" + N(MetaPanel.BattleSpiritPctPerWin) + "% (そのラン中)",
            MetaModule.Valor        => "戦闘の点が " + N(MetaPanel.ValorEliteUpgradePct) + "% でエリートになる・エリートの報酬ゴールド +" + N(MetaPanel.ValorEliteGoldPct)
                                       + "%・エリートへの与ダメ +" + MetaPanel.ValorEliteSlayerPct + "%・残りシールドの " + N(MetaPanel.GuardShieldCarryRatio * 100f) + "% を次の戦闘へ",
            MetaModule.VaultBank    => "層を越えるたびに所持金が " + N(VaultBank.Multiplier) + " 倍 (" + VaultBank.Cap + "G までの分)・宝箱からゴールドも出る",
            MetaModule.Robbery      => "店で値下げ交渉 (強盗) ができる・通常の敵が秘宝を落とす率 +" + N(MetaPanel.RobberyPassiveDropPct) + "%",
            MetaModule.Sale99       => "特売の枠 +" + MetaPanel.Sale99SlotBonus + "・99% 引きの枠が出る",
            MetaModule.StartPick    => "秘宝を " + MetaPanel.StartPickCount + " つ持って始める (1 つ目は " + MetaPanel.StartPickOffers(0) + " 択・2 つ目は " + MetaPanel.StartPickOffers(1) + " 択)",
            MetaModule.Immovable    => "1 回の攻撃で受けるダメージは最大 HP の " + N(MetaPanel.ArmorHitCapPct * 100f) + "% まで・被ダメージ −" + MetaPanel.ImmovableFlatReduction,
            MetaModule.SparkCharge  => "充電が最初から出現の 4 枠の 1 つ目・充電の上限 +" + MetaPanel.ChargeCapBonus + "・戦闘開始時の充電 +" + MetaPanel.ChargeOpening,
            MetaModule.SparkRinkai  => "臨界が最初から出現の 4 枠の 1 つ目・戦闘の終わりの臨界の " + N(MetaPanel.RinkaiCarryoverRatio * 100f) + "% を次の戦闘へ",
            MetaModule.SparkPoison  => "毒が最初から出現の 4 枠の 1 つ目・毎ターン最初に与える毒 +" + MetaPanel.PoisonApplyBonus,
            MetaModule.SparkBleed   => "出血が最初から出現の 4 枠の 1 つ目・出血 " + MetaPanel.BleedWeakenThreshold + " 以上の敵の攻撃値 −" + MetaPanel.BleedWeakenAmount,
            MetaModule.SparkShield  => "シールドが最初から出現の 4 枠の 1 つ目・ターン開始の減衰で残るシールド +" + MetaPanel.ShieldKeepBonusPct + "%",
            MetaModule.SparkCrit    => "会心が最初から出現の 4 枠の 1 つ目・会心倍率 +" + N(MetaPanel.CritMulBonus),
            MetaModule.SparkDeck    => "山札が最初から出現の 4 枠の 1 つ目・戦闘の 1 ターン目の手札 +" + MetaPanel.DeckOpeningDraw + " 枚",
            MetaModule.SparkRole    => "役が最初から出現の 4 枠の 1 つ目・各戦闘の最初の振り直しが無料",
            MetaModule.AttackTune   => "攻撃の端子に挿した出目 1 個につき +" + MetaPanel.TerminalTunePerDie,
            MetaModule.BlockTune    => "防御の端子に挿した出目 1 個につき +" + MetaPanel.TerminalTunePerDie,
            MetaModule.AxisTicket   => "店の軸指定の振り直しの追加 −" + MetaPanel.AxisTicketDiscount + "G",
            MetaModule.TacticKnife  => "ランの始めに戦術の消耗品を 1 つ持つ (どれかはランダム)",
            MetaModule.Compression  => "店で端子を山札から抜くのが、 ランで最初の 1 回だけ無料",
            _ => m.ToString(),
        };

        /// <summary>パネル 1 枚の効果の説明 (モジュール + 数値)。</summary>
        internal static string Describe(BoardPanelDef d)
        {
            var parts = new System.Collections.Generic.List<string>();
            var m = d.Module; if (m.HasValue) parts.Add(ModuleDesc(m.Value));
            var s = d.stats;
            if (s.hp > 0) parts.Add("最大 HP +" + s.hp);
            if (s.defense > 0) parts.Add("防御 +" + s.defense);
            if (s.damagePct > 0) parts.Add("与ダメ +" + s.damagePct + "%");
            if (s.saleSlots > 0) parts.Add("店の特売枠 +" + s.saleSlots);
            if (s.gold > 0) parts.Add("開幕ゴールド +" + s.gold + "G");
            if (s.chargeOpening > 0) parts.Add("各戦闘の開始時 充電 +" + s.chargeOpening);
            if (s.blockTurn1 > 0) parts.Add("各戦闘の 1 ターン目 ブロック +" + s.blockTurn1);
            if (s.critPct > 0) parts.Add("会心率 +" + s.critPct + "%");
            if (s.skipCashPct > 0) parts.Add("秘宝の見送り換金 +" + s.skipCashPct + "%");
            if (s.handTurn1 > 0) parts.Add("各戦闘の 1 ターン目 手札 +" + s.handTurn1 + " 枚");
            if (s.freeRerolls > 0) parts.Add("各戦闘の最初の振り直し " + s.freeRerolls + " 回無料");
            if (s.farmOffer > 0) parts.Add("0 層〈支給の秘宝〉の提示 +" + s.farmOffer);
            if (s.restHealPct > 0) parts.Add("休憩の回復量 +" + s.restHealPct + "%");
            if (s.outpostSupplyPct > 0) parts.Add("前哨基地の補給 +" + s.outpostSupplyPct + "%");
            if (s.vision > 0) parts.Add("地図の視界 +" + s.vision + " 歩");
            if (s.materialCutPct > 0) parts.Add("武器の強化素材の値段 −" + s.materialCutPct + "%");
            if (s.openerFights > 0) parts.Add("各層の最初の通常戦 " + s.openerFights + " 回 敵の攻撃 −" + s.openerAtkCutPct + "%");
            if (s.lockHp > 0) parts.Add("出現の枠が 4 つ埋まった時 最大 HP +" + s.lockHp + "・全回復");
            return string.Join("・", parts);
        }

        private static string ShapeName(string shape) => shape switch
        {
            "1" => "1 マス", "I2" => "2 マス (I)", "I3" => "3 マス (I)", "L3" => "3 マス (L)",
            "T" => "4 マス (T)", "O" => "4 マス (2×2)", "S" => "4 マス (S)", "Z" => "4 マス (Z)", _ => shape,
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

        /// <summary>整備盤の頁へ戻す (挑戦の窓の「整備へ」)。</summary>
        internal static void Show() => _modal.Replace(Build());

        internal static void Close()
        {
            SpriteButton.InputLocked = false;
            _modal.Close();
        }

        /// <summary>仮の置き方: そのパネルが今の盤で置ける最初の所 (開いたマスだけ・ほかのパネルと重ならない)。 置けなければ null。</summary>
        private static BoardPlacement FirstFit(MetaProgressState st, string id)
        {
            int used = 0;
            foreach (var q in st.ValidPlacements) if (q.id != id) used |= MetaBoard.MaskOf(MetaBoard.CellsOf(q));
            foreach (var p in MetaBoard.AllPlacements(id))
            {
                int m = MetaBoard.MaskOf(MetaBoard.CellsOf(p));
                if ((m & ~st.boardOpenMask) == 0 && (m & used) == 0) return p;
            }
            return null;
        }

        private static ScreenDef Build()
        {
            var mpm = MetaProgressManager.Instance; var st = mpm.State;
            var d = new ScreenDef { id = "meta_panel", jp = true, w = 472, cols = 1, rowH = 15, title = "整備盤 ― " + PageNames[_page] };
            d.sub = "トークン " + st.tokens + "  " + st.DescribeBoard();
            var panels = MetaBoard.All;
            if (_page == 0)
            {
                for (int c = 0; c < MetaBoard.Cells; c++)
                {
                    bool open = (st.boardOpenMask & 1 << c) != 0;
                    int cost = MetaBoard.OpenCost(st.boardOpenMask, c);
                    string on = "";
                    foreach (var q in st.ValidPlacements) if (System.Array.IndexOf(MetaBoard.CellsOf(q), c) >= 0) on = MetaBoard.Get(q.id)?.name ?? q.id;
                    d.Items.Add(new ModalItem
                    {
                        name = CellNames[c], disabled = !open && cost < 0,
                        value = open ? (on.Length > 0 ? on : "開") : cost >= 0 ? cost + "" : "―",
                        desc = open ? (on.Length > 0 ? "置いている: " + on : "空き") : cost >= 0 ? "トークン " + cost + " で開く" : "まだ開けない (中心 → 辺 → 角の順)",
                    });
                }
                d.buttons.Add(new ModalBtn { label = "マスを開く", action = "open", primary = true });
                d.footer = "中心 → 上下左右 → 四隅の順に開く。 開けた個数で値段が上がる";
            }
            else
            {
                foreach (var p in panels)
                {
                    bool own = st.OwnsPanel(p.id), placed = st.ValidPlacements.Exists(q => q.id == p.id);
                    d.Items.Add(new ModalItem
                    {
                        name = p.name + " [" + ShapeName(p.shape) + "]", disabled = !own && p.FromAchievement,
                        value = placed ? "置いた" : own ? "持" : p.FromAchievement ? "実績" : p.cost + "",
                        desc = Describe(p) + "。" + (own ? "" : p.FromAchievement ? " 実績〈" + (MetaProgression.Achievements.AchievementCatalog.Get(p.achievement)?.displayName ?? p.achievement) + "〉で手に入る。" : " トークン " + p.cost + " で買う。"),
                    });
                }
                d.buttons.Add(new ModalBtn { label = "買う・置く・外す", action = "use", primary = true });
                d.footer = "パネルは買い切り・置き直しは無料 (もう一度押すと外す)。 回転と位置を選ぶ盤の画面は未実装 (仮に置ける所の最初へ置く)";
            }
            d.buttons.Add(new ModalBtn { label = "次へ (" + PageNames[(_page + 1) % PageNames.Length] + ")", action = "page" });
            d.buttons.Add(new ModalBtn { label = "挑戦", action = "challenge" });
            d.buttons.Add(new ModalBtn { label = "閉じる", action = "close" });
            d.onAction = (a, sel) =>
            {
                switch (a)
                {
                    case "close": Close(); return;
                    case "challenge": Game2DChallengePanel.Show(_modal); return;
                    case "page": _page = (_page + 1) % PageNames.Length; break;
                    case "open":
                        if (sel < 0 || sel >= MetaBoard.Cells) break;
                        if (mpm.TryOpenCell(sel)) Game2DSfx.Play("ui_confirm");
                        else _modal.ShowError(MetaBoard.OpenCost(st.boardOpenMask, sel) < 0 ? "このマスはまだ開けない" : "トークンが足りない");
                        break;
                    case "use":
                    {
                        if (sel < 0 || sel >= panels.Count) break;
                        var p = panels[sel];
                        if (!st.OwnsPanel(p.id))
                        {
                            if (p.FromAchievement) _modal.ShowError("実績で手に入るパネル");
                            else if (mpm.TryBuyPanel(p.id)) Game2DSfx.Play("ui_confirm");
                            else _modal.ShowError("トークンが足りない (" + p.cost + ")");
                        }
                        else if (st.ValidPlacements.Exists(q => q.id == p.id)) { mpm.RemovePanel(p.id); Game2DSfx.Play("ui_click"); }
                        else
                        {
                            var fit = FirstFit(st, p.id);
                            if (fit != null && mpm.TryPlacePanel(fit)) Game2DSfx.Play("ui_click");
                            else _modal.ShowError("置ける所が無い (マスを開くか、 ほかのパネルを外す)");
                        }
                        break;
                    }
                }
                _modal.Replace(Build());
            };
            return d;
        }
    }
}
