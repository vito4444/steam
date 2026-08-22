/**
 * 够用就好的 8-bit RGBA PNG 读写，只给 CERE-28 的锚点复核图用。
 *
 * 仓库的运行期依赖只有 react / react-dom，为了一张证据图再引一个图像库不值当；
 * PNG 本身就是 zlib + 逐行滤波，node 自带 zlib 就能收拾。
 *
 * 只支持 8-bit truecolour-with-alpha（colour type 6）、非隔行 —— 管线写出来的
 * 抠图全是这一种。遇到别的格式直接报错，不做静默降级。
 */

import zlib from 'node:zlib';

export interface RgbaImage {
  width: number;
  height: number;
  /** RGBA，长度 width * height * 4 */
  data: Uint8Array;
}

const SIGNATURE = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);

function paeth(a: number, b: number, c: number): number {
  const p = a + b - c;
  const pa = Math.abs(p - a);
  const pb = Math.abs(p - b);
  const pc = Math.abs(p - c);
  if (pa <= pb && pa <= pc) return a;
  return pb <= pc ? b : c;
}

export const PNG = {
  read(buffer: Buffer): RgbaImage {
    if (!buffer.subarray(0, 8).equals(SIGNATURE)) throw new Error('not a PNG file');
    let offset = 8;
    let width = 0;
    let height = 0;
    const idat: Buffer[] = [];
    while (offset < buffer.length) {
      const length = buffer.readUInt32BE(offset);
      const type = buffer.toString('ascii', offset + 4, offset + 8);
      const body = buffer.subarray(offset + 8, offset + 8 + length);
      if (type === 'IHDR') {
        width = body.readUInt32BE(0);
        height = body.readUInt32BE(4);
        const depth = body[8];
        const colour = body[9];
        const interlace = body[12];
        if (depth !== 8 || colour !== 6 || interlace !== 0) {
          throw new Error(`unsupported PNG: depth=${depth} colour=${colour} interlace=${interlace}`);
        }
      } else if (type === 'IDAT') {
        idat.push(Buffer.from(body));
      } else if (type === 'IEND') {
        break;
      }
      offset += 12 + length;
    }
    if (!width || !height) throw new Error('PNG has no IHDR');

    const raw = zlib.inflateSync(Buffer.concat(idat));
    const stride = width * 4;
    const data = new Uint8Array(stride * height);
    let source = 0;
    for (let y = 0; y < height; y++) {
      const filter = raw[source++];
      const line = y * stride;
      const previous = line - stride;
      for (let x = 0; x < stride; x++) {
        const value = raw[source++];
        const a = x >= 4 ? data[line + x - 4] : 0;
        const b = y > 0 ? data[previous + x] : 0;
        const c = x >= 4 && y > 0 ? data[previous + x - 4] : 0;
        let out: number;
        switch (filter) {
          case 0: out = value; break;
          case 1: out = value + a; break;
          case 2: out = value + b; break;
          case 3: out = value + ((a + b) >> 1); break;
          case 4: out = value + paeth(a, b, c); break;
          default: throw new Error(`unsupported PNG filter ${filter}`);
        }
        data[line + x] = out & 0xff;
      }
    }
    return { width, height, data };
  },

  write(width: number, height: number, data: Uint8Array): Buffer {
    const stride = width * 4;
    // 全部用 filter 0：这是一次性的证据图，体积无所谓，代码简单要紧。
    const raw = Buffer.alloc((stride + 1) * height);
    for (let y = 0; y < height; y++) {
      raw[y * (stride + 1)] = 0;
      Buffer.from(data.buffer, data.byteOffset + y * stride, stride)
        .copy(raw, y * (stride + 1) + 1);
    }

    const chunk = (type: string, body: Buffer): Buffer => {
      const head = Buffer.alloc(8);
      head.writeUInt32BE(body.length, 0);
      head.write(type, 4, 'ascii');
      const crc = Buffer.alloc(4);
      crc.writeUInt32BE(crc32(Buffer.concat([head.subarray(4), body])) >>> 0, 0);
      return Buffer.concat([head, body, crc]);
    };

    const ihdr = Buffer.alloc(13);
    ihdr.writeUInt32BE(width, 0);
    ihdr.writeUInt32BE(height, 4);
    ihdr[8] = 8;
    ihdr[9] = 6;
    return Buffer.concat([
      SIGNATURE,
      chunk('IHDR', ihdr),
      chunk('IDAT', zlib.deflateSync(raw)),
      chunk('IEND', Buffer.alloc(0)),
    ]);
  },
};

let crcTable: number[] | null = null;
function crc32(buffer: Buffer): number {
  if (!crcTable) {
    crcTable = [];
    for (let n = 0; n < 256; n++) {
      let c = n;
      for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
      crcTable[n] = c;
    }
  }
  let crc = 0xffffffff;
  for (const byte of buffer) crc = crcTable[(crc ^ byte) & 0xff] ^ (crc >>> 8);
  return crc ^ 0xffffffff;
}
