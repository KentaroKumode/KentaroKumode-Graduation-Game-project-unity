using System.Collections.Generic;
using UnityEngine;

namespace UI.Game2D
{
    /// <summary>
    /// 仮置きの画面の見本の中身 (2026-10-24)。 値・名前はどれも見本で、 ゲームの状態にはつないでいない。 文字は 3×5 のドット字なので英数字だけ。
    /// 画面の一覧 (docs/design/gui-needed.md の「未実装」): ショップ(#34・#35)・イベント・報酬・休憩・秘宝・補給拠点・〈門〉・整備パネル(#39)・エンディングの選択(#40)・
    /// 設定・セーブ(#41)・所持品(#31・#32)・敵の詳細・脱出の確認(#19)。
    /// </summary>
    public static class Game2DScreens
    {
        public static readonly string[] Ids = { "shop", "event", "reward", "rest", "treasure", "depot", "gate", "meta", "ending", "settings", "inventory", "enemy", "exit" };

        private static ModalItem I(string icon, string name, string desc, string value = null, string tag = null, Color32? tone = null, bool dis = false)
            => new ModalItem { icon = icon, name = name, desc = desc, value = value, tag = tag, tone = tone, disabled = dis };
        private static ModalItem N(string name, string desc, int count, int max) => new ModalItem { name = name, desc = desc, count = count, max = max };
        private static ModalBtn B(string label, string action = "x", bool primary = false) => new ModalBtn { label = label, action = action, primary = primary };

        public static ScreenDef Make(string id)
        {
            var d = new ScreenDef { id = id };
            switch (id)
            {
                case "shop":
                    d.title = "SHOP"; d.sub = "GOLD 320   SUPPLY 420"; d.icon = "shop"; d.tabs = new[] { "SUPPLY", "RELIC", "SMITH" };
                    d.note = "ROBBERY: SHOPS NEARBY COST X2";
                    d.tabItems.Add(new List<ModalItem> {
                        I("supply", "SUPPLY PACK", "100 SUPPLY PER PACK. 3 PACKS PER SHOP.", "10 G", "STOCK 3"),
                        I("decoy", "DECOY TRAP", "THE CHASER FOLLOWS THE DECOY INSTEAD OF YOU.", "8 G", "INFINITE"),
                        I("flare", "FLARE", "LIGHTS THE AREA FOR 3 STEPS. COSTS SUPPLY TO USE.", "12 G", "STOCK 2"),
                        I("unknown", "MAP FRAGMENT", "REVEALS THE EXIT DIRECTION.", "25 G", "STOCK 1", null, true) });
                    d.tabItems.Add(new List<ModalItem> {
                        I("relic", "CROWN SHARD", "RELIC. ADDS +1 TO EVERY ATTACK DIE.", "120 G", "RARE", Game2DKit.Gold),
                        I("relic", "HOLLOW LENS", "RELIC. SEE THE ENEMY ATTACK BEFORE WIRING.", "80 G", "COMMON"),
                        I("relic", "IRON SPINE", "RELIC. BLOCK DICE COUNT TWICE.", "95 G", "COMMON") });
                    d.tabItems.Add(new List<ModalItem> {
                        I("anvil", "REFORGE", "REROLL ONE FACE OF A DIE.", "60 G"),
                        I("anvil", "SHARPEN", "UPGRADE A WEAPON ONE TIER.", "150 G", "T2 > T3") });
                    d.buttons.Add(B("BUY", "buy", true)); d.buttons.Add(B("LEAVE", "close")); d.footer = "SELECT AN ITEM AND PRESS BUY";
                    break;
                case "event":
                    d.title = "EVENT"; d.sub = "A WANDERING TRADER BLOCKS THE PATH"; d.icon = "event";
                    d.tabItems.Add(new List<ModalItem> {
                        I(null, "A  TRADE", "PAY 20 G FOR A RANDOM RELIC.", "-20 G"),
                        I(null, "B  FIGHT", "TAKE HIS GOODS BY FORCE. ENTERS COMBAT.", null, "RISK", Game2DKit.Red),
                        I(null, "C  LEAVE", "WALK AWAY. NOTHING HAPPENS.") });
                    d.buttons.Add(B("CONFIRM", "ok", true)); d.footer = "CHOOSE ONE";
                    break;
                case "reward":
                    d.title = "VICTORY"; d.sub = "CHOOSE ONE REWARD"; d.icon = "elite";
                    d.tabItems.Add(new List<ModalItem> {
                        I("coin", "GOLD", "A POUCH OF COINS FROM THE FALLEN.", "+34 G", null, Game2DKit.Gold),
                        I("relic", "RELIC: COLD FLAME", "RELIC. CRIT RATE +10 PERCENT.", null, "NEW", Game2DKit.Amber),
                        I("supply", "SUPPLY +150", "REFILL SUPPLY.", "+150"),
                        I("heart", "HEAL 20 PERCENT", "RECOVER HP.", "+15 HP") });
                    d.buttons.Add(B("TAKE", "take", true)); d.buttons.Add(B("SKIP", "close"));
                    break;
                case "rest":
                    d.title = "REST"; d.sub = "THE FIRE IS WARM"; d.icon = "rest"; 
                    d.tabItems.Add(new List<ModalItem> {
                        I(null, "REST", "RECOVER 30 PERCENT OF MAX HP.", "+23 HP"),
                        I(null, "MEDITATE", "RECHARGE. GAIN 2 CHARGE AT THE NEXT FIGHT.", null, "+2 CHG"),
                        I(null, "REPAIR", "MEND THE WEAPON. CURE ONE STATUS.") });
                    d.buttons.Add(B("DO IT", "do", true)); d.buttons.Add(B("LEAVE", "close"));
                    break;
                case "treasure":
                    d.title = "TREASURE"; d.sub = "A CHEST. TAKE ONE"; d.icon = "treasure";
                    d.tabItems.Add(new List<ModalItem> {
                        I("relic", "GOLDEN DIE", "RELIC. ONE DIE FACE IS ALWAYS 6.", null, "RARE", Game2DKit.Gold),
                        I("relic", "QUIET BELL", "RELIC. START EVERY FIGHT WITH 1 CHARGE."),
                        I("relic", "FROST COIN", "RELIC. ENEMIES TICK ONE STAGE SLOWER.") });
                    d.buttons.Add(B("TAKE", "take", true)); d.buttons.Add(B("LEAVE", "close"));
                    break;
                case "depot":
                    d.title = "SUPPLY DEPOT"; d.sub = "FLARE / TRAP / SUPPLY"; d.icon = "depot"; 
                    d.tabItems.Add(new List<ModalItem> {
                        I("flare", "FLARE +1", "LIGHT THE AREA. COSTS SUPPLY.", "30 SUP", "OWN 1"),
                        I("decoy", "TRAP +1", "SET A TRAP OR A DECOY.", "20 SUP", "OWN 2"),
                        I("supply", "SUPPLY +100", "REFILL YOUR SUPPLY.", "FREE", "ONCE") });
                    d.buttons.Add(B("TAKE", "take", true)); d.buttons.Add(B("LEAVE", "close"));
                    break;
                case "gate":
                    d.title = "THE GATE"; d.sub = "PAY A COST TO OPEN IT"; d.icon = "gate"; 
                    d.tabItems.Add(new List<ModalItem> {
                        I(null, "BLOOD", "LOSE 30 PERCENT OF MAX HP.", "-23 HP", null, Game2DKit.Red),
                        I(null, "GOLD", "LOSE ALL GOLD ABOVE 100.", "-220 G", null, Game2DKit.Gold),
                        I(null, "SUPPLY", "LOSE 300 SUPPLY.", "-300 SUP") });
                    d.buttons.Add(B("OPEN", "open", true)); d.buttons.Add(B("TURN BACK", "close")); d.footer = "BEYOND THE GATE: THE FINAL BOSS";
                    break;
                case "meta":
                    d.title = "TUNING PANEL"; d.sub = "SPEND POINTS BEFORE THE RUN"; d.icon = "unknown"; d.w = 380; d.cols = 2; d.budget = 24;
                    d.tabItems.Add(new List<ModalItem> {
                        N("MAX HP", "RAISE STARTING HP.", 0, 6), N("ATTACK", "RAISE BASE ATTACK.", 0, 6), N("BLOCK", "EXTRA BLOCK PER DIE.", 0, 6), N("CHARGE", "START WITH CHARGE.", 0, 4),
                        N("SUPPLY", "RAISE SUPPLY CAP.", 0, 6), N("LUCK", "BETTER REWARD ODDS.", 0, 4), N("TERMINAL TUNE", "EXTRA PER WIRED DIE.", 0, 3), N("DECOY", "START WITH A DECOY.", 0, 1) });
                    d.buttons.Add(B("+", "inc", true)); d.buttons.Add(B("-", "dec")); d.buttons.Add(B("START", "start", true)); d.buttons.Add(B("CLOSE", "close"));
                    break;
                case "ending":
                    d.title = "THE BOSS FALLS"; d.sub = "THE RUN IS CLEARED"; d.icon = "elite"; 
                    d.tabItems.Add(new List<ModalItem> {
                        I(null, "CLOSE THE BOOK", "END THE TALE HERE. THE BOSS ROUTE ENDING PLAYS.", null, "END A", Game2DKit.Amber),
                        I(null, "LEAVE IT OPEN", "WALK AWAY FROM THE HOLE. ANOTHER ENDING.", null, "END B") });
                    d.buttons.Add(B("DECIDE", "decide", true)); d.footer = "THE ENDING DEPENDS ON THE BOSS YOU DEFEATED";
                    break;
                case "settings":
                    d.title = "SETTINGS"; d.sub = "SAVE / LOAD"; d.icon = "unknown"; 
                    d.tabItems.Add(new List<ModalItem> {
                        N("MASTER VOLUME", "", 8, 10), N("BGM", "", 6, 10), N("SE", "", 8, 10), N("SCREEN FILTER", "0 OFF 1 LCD 2 STRONG", 1, 2) });
                    d.buttons.Add(B("+", "inc")); d.buttons.Add(B("-", "dec")); d.buttons.Add(B("SAVE", "save", true)); d.buttons.Add(B("LOAD", "load")); d.buttons.Add(B("CLOSE", "close"));
                    break;
                case "inventory":
                    d.title = "ITEMS"; d.sub = "WEAPONS / PASSIVES / CONSUMABLES"; d.icon = "treasure"; d.w = 360; d.cols = 2; d.tabs = new[] { "WEAPONS", "PASSIVES", "SUPPLIES" };
                    d.tabItems.Add(new List<ModalItem> { I("swordsman", "SWORD T2", "A PLAIN IRON SWORD. BASE ATK 3.", "ATK 3", "EQUIPPED", Game2DKit.Amber), I("berserker", "AXE T2", "BASE ATK 4.", "ATK 4"), I("knight", "SHIELD T2", "BASE ATK 2.", "ATK 2"), I("assassin", "DAGGER T2", "BASE ATK 2.", "ATK 2") });
                    d.tabItems.Add(new List<ModalItem> { I("relic", "COLD FLAME", "CRIT RATE UP.", null, "RELIC"), I("relic", "HOLLOW LENS", "SEE THE ATTACK FIRST."), I("relic", "IRON SPINE", "BLOCK TWICE."), I("relic", "QUIET BELL", "START WITH CHARGE.") });
                    d.tabItems.Add(new List<ModalItem> { I("flare", "FLARE", "LIGHT THE AREA.", "X2"), I("decoy", "DECOY", "LURE THE CHASER.", "X1"), I("supply", "SUPPLY PACK", "REFILL 100.", "X3") });
                    d.buttons.Add(B("USE", "use", true)); d.buttons.Add(B("CLOSE", "close"));
                    break;
                case "enemy":
                    d.title = "ENEMY: GOBLIN"; d.sub = "KNOWN ABOUT THIS FOE"; d.icon = "elite";
                    d.tabItems.Add(new List<ModalItem> {
                        I(null, "HP", "", "80"), I(null, "ATK", "BASE 4 + 3 DICE", "4 + DICE"),
                        I(null, "STAGE", "THE ATTACK GROWS WITH THE TURN: T5 X1.25 / T10 X1.5 / T15 X1.75 / T20 X2", "T5 T10 T15 T20"),
                        I(null, "TRAIT", "STEALS GOLD ON HIT.", null, "THIEF", Game2DKit.Red) });
                    d.buttons.Add(B("CLOSE", "close", true));
                    break;
                case "exit":
                    d.title = "LEAVE THE FLOOR?"; d.sub = "YOU REACHED THE EXIT"; d.icon = "gate"; d.w = 220; 
                    d.buttons.Add(B("YES", "go", true)); d.buttons.Add(B("NO", "close"));
                    break;
                default:
                    d.title = id.ToUpperInvariant(); d.buttons.Add(B("CLOSE", "close", true)); break;
            }
            return d;
        }
    }
}
