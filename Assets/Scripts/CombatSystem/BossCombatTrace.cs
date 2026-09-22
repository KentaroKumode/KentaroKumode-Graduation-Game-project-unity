using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace CombatSystem
{
    /// <summary><b>特定の層のボス戦だけをターン単位で記録する。</b> 2026-09-13。
    ///
    /// <para><b>なぜ集計ではなく生ログか。</b> 強奪 r10 は 6 層ボス突入時点で
    /// 希望・消耗品・HP が r9 と同等、 パッシブ +3・装備力 +4 と<b>優位</b>なのに、
    /// 6 層ボス突破率だけが 94.9% → 76.3% と 18.6pt 落ちる。 突入時の指標を
    /// 8 通り潰して全部空振りだったので、 差は戦闘の内部にしかない。</para>
    ///
    /// <para><b>平均を見ない。</b> 同じ <c>runIdx</c> で r9 はクリア・r10 は 6F ボス死という
    /// ランを 1 本ずつ突き合わせる。 両アームは 6 層ボスまで同じ経過をたどるので、
    /// <b>強盗の有無だけが違う対</b>になる。 今日の誤診断は全部
    /// 「平均で考えて外した」形だった。</para>
    ///
    /// <para>既定は無効。 有効時も対象は 1 層のボス戦だけなので出力は小さい。</para></summary>
    public static class BossCombatTrace
    {
        /// <summary>記録対象のボス層。 0 = 無効。</summary>
        public static int TargetFloor;
        /// <summary>出力先。 空なら記録しない。</summary>
        public static string OutputPath = "";
        /// <summary>アーム名 (ログの識別用)。</summary>
        public static string Label = "";

        private static readonly List<string> _lines = new List<string>();
        private static bool _active;
        private static int _runIdx;

        public static bool Enabled => TargetFloor > 0 && !string.IsNullOrEmpty(OutputPath);

        /// <summary>ボス戦の開始時に呼ぶ。 対象層でなければ以降の Note は無視される。</summary>
        public static void BeginFight(int floor, int runIdx, string enemyId,
            int playerHp, int playerMaxHp, int enemyHp, int hope, int passives)
        {
            _active = Enabled && floor == TargetFloor;
            if (!_active) return;
            _runIdx = runIdx;
            _lines.Add($"#FIGHT\t{Label}\t{runIdx}\tF{floor}\t{enemyId}"
                     + $"\thp={playerHp}/{playerMaxHp}\tenemyHp={enemyHp}"
                     + $"\thope={hope}\tpassives={passives}");
        }

        public static void NoteTurn(int turn, int enemyAtk, int blockSum, int lossBase,
            int damageToPlayer, int damageToEnemy, int playerHp, int enemyHp,
            int enemyBase, int enemyRoll, float escMul, int atkSum, int diceCount,
            int rawRoll, int diceBonus, int bossBonus, int enemyMaxHp)
        {
            if (!_active) return;
            _lines.Add($"T\t{_runIdx}\t{turn}\tatk={enemyAtk}\tblock={blockSum}"
                     + $"\tthrough={lossBase}\ttaken={damageToPlayer}\tdealt={damageToEnemy}"
                     + $"\thp={playerHp}\tenemyHp={enemyHp}"
                     // 敵攻撃の内訳: base × 倍率 + ロール。 どれが跳ねているかを分ける。
                     + $"\tebase={enemyBase}\teroll={enemyRoll}"
                     + $"\tesc={escMul.ToString("0.###", CultureInfo.InvariantCulture)}"
                     // 配線: 攻撃へ回した合計とダイス本数。 ブロックが薄い理由を見る。
                     + $"\tatkSum={atkSum}\tdice={diceCount}"
                     // ロールの内訳: 素の出目 + パッシブ加算。 **逆算をやめて直接記録する。**
                     + $"\traw={rawRoll}\tbonus={diceBonus}\tbossBonus={bossBonus}"
                     // ボス HP 比。 EmberOmen の周期と EmberFury はこれで決まる。
                     + $"\tehpmax={enemyMaxHp}");
        }

        /// <summary>パッシブ自身に「何を計算したか」を吐かせる。
        /// <b>外から逆算しない。</b> 今日 12 回、 逆算した推測が外れている。</summary>
        public static void NoteEvent(string tag, string detail)
        {
            if (!_active) return;
            _lines.Add($"E\t{_runIdx}\t{tag}\t{detail}");
        }

        public static void EndFight(bool playerWon, int turns)
        {
            if (!_active) return;
            _lines.Add($"#END\t{_runIdx}\t{(playerWon ? "WIN" : "LOSE")}\tturns={turns}");
            _active = false;
            if (_lines.Count > 4000) Flush();
        }

        public static void Flush()
        {
            if (_lines.Count == 0 || string.IsNullOrEmpty(OutputPath)) return;
            try
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(OutputPath));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.AppendAllText(OutputPath, string.Join("\n", _lines) + "\n",
                    new UTF8Encoding(false));
            }
            catch { }
            _lines.Clear();
        }
    }
}
