/**
 * 搭配画板（CERE-21）的数据结构。
 *
 * 模特视图解决的是「上身效果」，画板解决的是「看清每一件」——单品各自摊开，
 * 默认布局不允许任何两件互相遮挡。两者共用同一份衣橱数据与同一个 Look 存储，
 * 画板只是在 Look 上多挂一份 `board` 布局，没有第二套素材 schema。
 *
 * 坐标系：画板自己的画布坐标（`BOARD_CANVAS`），与模特底图画布无关。
 * 元素记录的是**中心点 + 未旋转时的宽高 + 旋转角**，这样缩放和旋转都绕中心，
 * 拖动时不会因为参考点漂移而跳。
 */

export const BOARD_CANVAS = { w: 1400, h: 1800 } as const;

export type BoardBackgroundId =
  | 'paper_warm' | 'plain' | 'linen' | 'grid' | 'sand' | 'slate';

export interface BoardBackground {
  id: BoardBackgroundId;
  name: string;
  /** 底色，同时用作缩略图色块 */
  base: string;
  kind: 'solid' | 'paper' | 'linen' | 'grid' | 'gradient';
  /** 纹理线条 / 噪点颜色 */
  detail: string;
  /** 这张背景下的默认文字颜色 */
  ink: string;
}

export const BOARD_BACKGROUNDS: BoardBackground[] = [
  { id: 'paper_warm', name: '暖白纸', base: '#F7F3EC', kind: 'paper', detail: 'rgba(120,100,74,0.055)', ink: '#26231E' },
  { id: 'plain', name: '纯白', base: '#FFFFFF', kind: 'solid', detail: 'rgba(0,0,0,0)', ink: '#1E1C1A' },
  { id: 'linen', name: '亚麻纹', base: '#EFE9DE', kind: 'linen', detail: 'rgba(110,92,68,0.07)', ink: '#2A2620' },
  { id: 'grid', name: '细格', base: '#FAF8F4', kind: 'grid', detail: 'rgba(120,110,95,0.13)', ink: '#232019' },
  { id: 'sand', name: '沙色渐变', base: '#F1E7DA', kind: 'gradient', detail: '#E3D3C0', ink: '#312A22' },
  { id: 'slate', name: '深灰', base: '#23211E', kind: 'solid', detail: 'rgba(255,255,255,0.05)', ink: '#F2EDE5' },
];

export function boardBackground(id: BoardBackgroundId): BoardBackground {
  return BOARD_BACKGROUNDS.find((b) => b.id === id) ?? BOARD_BACKGROUNDS[0];
}

export type BoardFont = 'serif' | 'serif_italic' | 'sans';

export const BOARD_FONT_STACK: Record<BoardFont, string> = {
  serif: `'Playfair Display', Georgia, 'Songti SC', 'Source Han Serif SC', serif`,
  serif_italic: `italic 1em 'Playfair Display', Georgia, 'Songti SC', serif`,
  sans: `'Inter', 'Segoe UI', 'PingFang SC', 'Microsoft YaHei UI', sans-serif`,
};

export const BOARD_FONT_LABEL: Record<BoardFont, string> = {
  serif: '衬线',
  serif_italic: '衬线斜体',
  sans: '无衬线',
};

export interface BoardItem {
  id: string;
  kind: 'asset' | 'text';
  /** kind === 'asset' 时指向衣橱素材 id */
  assetId?: string;
  /** kind === 'text' 时的内容 */
  text?: string;
  font?: BoardFont;
  color?: string;
  /** 中心点（画布坐标） */
  x: number;
  y: number;
  /** 未旋转时的包围盒尺寸（画布坐标） */
  w: number;
  h: number;
  /** 顺时针角度 */
  rotation: number;
  /** 层级，越大越靠前 */
  z: number;
  flipX?: boolean;
}

export interface BoardLook {
  version: 1;
  canvas: { w: number; h: number };
  background: BoardBackgroundId;
  items: BoardItem[];
}

export const OCCASIONS = [
  { key: 'work', label: '工作' },
  { key: 'casual', label: '休闲' },
  { key: 'date', label: '约会' },
  { key: 'sport', label: '运动' },
  { key: 'resort', label: '度假' },
  { key: 'travel', label: '旅游' },
  { key: 'home', label: '居家' },
] as const;

export type OccasionKey = (typeof OCCASIONS)[number]['key'];

export function occasionLabel(key: string): string {
  return OCCASIONS.find((o) => o.key === key)?.label ?? key;
}

/** 旋转后的轴对齐包围盒 —— 重叠检查与吸附都用它 */
export function itemBounds(it: Pick<BoardItem, 'x' | 'y' | 'w' | 'h' | 'rotation'>): {
  x1: number; y1: number; x2: number; y2: number;
} {
  const rad = (it.rotation * Math.PI) / 180;
  const c = Math.abs(Math.cos(rad));
  const s = Math.abs(Math.sin(rad));
  const w = it.w * c + it.h * s;
  const h = it.w * s + it.h * c;
  return { x1: it.x - w / 2, y1: it.y - h / 2, x2: it.x + w / 2, y2: it.y + h / 2 };
}

/**
 * 两件是否互相遮挡。
 *
 * 素材都按 alpha 包围盒裁过（见 CERE-11 的导入流程），所以包围盒相交
 * 基本等价于视觉相交。留 `tolerance` 是因为包围盒四角常是透明的，
 * 极小的接触不该判成遮挡。
 */
export function overlapArea(a: BoardItem, b: BoardItem): number {
  const A = itemBounds(a);
  const B = itemBounds(b);
  const w = Math.min(A.x2, B.x2) - Math.max(A.x1, B.x1);
  const h = Math.min(A.y2, B.y2) - Math.max(A.y1, B.y1);
  if (w <= 0 || h <= 0) return 0;
  return w * h;
}

/** 返回所有互相遮挡超过阈值的组合；空数组 = 每件单品都完整可见 */
export function findOverlaps(items: BoardItem[], tolerance = 0.02): [string, string][] {
  const out: [string, string][] = [];
  const visible = items.filter((i) => i.kind === 'asset');
  for (let i = 0; i < visible.length; i++) {
    for (let j = i + 1; j < visible.length; j++) {
      const a = visible[i];
      const b = visible[j];
      const area = overlapArea(a, b);
      if (area <= 0) continue;
      const smaller = Math.min(a.w * a.h, b.w * b.h);
      if (area / smaller > tolerance) out.push([a.id, b.id]);
    }
  }
  return out;
}
