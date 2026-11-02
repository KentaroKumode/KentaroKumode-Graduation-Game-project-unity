// DICE BOUND 用の効果が共有する道具 (ファイル名が _ で始まるものは効果ではない)。
// 敵の形は絵ごとに違う (穴・切れ目・細い突起がある) ので、 「高さ H×q の行」が空でも位置がはみ出さないようにする。

/** my に最も近い、 体のある行の [y, 左端, 右端]。 体が 1 行も無ければ null。 */
export function rowNear(S, my) {
  my = Math.max(0, Math.min(S.h - 1, Math.round(my)));
  for (let d = 0; d < S.h; d++)
    for (const y of [my - d, my + d]) {
      if (y < 0 || y >= S.h) continue;
      const [l, r] = S.rows[y];
      if (l >= 0) return [y, l, r];
    }
  return null;
}

/** list が空なら fallback から選ぶ (細い形では「内側」の画素が無いことがある)。 */
export const pickFrom = (R, list, fallback) => {
  const L = list.length ? list : fallback;
  return L[Math.floor(R.next() * L.length)];
};
