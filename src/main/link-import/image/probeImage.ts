/**
 * 从文件头判断图片格式与尺寸。
 *
 * 不信 Content-Type：国内 CDN 经常把 webp 标成 image/jpeg，
 * 也经常用 application/octet-stream（网易严选就是，见 probe-logs）。
 * 只读文件头，不解码整张图，所以对几 MB 的商品图也是常数开销。
 */

export type SniffedMime = 'image/jpeg' | 'image/png' | 'image/webp' | 'image/gif';

export interface ImageInfo {
  mime: SniffedMime;
  width: number;
  height: number;
}

function u16be(b: Uint8Array, i: number): number {
  return ((b[i] ?? 0) << 8) | (b[i + 1] ?? 0);
}
function u32be(b: Uint8Array, i: number): number {
  return (((b[i] ?? 0) << 24) >>> 0) + ((b[i + 1] ?? 0) << 16) + ((b[i + 2] ?? 0) << 8) + (b[i + 3] ?? 0);
}
function u32le(b: Uint8Array, i: number): number {
  return ((b[i] ?? 0) | ((b[i + 1] ?? 0) << 8) | ((b[i + 2] ?? 0) << 16) | ((b[i + 3] ?? 0) << 24)) >>> 0;
}

function png(b: Uint8Array): ImageInfo | null {
  if (b.length < 24) return null;
  const sig = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
  if (!sig.every((v, i) => b[i] === v)) return null;
  return { mime: 'image/png', width: u32be(b, 16), height: u32be(b, 20) };
}

function gif(b: Uint8Array): ImageInfo | null {
  if (b.length < 10) return null;
  if (!(b[0] === 0x47 && b[1] === 0x49 && b[2] === 0x46)) return null;
  return { mime: 'image/gif', width: (b[6] ?? 0) | ((b[7] ?? 0) << 8), height: (b[8] ?? 0) | ((b[9] ?? 0) << 8) };
}

function webp(b: Uint8Array): ImageInfo | null {
  if (b.length < 30) return null;
  const tag = String.fromCharCode(...b.subarray(0, 4));
  const fmt = String.fromCharCode(...b.subarray(8, 12));
  if (tag !== 'RIFF' || fmt !== 'WEBP') return null;
  const chunk = String.fromCharCode(...b.subarray(12, 16));
  if (chunk === 'VP8X') {
    const w = 1 + (((b[24] ?? 0) | ((b[25] ?? 0) << 8) | ((b[26] ?? 0) << 16)) & 0xffffff);
    const h = 1 + (((b[27] ?? 0) | ((b[28] ?? 0) << 8) | ((b[29] ?? 0) << 16)) & 0xffffff);
    return { mime: 'image/webp', width: w, height: h };
  }
  if (chunk === 'VP8L') {
    const bits = u32le(b, 21);
    return { mime: 'image/webp', width: (bits & 0x3fff) + 1, height: ((bits >> 14) & 0x3fff) + 1 };
  }
  if (chunk === 'VP8 ') {
    return { mime: 'image/webp', width: u16be(b, 27) & 0x3fff, height: ((b[28] ?? 0) | ((b[29] ?? 0) << 8)) & 0x3fff };
  }
  return null;
}

function jpeg(b: Uint8Array): ImageInfo | null {
  if (b.length < 4 || b[0] !== 0xff || b[1] !== 0xd8) return null;
  let i = 2;
  while (i + 9 < b.length) {
    if (b[i] !== 0xff) { i += 1; continue; }
    const marker = b[i + 1] ?? 0;
    // SOF0..SOF15，排除 DHT(c4)/JPG(c8)/DAC(cc) —— 这三个不是帧头
    if (marker >= 0xc0 && marker <= 0xcf && marker !== 0xc4 && marker !== 0xc8 && marker !== 0xcc) {
      return { mime: 'image/jpeg', height: u16be(b, i + 5), width: u16be(b, i + 7) };
    }
    const len = u16be(b, i + 2);
    if (len < 2) return null;
    i += 2 + len;
  }
  return null;
}

export function probeImage(bytes: Uint8Array): ImageInfo | null {
  return png(bytes) ?? jpeg(bytes) ?? webp(bytes) ?? gif(bytes);
}
