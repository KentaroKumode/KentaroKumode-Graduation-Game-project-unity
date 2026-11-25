using System.Globalization;
using MetaProgression;
using UnityEngine;

namespace UI.Game2D
{
    /// <summary>
    /// 整備盤 (docs/GAME.md §15-1・2026-11-23) の窓。 タイトルの「SKILL TREE」から開く。 中身 (盤・棚・詳細・ボタン) は <see cref="MetaBoardView"/> が描く。
    /// ゲームの処理は <see cref="MetaProgressManager"/> の口だけ。
    /// パネルの名前・形・効果の数字は <see cref="MetaBoard"/> (Resources/meta_board_panels.json) から組む。 挑戦 (§23-25) は「挑戦」ボタンの窓。
    /// </summary>
    public static class Game2DMetaPanel
    {
        private static string N(float v) => v.ToString("0.#", CultureInfo.InvariantCulture);


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
            MetaModule.AttackTune   => "攻撃のカードに挿した出目 1 個につき +" + MetaPanel.TerminalTunePerDie,
            MetaModule.BlockTune    => "防御のカードに挿した出目 1 個につき +" + MetaPanel.TerminalTunePerDie,
            MetaModule.AxisTicket   => "店の軸指定の振り直しの追加 −" + MetaPanel.AxisTicketDiscount + "G",
            MetaModule.TacticKnife  => "ランの始めに戦術の消耗品を 1 つ持つ (どれかはランダム)",
            MetaModule.Compression  => "店でカードを山札から抜くのが、 ランで最初の 1 回だけ無料",
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

        private static Game2DModal _modal;
        private static MetaBoardView _view;

        /// <summary>窓を開く。 開けなければ false (モーダルが無い・メタ進行が無い)。</summary>
        public static bool Open()
        {
            if (_modal == null) _modal = Object.FindObjectOfType<Game2DModal>();
            if (_modal == null || MetaProgressManager.Instance == null || MetaProgressManager.Instance.State == null) return false;
            _view = new MetaBoardView { onClose = Close, onChallenge = () => Game2DChallengePanel.Show(_modal) };
            SpriteButton.InputLocked = true;      // 後ろのタイトルのボタンを押さない
            _modal.Open(Build());
            Game2DTutorial.Request(TutorialTopics.MetaPanel);      // 手引き「整備パネル」: 初めて開いた時 (issue #86)
            return true;
        }

        /// <summary>整備盤の窓へ戻す (挑戦の窓の「整備へ」)。</summary>
        internal static void Show()
        {
            if (_view == null) _view = new MetaBoardView { onClose = Close, onChallenge = () => Game2DChallengePanel.Show(_modal) };
            _modal.Replace(Build());
        }

        internal static void Close()
        {
            SpriteButton.InputLocked = false;
            _modal.Close();
        }

        /// <summary>[確かめ用] 今の盤の画面の中身 (スクリーンショットの状態を作る)。</summary>
        internal static MetaBoardView View => _view;

        private static ScreenDef Build()
            => new ScreenDef { id = "meta_board", jp = true, w = MetaBoardView.W, h = MetaBoardView.H, title = "整備盤", canvas = _view,
                 onAction = (a, s) => { } };      // 窓の Esc は使わない (中身が受ける・確かめの間は確かめだけ閉じる)
    }
}
