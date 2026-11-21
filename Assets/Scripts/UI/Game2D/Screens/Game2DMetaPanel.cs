using System.Globalization;
using MetaProgression;
using UnityEngine;

namespace UI.Game2D
{
    /// <summary>
    /// 整備パネル (docs/GAME.md §15-1・2026-11-20 刷新) の窓 (<b>仮置き</b>)。 タイトルの「SKILL TREE」から開く。
    /// 頁: 基礎整備 (6 段・トークンで順に買い切る・「基礎整備を切る」の縛り) / 主方針 / 補助 / 種火 (作戦の枠ごとのモジュールの一覧)。
    /// 枠はトークンで 主方針 → 補助 → 種火 の順に開き、 モジュールはトークンで解禁して、 枠へ無料で挿し替える。
    /// ゲームの処理は <see cref="MetaProgressManager"/> の口だけ。 <b>説明文の数字は <see cref="MetaPanel"/> の定数から組む</b> (手書きの数字は調整のたびに古くなる・#125)。
    /// 挑戦 (§23-25) は「挑戦」ボタンの窓 (<see cref="Game2DChallengePanel"/>)、 遺物は「遺物」ボタンの窓。
    /// </summary>
    public static class Game2DMetaPanel
    {
        private static string N(float v) => v.ToString("0.#", CultureInfo.InvariantCulture);

        private static readonly string[] PageNames = { "基礎整備", "主方針", "補助", "種火" };
        private static readonly MetaSlot[] PageSlot = { MetaSlot.Main, MetaSlot.Main, MetaSlot.Support, MetaSlot.Spark };   // 頁 1〜3 の枠 (頁 0 は使わない)

        /// <summary>基礎整備の n 段目 (1 起点) の効果。</summary>
        private static string TierDesc(int n) => n switch
        {
            1 => "最大 HP +" + MetaPanel.BaseTier1Hp,
            2 => "防御 +" + MetaPanel.BaseTier2Defense,
            3 => "開幕ゴールド +" + MetaPanel.BaseTier3Gold + "・特売の枠 +1",
            4 => "最大 HP +" + MetaPanel.BaseTier4Hp,
            5 => "与ダメ +" + MetaPanel.BaseTier5DamagePct + "%",
            _ => "特売の枠 +1",
        };

        private static string SparkDesc(string axis, string own)
            => axis + "が最初から出現の 4 枠の 1 つ目に入る。 " + own + "。";

        /// <summary>モジュールの名前と説明。 新しいモジュール (種火の 2 軸など) はここにも足す。</summary>
        private static (string name, string desc) Info(MetaModule m) => m switch
        {
            MetaModule.LastStand    => ("ラストスタンド", "戦闘で倒れた時、 1 度だけその場で立ち上がる (ボス戦でも)。 代償に最大 HP が半分になる。"),
            MetaModule.BattleSpirit => ("戦意", "戦闘に勝つたび与ダメ +" + N(MetaPanel.BattleSpiritPctPerWin) + "% (そのラン中)。"),
            MetaModule.Valor        => ("剛胆", "戦闘の点が " + N(MetaPanel.ValorEliteUpgradePct) + "% でエリートになる・エリートの報酬ゴールド +" + N(MetaPanel.ValorEliteGoldPct)
                                                 + "%・エリートへの与ダメ +" + MetaPanel.ValorEliteSlayerPct + "%・勝った戦闘の残りシールドの " + N(MetaPanel.GuardShieldCarryRatio * 100f) + "% を次の戦闘へ。"),
            MetaModule.VaultBank    => ("貸金庫", "層を越えるたびに所持金が " + N(VaultBank.Multiplier) + " 倍 (" + VaultBank.Cap + "G までの分)・宝箱からゴールドも出る。"),
            MetaModule.Robbery      => ("ショップ強盗", "店で値下げ交渉 (強盗) ができる・通常の敵が秘宝を落とす率 +" + N(MetaPanel.RobberyPassiveDropPct) + "%。"),
            MetaModule.Sale99       => ("99% 特売", "特売の枠 +" + MetaPanel.Sale99SlotBonus + "・" + MetaPanel.SaleTiers(true)[MetaPanel.SaleTiers(true).Length - 1] + "% 引きの枠が出る。"),
            MetaModule.StartPick    => ("開幕厳選", "秘宝を " + MetaPanel.StartPickCount + " つ持って始める (1 つ目は " + MetaPanel.StartPickOffers(0) + " 択・2 つ目は " + MetaPanel.StartPickOffers(1) + " 択)。"),
            MetaModule.Immovable    => ("不動", "1 回の攻撃で受けるダメージは最大 HP の " + N(MetaPanel.ArmorHitCapPct * 100f) + "% まで・被ダメージ −" + MetaPanel.ImmovableFlatReduction + " (防御の後に引く)。"),
            MetaModule.SparkCharge  => ("種火・充電", SparkDesc("充電", "充電の上限 +" + MetaPanel.ChargeCapBonus + "・戦闘開始時の充電 +" + MetaPanel.ChargeOpening)),
            MetaModule.SparkRinkai  => ("種火・臨界", SparkDesc("臨界", "戦闘の終わりの臨界の " + N(MetaPanel.RinkaiCarryoverRatio * 100f) + "% を次の戦闘へ")),
            MetaModule.SparkPoison  => ("種火・毒", SparkDesc("毒", "毎ターン最初に与える毒 +" + MetaPanel.PoisonApplyBonus)),
            MetaModule.SparkBleed   => ("種火・出血", SparkDesc("出血", "出血 " + MetaPanel.BleedWeakenThreshold + " 以上の敵の攻撃値 −" + MetaPanel.BleedWeakenAmount)),
            MetaModule.SparkShield  => ("種火・シールド", SparkDesc("シールド", "ターン開始の減衰で残るシールド +" + MetaPanel.ShieldKeepBonusPct + "%")),
            MetaModule.SparkCrit    => ("種火・会心", SparkDesc("会心", "会心倍率 +" + N(MetaPanel.CritMulBonus))),
            MetaModule.SparkDeck    => ("種火・山札", SparkDesc("山札", "戦闘の 1 ターン目の手札 +" + MetaPanel.DeckOpeningDraw + " 枚")),
            MetaModule.SparkRole    => ("種火・役", SparkDesc("役", "各戦闘の最初の振り直しが無料")),
            MetaModule.AttackTune   => ("攻撃端子調律", "攻撃の端子に挿した出目 1 個につき +" + MetaPanel.TerminalTunePerDie + "。"),
            MetaModule.BlockTune    => ("防御端子調律", "防御の端子に挿した出目 1 個につき +" + MetaPanel.TerminalTunePerDie + "。"),
            MetaModule.AxisTicket   => ("軸引きの手形", "店の軸指定の振り直しの追加 −" + MetaPanel.AxisTicketDiscount + "G"),
            MetaModule.TacticKnife  => ("戦術の懐刀", "ランの始めに戦術の消耗品を 1 つ持つ (どれかはランダム)。"),
            MetaModule.Compression  => ("圧縮の心得", "店で端子を山札から抜くのが、 ランで最初の 1 回だけ無料。"),
            _ => (m.ToString(), ""),
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

        /// <summary>その頁 (枠) のモジュール (enum の並び)。</summary>
        private static System.Collections.Generic.List<MetaModule> ModulesOf(MetaSlot slot)
        {
            var l = new System.Collections.Generic.List<MetaModule>();
            foreach (MetaModule m in System.Enum.GetValues(typeof(MetaModule))) if (MetaPanel.SlotOf(m) == slot) l.Add(m);
            return l;
        }

        private static ScreenDef Build()
        {
            var mpm = MetaProgressManager.Instance; var st = mpm.State;
            int slotCost = mpm.NextSlotCost;
            var d = new ScreenDef { id = "meta_panel", jp = true, w = 472, cols = 1, rowH = 15, title = "整備パネル ― " + PageNames[_page] };
            var mods = _page == 0 ? null : ModulesOf(PageSlot[_page]);
            if (_page == 0)
            {
                int cost = mpm.NextBaseTierCost;
                d.sub = "トークン " + st.tokens + "  基礎整備 " + st.baseTier + "/" + MetaPanel.BaseTierCount + (st.baseDisabled ? "  (切っている)" : "");
                for (int n = 1; n <= MetaPanel.BaseTierCount; n++)
                {
                    bool got = st.baseTier >= n, next = st.baseTier == n - 1;
                    d.Items.Add(new ModalItem
                    {
                        name = n + " 段目", disabled = !got || st.baseDisabled,
                        value = got ? (st.baseDisabled ? "切" : "済") : next ? MetaPanel.BaseTierCost[n - 1] + "" : "―",
                        desc = TierDesc(n) + "。" + (got ? "" : " トークン " + MetaPanel.BaseTierCost[n - 1] + " で買う (段は順に買う)。"),
                    });
                }
                d.buttons.Add(new ModalBtn { label = cost == int.MaxValue ? "全部買った" : "段を買う " + cost, action = "buyTier", primary = true });
                d.buttons.Add(new ModalBtn { label = st.baseDisabled ? "基礎整備を戻す" : "基礎整備を切る", action = "toggleBase" });
                d.footer = "段は買い切り。 「切る」と買った段の効果が止まる (縛り・いつでも戻せる)";
            }
            else
            {
                var slot = PageSlot[_page];
                bool open = st.IsSlotOpen(slot);
                var eq = st.Equipped(slot);
                d.sub = "トークン " + st.tokens + "  枠 " + st.slotsOpen + "/" + MetaPanel.SlotOpenCost.Length + "  "
                      + (open ? "挿している: " + (eq.HasValue ? Info(eq.Value).name : "なし") : "この枠は閉じている");
                foreach (var m in mods)
                {
                    var (name, desc) = Info(m);
                    bool unlocked = st.IsUnlocked(m), on = eq == m;
                    d.Items.Add(new ModalItem
                    {
                        name = name, disabled = !unlocked,
                        value = on ? "使用中" : unlocked ? "解禁済" : MetaPanel.ModuleCost(m) + "",
                        desc = desc + (unlocked ? "" : " 解禁はトークン " + MetaPanel.ModuleCost(m) + "。"),
                    });
                }
                d.buttons.Add(new ModalBtn { label = "解禁・挿す", action = "use", primary = true });
                d.buttons.Add(new ModalBtn { label = slotCost == int.MaxValue ? "枠は全部開いた" : "枠を開く " + slotCost, action = "openSlot" });
                d.footer = "解禁は買い切り・挿し替えは無料 (もう一度押すと外す)。 枠は 主方針 → 補助 → 種火 の順に開く";
            }
            d.buttons.Add(new ModalBtn { label = "次へ (" + PageNames[(_page + 1) % PageNames.Length] + ")", action = "page" });
            d.buttons.Add(new ModalBtn { label = "挑戦", action = "challenge" });
            d.buttons.Add(new ModalBtn { label = "遺物", action = "relic" });
            d.buttons.Add(new ModalBtn { label = "閉じる", action = "close" });
            d.onAction = (a, sel) =>
            {
                switch (a)
                {
                    case "close": Close(); return;
                    case "challenge": Game2DChallengePanel.Show(_modal); return;
                    case "relic": Game2DRelicPanel.Show(_modal); return;
                    case "page": _page = (_page + 1) % PageNames.Length; break;
                    case "buyTier":
                        if (mpm.TryBuyBaseTier()) Game2DSfx.Play("ui_confirm");
                        else _modal.ShowError(st.baseTier >= MetaPanel.BaseTierCount ? "全部の段を買った" : "トークンが足りない (次 " + mpm.NextBaseTierCost + ")");
                        break;
                    case "toggleBase":
                        mpm.ToggleBaseDisabled();
                        _modal.ShowToast(st.baseDisabled ? "基礎整備を切った" : "基礎整備を戻した");
                        break;
                    case "openSlot":
                        if (mpm.TryOpenSlot()) Game2DSfx.Play("ui_confirm");
                        else _modal.ShowError(st.slotsOpen >= MetaPanel.SlotOpenCost.Length ? "枠は全部開いている" : "トークンが足りない (次 " + mpm.NextSlotCost + ")");
                        break;
                    case "use":
                    {
                        if (mods == null || sel < 0 || sel >= mods.Count) break;
                        var m = mods[sel];
                        if (!st.IsUnlocked(m))
                        {
                            if (mpm.TryUnlockModule(m)) Game2DSfx.Play("ui_confirm");
                            else _modal.ShowError("トークンが足りない (解禁 " + MetaPanel.ModuleCost(m) + ")");
                        }
                        else if (mpm.TryToggleEquip(m)) Game2DSfx.Play("ui_click");
                        else _modal.ShowError("この枠はまだ閉じている (「枠を開く」で開く)");
                        break;
                    }
                }
                _modal.Replace(Build());
            };
            return d;
        }
    }
}
