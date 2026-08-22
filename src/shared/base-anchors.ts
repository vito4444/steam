/**
 * 从人物剪影推锚点（CERE-28）。
 *
 * 成员上传一张模特照片，应用要自己把 17 个锚点标出来 —— 不能要求对方去改
 * `manifest.json`。这里不做姿态估计（离线包里没有姿态模型，也不该为了几个点
 * 再塞一个 200 MB 的网络进去），而是**量剪影**：
 *
 *   - 头顶 / 脚底 = alpha 的第一行和最后一行；
 *   - 脖子 = 头和肩之间最窄的一行（人体在这里必然收腰）；
 *   - 肩线 = 脖子往下一小段里最宽的一行；
 *   - 腰 = 胸到胯之间最窄的一行；胯 = 腰往下最宽的一行；
 *   - 裆 = 剪影第一次从「一段」裂成「两段」的那一行（两条腿分开了）；
 *   - 腕 = 肩到胯之间左右最外侧的点（手臂垂在体侧时就是它）。
 *
 * 这套量法和应用现有的做法是一致的：衣物缩放本来就不读 manifest，而是实测
 * 底图轮廓在锚点那一行有多宽（见 `render/fit.ts`）。所以锚点只要把**纵向层次**
 * 排对，衣服的横向尺寸自然跟着真人走。
 *
 * 量出来的结果一律过一遍单调化：解剖顺序不能乱，哪怕剪影本身很奇怪。
 */

import { BASE_ANCHORS, type AnchorName, type Point } from './spec';

export interface Silhouette {
  width: number;
  height: number;
  /** 每像素一个 alpha 值，长度必须是 width * height */
  alpha: Uint8Array | Uint8ClampedArray;
}

/** 一行里的一段连续实心像素 */
interface Run {
  start: number;
  end: number;
}

interface RowProfile {
  runs: Run[];
  left: number;
  right: number;
  width: number;
  /** 实心像素个数（不含段之间的空隙） */
  solid: number;
}

export const ANCHOR_ORDER: AnchorName[] = [
  'head_top', 'eye_line', 'chin', 'neck', 'shoulder_line',
  'chest', 'waist', 'hip', 'crotch', 'knee', 'foot_base',
];

const ALPHA_THRESHOLD = 16;
/** 小于这个宽度的段当成噪点（碎发、耳环反光之类），不参与量身 */
const MIN_RUN_RATIO = 0.012;

function profileRow(silhouette: Silhouette, y: number, minRun: number): RowProfile {
  const { width, alpha } = silhouette;
  const runs: Run[] = [];
  let start = -1;
  let solid = 0;
  for (let x = 0; x < width; x++) {
    const on = alpha[y * width + x] >= ALPHA_THRESHOLD;
    if (on) {
      solid++;
      if (start < 0) start = x;
    } else if (start >= 0) {
      if (x - start >= minRun) runs.push({ start, end: x - 1 });
      start = -1;
    }
  }
  if (start >= 0 && width - start >= minRun) runs.push({ start, end: width - 1 });
  if (runs.length === 0) return { runs, left: -1, right: -1, width: 0, solid: 0 };
  const left = runs[0].start;
  const right = runs[runs.length - 1].end;
  return { runs, left, right, width: right - left + 1, solid };
}

function centerOf(row: RowProfile, fallback: number): number {
  if (row.runs.length === 0) return fallback;
  return Math.round((row.left + row.right) / 2);
}

/** 在 [from, to] 里找 measure 值最小 / 最大的那一行；空行跳过 */
function extremeRow(
  rows: RowProfile[],
  from: number,
  to: number,
  mode: 'min' | 'max',
  measure: (row: RowProfile) => number = (row) => row.width,
): number | null {
  const lo = Math.max(0, Math.min(from, to));
  const hi = Math.min(rows.length - 1, Math.max(from, to));
  let best: number | null = null;
  let bestValue = mode === 'min' ? Number.POSITIVE_INFINITY : -1;
  for (let y = lo; y <= hi; y++) {
    const value = measure(rows[y]);
    if (value <= 0) continue;
    if (mode === 'min' ? value < bestValue : value > bestValue) {
      bestValue = value;
      best = y;
    }
  }
  return best;
}

/**
 * 躯干宽度 = 过中轴那一段的宽度。
 *
 * 手臂垂在体侧时，剪影会在某一行突然从「一整段」裂成「左臂 / 躯干 / 右臂」三段，
 * 整行宽度于是从肩宽跳到指尖跨度。直接拿整行宽度找腰和胯，量到的是手臂张开
 * 的程度，不是身体 —— 实测腰会被顶到胸口下面。只量过中轴的那一段就没这问题。
 */
function torsoWidth(row: RowProfile, centerX: number): number {
  for (const run of row.runs) {
    if (centerX >= run.start && centerX <= run.end) return run.end - run.start + 1;
  }
  return 0;
}

/**
 * 找「两条腿分开」的第一行。
 *
 * 不能只看「这一行有没有两段以上」：手臂离开身体时也会裂成多段，而且裂得更早。
 * 裆的特征是**缝隙在中轴上**，所以只认中轴附近的那道缝。
 */
function legSplitRow(
  rows: RowProfile[],
  from: number,
  to: number,
  centerX: number,
  tolerance: number,
): number | null {
  const lo = Math.max(0, Math.min(from, to));
  const hi = Math.min(rows.length - 1, Math.max(from, to));
  for (let y = lo; y <= hi; y++) {
    const runs = rows[y].runs;
    for (let i = 0; i + 1 < runs.length; i++) {
      const gapCenter = (runs[i].end + runs[i + 1].start) / 2;
      if (Math.abs(gapCenter - centerX) <= tolerance) return y;
    }
  }
  return null;
}

/**
 * 缩放兜底锚点表到目标画布。
 *
 * 剪影量不出来时（比如整张图几乎全实心）用它，至少让画布尺寸是对的，
 * 不会把衣服贴到画布外面去。
 */
export function scaledFallbackAnchors(width: number, height: number): Record<AnchorName, Point> {
  const sx = width / 1152;
  const sy = height / 2304;
  const out = {} as Record<AnchorName, Point>;
  for (const [name, point] of Object.entries(BASE_ANCHORS) as [AnchorName, Point][]) {
    out[name] = { x: Math.round(point.x * sx), y: Math.round(point.y * sy) };
  }
  return out;
}

export interface DerivedAnchors {
  anchors: Record<AnchorName, Point>;
  /** true = 剪影不可用，返回的是按画布缩放过的兜底锚点 */
  fallback: boolean;
  /** 量身过程里值得如实告诉用户的观察，比如两条腿没分开 */
  notes: string[];
}

export function deriveAnchors(silhouette: Silhouette): DerivedAnchors {
  const { width, height } = silhouette;
  if (width < 8 || height < 16 || silhouette.alpha.length < width * height) {
    return { anchors: scaledFallbackAnchors(Math.max(width, 1), Math.max(height, 1)), fallback: true, notes: ['剪影尺寸不可用'] };
  }

  const minRun = Math.max(2, Math.round(width * MIN_RUN_RATIO));
  const rows: RowProfile[] = [];
  for (let y = 0; y < height; y++) rows.push(profileRow(silhouette, y, minRun));

  const filled = rows.map((row) => row.width > 0);
  const headTopY = filled.indexOf(true);
  const footBaseY = filled.lastIndexOf(true);
  const notes: string[] = [];
  if (headTopY < 0 || footBaseY - headTopY < 16) {
    return { anchors: scaledFallbackAnchors(width, height), fallback: true, notes: ['照片里没有量得出的人物剪影'] };
  }

  const bodyH = footBaseY - headTopY;
  const at = (ratio: number) => Math.round(headTopY + bodyH * ratio);

  // 脖子：头顶往下 8%–30% 之间最窄的一行。
  const neckY = extremeRow(rows, at(0.08), at(0.30), 'min') ?? at(0.18);

  /*
   * 肩线不能取「最宽的一行」。手臂垂在体侧时，脖子往下最宽的一行落在腋下 /
   * 上臂，量出来的肩线会掉到胸口去（实测差了将近 100px）。肩膀真正的特征是
   * **宽度从脖子那一档突然涨上来**，所以取「第一次涨到肩带宽度 62% 的那一行」。
   */
  const shoulderBandEnd = Math.min(neckY + Math.round(bodyH * 0.16), footBaseY);
  const shoulderPeakY = extremeRow(rows, neckY, shoulderBandEnd, 'max');
  let shoulderY = shoulderPeakY ?? Math.min(neckY + Math.round(bodyH * 0.05), footBaseY);
  if (shoulderPeakY !== null) {
    const neckWidth = Math.max(rows[neckY].width, 1);
    const peakWidth = rows[shoulderPeakY].width;
    const target = neckWidth + (peakWidth - neckWidth) * 0.62;
    for (let y = neckY; y <= shoulderPeakY; y++) {
      if (rows[y].width >= target) { shoulderY = y; break; }
    }
  }

  const headH = Math.max(neckY - headTopY, 1);
  const chinY = Math.round(headTopY + headH * 0.92);
  const eyeY = Math.round(headTopY + headH * 0.45);

  const chestY = Math.min(shoulderY + Math.round(bodyH * 0.09), footBaseY);
  const centerX = centerOf(rows[chestY], Math.round(width / 2));
  const torso = (row: RowProfile) => torsoWidth(row, centerX);

  // 裆先量：胯和腰都要限制在裆以上，顺序反了会全挤到一起。
  let crotchY = legSplitRow(
    rows,
    chestY + Math.round(bodyH * 0.10),
    footBaseY,
    centerX,
    Math.max(Math.round(width * 0.10), 4),
  ) ?? -1;
  if (crotchY < 0) {
    crotchY = Math.min(chestY + Math.round(bodyH * 0.30), footBaseY);
    notes.push('两条腿没有分开（裙装或并腿站姿），裆位按比例估算');
  }

  // 胯：胸到裆之间躯干最宽的一行。
  const hipY = extremeRow(rows, chestY + Math.round(bodyH * 0.06), Math.max(crotchY - Math.round(bodyH * 0.01), chestY + 1), 'max', torso)
    ?? Math.round((chestY + crotchY) / 2);
  // 腰：胸到胯之间躯干最窄的一行。
  const waistY = extremeRow(rows, chestY + Math.round(bodyH * 0.03), Math.max(hipY - Math.round(bodyH * 0.01), chestY + 1), 'min', torso)
    ?? Math.round((chestY + hipY) / 2);

  const legSpan = Math.max(footBaseY - crotchY, 1);
  const kneeY = crotchY + Math.round(legSpan * 0.5);
  const ankleY = crotchY + Math.round(legSpan * 0.9);

  // 腕：肩到胯之间左右最外侧的点 —— 手臂垂在体侧时最外沿就是手。
  let wristL = { x: rows[shoulderY].left, y: Math.round((shoulderY + hipY) / 2) };
  let wristR = { x: rows[shoulderY].right, y: wristL.y };
  let minX = Number.POSITIVE_INFINITY;
  let maxX = -1;
  for (let y = shoulderY; y <= Math.min(hipY + Math.round(bodyH * 0.06), footBaseY); y++) {
    const row = rows[y];
    if (row.width <= 0) continue;
    if (row.left < minX) { minX = row.left; wristL = { x: row.left, y }; }
    if (row.right > maxX) { maxX = row.right; wristR = { x: row.right, y }; }
  }

  // 肩点从最外沿往里收一点：外沿是袖子/手臂的边，肩关节在里面。
  const shoulderRow = rows[shoulderY];
  const inset = Math.round(shoulderRow.width * 0.06);
  const shoulderL = { x: shoulderRow.left + inset, y: shoulderY };
  const shoulderR = { x: shoulderRow.right - inset, y: shoulderY };

  const ankleRow = rows[Math.min(ankleY, height - 1)];
  const ankleCenter = centerOf(ankleRow, Math.round(width / 2));
  const ankleL = ankleRow.runs.length >= 2
    ? { x: Math.round((ankleRow.runs[0].start + ankleRow.runs[0].end) / 2), y: ankleY }
    : { x: Math.round(ankleCenter - ankleRow.width * 0.22), y: ankleY };
  const ankleR = ankleRow.runs.length >= 2
    ? { x: Math.round((ankleRow.runs[ankleRow.runs.length - 1].start + ankleRow.runs[ankleRow.runs.length - 1].end) / 2), y: ankleY }
    : { x: Math.round(ankleCenter + ankleRow.width * 0.22), y: ankleY };

  const anchors: Record<AnchorName, Point> = {
    head_top: { x: centerOf(rows[headTopY], Math.round(width / 2)), y: headTopY },
    eye_line: { x: centerOf(rows[eyeY], Math.round(width / 2)), y: eyeY },
    chin: { x: centerOf(rows[chinY], Math.round(width / 2)), y: chinY },
    neck: { x: centerOf(rows[neckY], Math.round(width / 2)), y: neckY },
    shoulder_line: { x: centerOf(shoulderRow, Math.round(width / 2)), y: shoulderY },
    shoulder_l: shoulderL,
    shoulder_r: shoulderR,
    chest: { x: centerOf(rows[chestY], Math.round(width / 2)), y: chestY },
    waist: { x: centerOf(rows[waistY], Math.round(width / 2)), y: waistY },
    hip: { x: centerOf(rows[hipY], Math.round(width / 2)), y: hipY },
    crotch: { x: centerOf(rows[crotchY], Math.round(width / 2)), y: crotchY },
    wrist_l: wristL,
    wrist_r: wristR,
    knee: { x: centerOf(rows[Math.min(kneeY, height - 1)], Math.round(width / 2)), y: kneeY },
    ankle_l: ankleL,
    ankle_r: ankleR,
    foot_base: { x: centerOf(rows[footBaseY], Math.round(width / 2)), y: footBaseY },
  };

  return { anchors: normalizeAnchors(anchors, width, height), fallback: false, notes };
}

/**
 * 夹回画布，并把纵向顺序强制单调。
 *
 * 剪影可能很怪（举手、宽摆裙、背景没抠干净），量出来的某一层可能跑到上一层
 * 上面去。与其把一套自相矛盾的锚点写进底图包，不如在这里压平 —— 衣服贴歪
 * 好歹还能手动微调，锚点顺序反了会直接把上下装画反。
 */
export function normalizeAnchors(
  anchors: Record<AnchorName, Point>,
  width: number,
  height: number,
): Record<AnchorName, Point> {
  const clampX = (x: number) => Math.max(0, Math.min(width - 1, Math.round(x)));
  const clampY = (y: number) => Math.max(0, Math.min(height - 1, Math.round(y)));
  const out = {} as Record<AnchorName, Point>;
  for (const [name, point] of Object.entries(anchors) as [AnchorName, Point][]) {
    out[name] = { x: clampX(point.x), y: clampY(point.y) };
  }

  /*
   * 单调化时要给后面的锚点**留出行数**。直接 clamp 到 height-1 的话，一组退化
   * 输入（比如全部落在画布外）会被压成同一行，顺序保证等于没有。所以第 i 个
   * 锚点的上限是 height-1-(剩下几个)，只要画布高度不少于锚点个数，严格递增就
   * 一定成立。
   */
  let previous = -1;
  for (let i = 0; i < ANCHOR_ORDER.length; i++) {
    const name = ANCHOR_ORDER[i];
    const ceiling = height - 1 - (ANCHOR_ORDER.length - 1 - i);
    const y = Math.min(Math.max(out[name].y, previous + 1), Math.max(ceiling, 0));
    out[name] = { x: out[name].x, y: clampY(y) };
    previous = out[name].y;
  }

  // 成对锚点跟随它们所在的那一层，左右不许交叉。
  out.shoulder_l = { x: Math.min(out.shoulder_l.x, out.shoulder_r.x), y: out.shoulder_line.y };
  out.shoulder_r = { x: Math.max(out.shoulder_l.x, out.shoulder_r.x), y: out.shoulder_line.y };
  const wristY = clampY(Math.max(out.wrist_l.y, out.chest.y));
  out.wrist_l = { x: Math.min(out.wrist_l.x, out.wrist_r.x), y: wristY };
  out.wrist_r = { x: Math.max(out.wrist_l.x, out.wrist_r.x), y: wristY };
  const ankleY = clampY(Math.max(out.ankle_l.y, out.knee.y + 1));
  out.ankle_l = { x: Math.min(out.ankle_l.x, out.ankle_r.x), y: ankleY };
  out.ankle_r = { x: Math.max(out.ankle_l.x, out.ankle_r.x), y: ankleY };
  out.foot_base = { x: out.foot_base.x, y: clampY(Math.max(out.foot_base.y, ankleY + 1)) };
  return out;
}
