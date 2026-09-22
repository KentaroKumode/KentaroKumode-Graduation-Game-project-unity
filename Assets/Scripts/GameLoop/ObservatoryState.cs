using UnityEngine;

namespace GameLoop
{
    /// <summary>〈さびれた観測所〉のラン跨ぎ状態。
    ///
    /// <para><b>なぜ MetaProgressState ではなく PlayerPrefs 直なのか。</b>
    /// 当初 <c>MetaProgressState.observatoryMeetings</c> に置いたが、
    /// <c>AutoRunner.RunOne</c> が **毎ラン** メタ前処理を走らせるため
    /// State が作り直され、1000 ラン 回して **再訪が 0 回**だった
    /// (初回 20 / 再訪 0)。ラン跨ぎで残すという要件をそもそも満たせていない。</para>
    ///
    /// <para>ここは「博士がこちらを観測した回数」であって進行度でも報酬でもないので、
    /// メタ進行のリセットと運命を共にする必要がない。独立した器に置く。</para>
    /// </summary>
    public static class ObservatoryState
    {
        private const string MeetingsKey = "Observatory.Meetings";
        private const string CopyKey     = "Observatory.CopyHeld";

        /// <summary>博士に会った回数。0 = 初対面。</summary>
        public static int Meetings
        {
            get => PlayerPrefs.GetInt(MeetingsKey, 0);
            private set => PlayerPrefs.SetInt(MeetingsKey, value);
        }

        /// <summary>〈観測所の写し〉を持ち帰ったか。
        /// **次の再訪 1 回でだけ**追加報酬になり、そこで消費される。</summary>
        public static bool CopyHeld
        {
            get => PlayerPrefs.GetInt(CopyKey, 0) != 0;
            private set => PlayerPrefs.SetInt(CopyKey, value ? 1 : 0);
        }

        public static bool IsRevisit => Meetings > 0;

        public static void NoteMet() => Meetings = Meetings + 1;

        /// <summary>写しを持ち帰る（イベント効果から呼ぶ）。</summary>
        public static void TakeCopy()
        {
            CopyHeld = true;
            Debug.Log("[観測所] 写しを持ち帰った（次の再訪 1 回で効く）");
        }

        /// <summary>再訪で写しを使い切る。</summary>
        public static void ConsumeCopy()
        {
            CopyHeld = false;
            Debug.Log("[観測所] 写しを渡した（消費）");
        }

        /// <summary>計測用: 全部戻す。</summary>
        public static void ResetAll()
        {
            PlayerPrefs.DeleteKey(MeetingsKey);
            PlayerPrefs.DeleteKey(CopyKey);
        }
    }
}
