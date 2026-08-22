/**
 * CERE-59：把 GitHub 给的更新说明变成人能读的纯文本。
 *
 * electron-updater 的 GitHub provider 从 Releases 的 Atom feed 取 `<content>`，
 * 那是**渲染后的 HTML**。直接扔进界面，用户看到的是
 * `<p><strong>这是第一个……</strong></p>` 这种标签原文 —— 0.4.5 的更新弹窗里
 * 就是这么显示的。
 *
 * 这里做纯文本化而不是渲染 HTML：更新说明是从网络拿来的内容，
 * 塞进 `dangerouslySetInnerHTML` 等于给自己开一个注入口子，
 * 而这块 UI 需要的只是「让人读懂这一版改了什么」。
 */

const ENTITIES: Record<string, string> = {
  amp: '&',
  lt: '<',
  gt: '>',
  quot: '"',
  apos: "'",
  nbsp: ' ',
  hellip: '…',
  mdash: '—',
  ndash: '–',
  laquo: '«',
  raquo: '»',
  ldquo: '“',
  rdquo: '”',
  lsquo: '‘',
  rsquo: '’',
};

function decodeEntities(text: string): string {
  return text
    .replace(/&#(\d+);/g, (_, code: string) => String.fromCodePoint(Number(code)))
    .replace(/&#x([0-9a-f]+);/gi, (_, code: string) => String.fromCodePoint(parseInt(code, 16)))
    .replace(/&([a-z]+);/gi, (match, name: string) => ENTITIES[name.toLowerCase()] ?? match);
}

/**
 * HTML → 纯文本。块级元素换行，列表项加项目符号，其余标签一律去掉。
 * 传进来已经是纯文本时原样返回（本地构建、或 provider 换了实现）。
 */
export function releaseNotesToText(raw: unknown): string {
  const source = Array.isArray(raw)
    ? raw
        .map((entry) => {
          const item = entry as { version?: string; note?: string | null };
          return item.note ? `${item.version ? `## ${item.version}\n` : ''}${item.note}` : '';
        })
        .filter(Boolean)
        .join('\n\n')
    : typeof raw === 'string'
      ? raw
      : '';
  if (!source.trim()) return '';

  const text = source
    // 代码块和表格里的换行必须先保住，不然整段挤成一行。
    .replace(/<\s*br\s*\/?\s*>/gi, '\n')
    .replace(/<\s*\/\s*(p|div|h[1-6]|tr|pre|blockquote)\s*>/gi, '\n\n')
    .replace(/<\s*li[^>]*>/gi, '\n• ')
    .replace(/<\s*\/\s*(li|ul|ol|table)\s*>/gi, '\n')
    .replace(/<\s*\/?\s*t[dh][^>]*>/gi, ' ')
    .replace(/<[^>]+>/g, '');

  return decodeEntities(text)
    .replace(/\r\n/g, '\n')
    .replace(/[ \t]+\n/g, '\n')
    .replace(/\n{3,}/g, '\n\n')
    .trim();
}
