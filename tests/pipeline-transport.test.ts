import { describe, expect, it } from 'vitest';

import { encodePipelineRequest } from '../src/main/pipeline';

/**
 * CERE-28：打包后的管线用 ANSI 代码页解 stdin，不是 UTF-8。成员选了一张名叫
 * `屏幕截图 2026-07-30 005149.png` 的图，管线拿到的是乱码路径，于是报
 * `ASSET_NOT_FOUND: source image was not found`。
 *
 * 最阴的一点：错误信息里回显的路径**看着是对的**。错解一次、再错编一次，
 * 字节又变回原样，所以这个 bug 一直没被看出来。
 *
 * 请求编成纯 ASCII 之后，任何代码页解出来都是同一个字符串。
 */
describe('encodePipelineRequest', () => {
  const chinesePath = 'C:\\Users\\me\\Pictures\\屏幕截图 2026-07-30 005149.png';
  const ESCAPED_SCREENSHOT = '\\u5c4f\\u5e55\\u622a\\u56fe';

  it('emits no byte above ASCII, whatever the payload', () => {
    const line = encodePipelineRequest({ command: 'analyze', image_path: chinesePath });
    expect(/^[\x20-\x7e]*\n$/.test(line)).toBe(true);
    expect(line).not.toContain('屏幕截图');
    expect(line).toContain(ESCAPED_SCREENSHOT);
  });

  it('round-trips to exactly the original request', () => {
    const request = {
      command: 'cutout-subject',
      image_path: chinesePath,
      destination: 'D:\\出力\\body.png',
    };
    expect(JSON.parse(encodePipelineRequest(request))).toEqual(request);
  });

  it('terminates the line so the reader can dispatch it', () => {
    expect(encodePipelineRequest({ command: 'ping' }).endsWith('\n')).toBe(true);
  });

  it('leaves a pure-ASCII request byte-identical to plain JSON', () => {
    const request = { command: 'ping' };
    expect(encodePipelineRequest(request)).toBe(`${JSON.stringify(request)}\n`);
  });
});
