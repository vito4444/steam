import { loadImage } from './images';
import { alphaCeiling } from './pick';
import type { StageLayer, StageScene } from './types';

const BACKGROUNDS: Record<string, [string, string]> = {
  studio_warm: ['#F6F1EA', '#E2D8CB'],
  studio_cool: ['#F2F4F7', '#D9DEE6'],
};

/**
 * 分层合成器。
 *
 * 只做四件事：按 z 叠图、按遮挡规则裁切、羽化抠图边缘、在衣物与下层交界处
 * 压一道接触阴影。**不做任何风格化**——没有描边、没有色彩量化、没有卡通阴影。
 *
 * 裁切分三步，顺序不能换：
 *   1. 区间守卫  这件衣服只允许出现在身上的哪一段（围巾不许爬到下巴以上）
 *   2. 身体遮罩  超出身体轮廓的部分削掉（袖子不许飘在体侧）
 *   3. 成对挖除  被别的件挖掉（裤脚盖鞋帮、上装塞进腰里）
 *
 * 前两步是「这件自己该长什么样」，第三步要用到**别的件裁完之后**的轮廓，
 * 所以必须分两轮：先全部各自裁好，再互相挖。边裁边挖的话结果依赖处理顺序，
 * 而遮挡关系里 z 高挖 z 低（裙挖打底裤）和 z 低挖 z 高（裤挖鞋）同时存在，
 * 没有任何一个顺序能同时满足。
 *
 * 接触阴影必须裁进「已经画好的那些层」的轮廓里，否则会糊到背景上变成
 * 一圈脏边。所以合成时同步维护一张 accum 画布记录已画内容的 alpha，
 * 用它当阴影的裁剪掩膜：外套的影子落在衬衫上，衬衫的影子落在身体上。
 */
export class Canvas2DCompositor {
  private canvas: HTMLCanvasElement | null = null;
  /** 每次 render 领一个号；画完发现号过期就丢弃，见下面 */
  private token = 0;

  mount(canvas: HTMLCanvasElement): void {
    this.canvas = canvas;
  }

  destroy(): void {
    this.canvas = null;
    this.token++;
  }

  /**
   * 渲染一帧。
   *
   * 两件事必须一起做，少一件都会花屏：
   *   **离屏后再贴**  合成要等图片、遮罩、alpha 统计一路 await，直接往可见画布
   *                   上画的话，画到一半的中间态会被用户看见。
   *   **丢弃过期帧**  连着换三件衣服会同时起三次渲染，它们的 await 交错完成，
   *                   谁后画完谁盖住谁 —— 画面上会出现两个叠在一起的模特。
   *                   领号 + 画完比号，过期的直接扔掉。
   */
  async render(scene: StageScene): Promise<void> {
    const canvas = this.canvas;
    if (!canvas) return;
    const my = ++this.token;
    const dpr = Math.min(window.devicePixelRatio || 1, 2);
    const w = Math.round(scene.width * dpr);
    const h = Math.round(scene.height * dpr);

    const off = newCanvas(w, h);
    const octx = off.getContext('2d');
    if (!octx) return;
    octx.scale(dpr, dpr);
    await this.paint(octx, scene, false, dpr);

    if (my !== this.token || this.canvas !== canvas) return;
    if (canvas.width !== w || canvas.height !== h) {
      canvas.width = w;
      canvas.height = h;
    }
    const ctx = canvas.getContext('2d');
    if (!ctx) return;
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.clearRect(0, 0, w, h);
    ctx.drawImage(off, 0, 0);
  }

  async toDataURL(scene: StageScene, opts: { transparent: boolean; scale: number }): Promise<string> {
    const out = document.createElement('canvas');
    out.width = Math.round(scene.width * opts.scale);
    out.height = Math.round(scene.height * opts.scale);
    const ctx = out.getContext('2d');
    if (!ctx) throw new Error('no 2d context');
    ctx.scale(opts.scale, opts.scale);
    await this.paint(ctx, scene, opts.transparent, opts.scale);
    return out.toDataURL('image/png');
  }

  private async paint(
    ctx: CanvasRenderingContext2D,
    scene: StageScene,
    transparent: boolean,
    ratio: number,
  ): Promise<void> {
    const { width, height } = scene;
    const pw = Math.max(Math.round(width * ratio), 1);
    const ph = Math.max(Math.round(height * ratio), 1);

    ctx.clearRect(0, 0, width, height);

    if (!transparent && scene.background !== 'none') {
      if (scene.background === 'solid') {
        ctx.fillStyle = scene.backgroundColor ?? '#EFEAE2';
        ctx.fillRect(0, 0, width, height);
      } else {
        const [a, b] = BACKGROUNDS[scene.background];
        const g = ctx.createLinearGradient(0, 0, width * 0.35, height);
        g.addColorStop(0, a);
        g.addColorStop(1, b);
        ctx.fillStyle = g;
        ctx.fillRect(0, 0, width, height);
      }
    }

    if (!transparent && scene.groundShadow) {
      drawGroundShadow(ctx, width, height);
    }

    ctx.imageSmoothingEnabled = true;
    ctx.imageSmoothingQuality = 'high';

    const layers = [...scene.layers].sort((a, b) => a.z - b.z);
    const sprites = await this.buildGarmentSprites(layers, scene, pw, ph, ratio, width, height);

    const accum = newCanvas(pw, ph);
    const actx = accum.getContext('2d')!;
    actx.setTransform(ratio, 0, 0, ratio, 0, 0);
    actx.clearRect(0, 0, width, height);
    actx.imageSmoothingQuality = 'high';

    // fill/multiply 成对出现：先把 fill 层合成到暂存画布，multiply 层直接叠在其上
    let pending: { canvas: HTMLCanvasElement; layer: StageLayer } | null = null;

    const flush = () => {
      if (!pending) return;
      ctx.save();
      ctx.globalAlpha = pending.layer.opacity;
      ctx.drawImage(pending.canvas, 0, 0, width, height);
      ctx.restore();
      actx.drawImage(pending.canvas, 0, 0, width, height);
      pending = null;
    };

    for (const layer of layers) {
      if (layer.kind === 'garment') {
        const sprite = sprites.get(layer.key);
        if (!sprite) continue;
        flush();

        if (layer.contact) {
          const shadow = buildContactShadow(sprite, accum, layer, pw, ph, ratio);
          if (shadow) {
            ctx.drawImage(shadow, 0, 0, width, height);
            actx.drawImage(shadow, 0, 0, width, height);
          }
        }

        ctx.save();
        ctx.globalAlpha = layer.opacity;
        if (layer.highlight) {
          ctx.shadowColor = 'rgba(214,127,74,0.9)';
          ctx.shadowBlur = 24;
        }
        ctx.drawImage(sprite, 0, 0, width, height);
        ctx.restore();
        actx.drawImage(sprite, 0, 0, width, height);
        continue;
      }

      const img = await loadImage(layer.url).catch(() => null);
      if (!img) continue;

      if (layer.mode === 'multiply' && pending) {
        const sctx = pending.canvas.getContext('2d')!;
        sctx.save();
        sctx.setTransform(ratio, 0, 0, ratio, 0, 0);
        sctx.globalCompositeOperation = 'multiply';
        sctx.drawImage(img, layer.dx, layer.dy, layer.dw, layer.dh);
        sctx.restore();
        continue;
      }

      flush();

      if (layer.mode === 'fill') {
        const c = newCanvas(pw, ph);
        const sctx = c.getContext('2d')!;
        sctx.setTransform(ratio, 0, 0, ratio, 0, 0);
        sctx.imageSmoothingQuality = 'high';
        sctx.drawImage(img, layer.dx, layer.dy, layer.dw, layer.dh);
        sctx.globalCompositeOperation = 'source-in';
        sctx.fillStyle = layer.tint ?? '#CCCCCC';
        sctx.fillRect(0, 0, width, height);
        pending = { canvas: c, layer };
        continue;
      }

      ctx.save();
      ctx.globalAlpha = layer.opacity;
      ctx.drawImage(img, layer.dx, layer.dy, layer.dw, layer.dh);
      ctx.restore();
      actx.drawImage(img, layer.dx, layer.dy, layer.dw, layer.dh);
    }
    flush();
  }

  /** 两轮：先各自裁（守卫 + 身体遮罩），再互相挖 */
  private async buildGarmentSprites(
    layers: StageLayer[],
    scene: StageScene,
    pw: number,
    ph: number,
    ratio: number,
    width: number,
    height: number,
  ): Promise<Map<string, HTMLCanvasElement>> {
    const garments = layers.filter((l) => l.kind === 'garment');
    const self = new Map<string, HTMLCanvasElement>();

    for (const layer of garments) {
      const img = await loadImage(layer.url).catch(() => null);
      if (!img) continue;
      const sprite = buildSprite(img, layer, pw, ph, ratio, await alphaCeiling(layer.url));
      const clip = layer.clip;
      if (clip) {
        const guard = bandStencil(pw, ph, clip.keepFrom, clip.keepTo, clip.keepFeather, ratio);
        if (guard) applyStencil(sprite, guard);

        if (clip.mask) {
          const mask = await scene.bodyMask(clip.mask.growCanvas);
          if (mask) {
            const stencil = maskStencil(mask.canvas, clip.mask, pw, ph, ratio, width, height);
            applyStencil(sprite, stencil);
          }
        }
      }
      self.set(layer.key, sprite);
    }

    const out = new Map<string, HTMLCanvasElement>();
    for (const layer of garments) {
      const sprite = self.get(layer.key);
      if (!sprite) continue;
      const erasedBy = layer.clip?.erasedBy ?? [];
      if (erasedBy.length === 0) {
        out.set(layer.key, sprite);
        continue;
      }
      const copy = newCanvas(pw, ph);
      copy.getContext('2d')!.drawImage(sprite, 0, 0);
      for (const e of erasedBy) {
        const occluder = self.get(e.key);
        if (occluder) eraseWith(copy, occluder, e, pw, ph, ratio);
      }
      out.set(layer.key, copy);
    }
    return out;
  }
}

/**
 * 画一件衣物、补偿欠实的抠图、羽化边缘。
 *
 * **不透明度补偿**：CERE-10 有几件抠图整件都是半透明的（alpha 上限只有 180
 * 左右，全图没有一个 255），贴上去就是一件能看见身体的衬衫。根因在素材侧，
 * 但渲染层不能装作没看见。补偿的做法是把同一张图多叠一两遍 —— alpha 按
 * `1-(1-a)^n` 往上顶，**颜色一个像素都没动**（同色自叠还是同色）。只在
 * alpha 上限明显偏低时才做，真·半透明的纱、蕾丝（上限本来就是 255）不受影响。
 *
 * **羽化**：把「模糊过的自身」当掩膜再乘一次 alpha，边缘处收一圈，抠图残留
 * 的那一线白边跟着被削掉。掩膜要先把内部顶实再模糊，否则本来就欠实的素材
 * 会被再乘薄一层 —— 这正是补偿之前那件衬衫透得更厉害的原因。
 */
function buildSprite(
  img: HTMLImageElement,
  layer: StageLayer,
  pw: number,
  ph: number,
  ratio: number,
  ceiling = 255,
): HTMLCanvasElement {
  const c = newCanvas(pw, ph);
  const ctx = c.getContext('2d')!;
  ctx.setTransform(ratio, 0, 0, ratio, 0, 0);
  ctx.imageSmoothingQuality = 'high';

  const passes = ceiling < 170 ? 3 : ceiling < 240 ? 2 : 1;
  for (let i = 0; i < passes; i++) {
    ctx.drawImage(img, layer.dx, layer.dy, layer.dw, layer.dh);
  }

  if (layer.feather > 0) {
    const mask = newCanvas(pw, ph);
    const mctx = mask.getContext('2d')!;
    mctx.setTransform(ratio, 0, 0, ratio, 0, 0);
    mctx.imageSmoothingQuality = 'high';
    // 顶实：内部 alpha 逼近 1，边缘那一圈仍然是渐变，羽化的手感不丢
    for (let i = 0; i < 3; i++) {
      mctx.drawImage(img, layer.dx, layer.dy, layer.dw, layer.dh);
    }
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.globalCompositeOperation = 'destination-in';
    ctx.filter = `blur(${(layer.feather * ratio).toFixed(2)}px)`;
    ctx.drawImage(mask, 0, 0);
    ctx.filter = 'none';
    ctx.globalCompositeOperation = 'source-over';
  }
  return c;
}

// ---------------------------------------------------------------- 裁切原语

/**
 * 一条竖直区间掩膜：[from, to] 之内不透明，之外透明，两端各留 feather 的软过渡。
 * 区间两端都没给就返回 null（= 不限制），调用方跳过这一步。
 * 传入的 y 与 feather 是显示像素，这里换算到设备像素。
 */
function bandStencil(
  pw: number,
  ph: number,
  from: number | undefined,
  to: number | undefined,
  feather: number,
  ratio: number,
): HTMLCanvasElement | null {
  if (from === undefined && to === undefined) return null;
  const c = newCanvas(pw, ph);
  const ctx = c.getContext('2d')!;
  const f = Math.max(feather * ratio, 1);
  const y0 = from === undefined ? -f : from * ratio;
  const y1 = to === undefined ? ph + f : to * ratio;
  if (y1 <= y0) return c; // 区间被压没了 —— 这一层整层不显示

  ctx.fillStyle = '#FFFFFF';
  const solidTop = Math.min(y0 + f, y1);
  const solidBottom = Math.max(y1 - f, y0);
  if (solidBottom > solidTop) ctx.fillRect(0, solidTop, pw, solidBottom - solidTop);

  if (from !== undefined && solidTop > y0) {
    const g = ctx.createLinearGradient(0, y0, 0, solidTop);
    g.addColorStop(0, 'rgba(255,255,255,0)');
    g.addColorStop(1, 'rgba(255,255,255,1)');
    ctx.fillStyle = g;
    ctx.fillRect(0, y0, pw, solidTop - y0);
  }
  if (to !== undefined && y1 > solidBottom) {
    const g = ctx.createLinearGradient(0, solidBottom, 0, y1);
    g.addColorStop(0, 'rgba(255,255,255,1)');
    g.addColorStop(1, 'rgba(255,255,255,0)');
    ctx.fillStyle = g;
    ctx.fillRect(0, solidBottom, pw, y1 - solidBottom);
  }
  return c;
}

/**
 * 身体遮罩掩膜：区间内按人体轮廓，区间外不裁（留满）。
 *
 * 「区间外不裁」这件事必须显式补回来，否则裙摆、大衣下摆会被整段切掉 ——
 * 那正是分层贴图看着最假的一种失败。
 */
function maskStencil(
  mask: HTMLCanvasElement,
  spec: { from?: number; to?: number; feather: number },
  pw: number,
  ph: number,
  ratio: number,
  width: number,
  height: number,
): HTMLCanvasElement {
  const c = newCanvas(pw, ph);
  const ctx = c.getContext('2d')!;
  ctx.setTransform(ratio, 0, 0, ratio, 0, 0);
  ctx.imageSmoothingQuality = 'high';
  ctx.drawImage(mask, 0, 0, width, height);
  ctx.setTransform(1, 0, 0, 1, 0, 0);

  const band = bandStencil(pw, ph, spec.from, spec.to, spec.feather, ratio);
  if (band) {
    ctx.globalCompositeOperation = 'destination-in';
    ctx.drawImage(band, 0, 0);
    ctx.globalCompositeOperation = 'source-over';
    ctx.drawImage(invert(band, pw, ph), 0, 0);
  }
  return c;
}

/** 反相一张掩膜的 alpha（1 - a），用来表达「这一段不裁」 */
function invert(stencil: HTMLCanvasElement, pw: number, ph: number): HTMLCanvasElement {
  const c = newCanvas(pw, ph);
  const ctx = c.getContext('2d')!;
  ctx.fillStyle = '#FFFFFF';
  ctx.fillRect(0, 0, pw, ph);
  ctx.globalCompositeOperation = 'destination-out';
  ctx.drawImage(stencil, 0, 0);
  return c;
}

/** 用掩膜乘一次 alpha（destination-in），掩膜之外的像素被裁掉 */
function applyStencil(sprite: HTMLCanvasElement, stencil: HTMLCanvasElement): void {
  const ctx = sprite.getContext('2d')!;
  ctx.setTransform(1, 0, 0, 1, 0, 0);
  ctx.filter = 'none';
  ctx.globalCompositeOperation = 'destination-in';
  ctx.drawImage(stencil, 0, 0);
  ctx.globalCompositeOperation = 'source-over';
}

/**
 * 成对挖除：用遮挡方的轮廓在被遮方身上挖一个洞。
 *
 * 先把遮挡方的轮廓向外推 grow —— 不推的话两层边缘完全重合，抠图各自的
 * 半透明过渡叠起来会留下一条一像素的亮缝，比不挖还显眼。
 */
function eraseWith(
  sprite: HTMLCanvasElement,
  occluder: HTMLCanvasElement,
  spec: { from?: number; to?: number; grow: number; feather: number },
  pw: number,
  ph: number,
  ratio: number,
): void {
  const eraser = newCanvas(pw, ph);
  const ectx = eraser.getContext('2d')!;
  ectx.drawImage(occluder, 0, 0);

  const g = spec.grow * ratio;
  if (g >= 0.5) {
    for (let i = 0; i < 8; i++) {
      const a = (i / 8) * Math.PI * 2;
      ectx.drawImage(occluder, Math.cos(a) * g, Math.sin(a) * g);
    }
  }

  const band = bandStencil(pw, ph, spec.from, spec.to, spec.feather, ratio);
  if (band) {
    ectx.globalCompositeOperation = 'destination-in';
    ectx.drawImage(band, 0, 0);
    ectx.globalCompositeOperation = 'source-over';
  }

  const ctx = sprite.getContext('2d')!;
  ctx.setTransform(1, 0, 0, 1, 0, 0);
  ctx.globalCompositeOperation = 'destination-out';
  const f = spec.feather * ratio;
  if (f >= 0.5) ctx.filter = `blur(${f.toFixed(2)}px)`;
  ctx.drawImage(eraser, 0, 0);
  ctx.filter = 'none';
  ctx.globalCompositeOperation = 'source-over';
}

/**
 * 接触阴影。两个分量叠在一起才像「压在身上」而不是「浮在上面」：
 *
 *   环境遮蔽  沿整圈轮廓的软暗环，不偏移 —— 负责让边界不再是刀切的
 *   方向投影  向下偏一点的实一些的影子 —— 负责给出「衣服有厚度」的暗示
 *
 * 两者都要裁进「已画内容」的轮廓，不裁的话影子会飘到背景上，比不加还假。
 * 阴影是从**裁完的**精灵图算的，所以被挖掉的部分不会留下没主的影子。
 */
function buildContactShadow(
  sprite: HTMLCanvasElement,
  accum: HTMLCanvasElement,
  layer: StageLayer,
  pw: number,
  ph: number,
  ratio: number,
): HTMLCanvasElement | null {
  const contact = layer.contact;
  if (!contact) return null;
  const c = newCanvas(pw, ph);
  const ctx = c.getContext('2d')!;

  const ambient = tinted(sprite, pw, ph, contact.blur * 1.8 * ratio, 0, contact.alpha * 0.7);
  const cast = tinted(sprite, pw, ph, contact.blur * ratio, contact.offset * ratio, contact.alpha);
  ctx.drawImage(ambient, 0, 0);
  ctx.drawImage(cast, 0, 0);

  // 只保留落在已画内容上的部分
  ctx.globalCompositeOperation = 'destination-in';
  ctx.drawImage(accum, 0, 0);
  ctx.globalCompositeOperation = 'source-over';
  return c;
}

/** 把一张精灵图变成「模糊 + 偏移 + 压暗」的单色影子 */
function tinted(
  sprite: HTMLCanvasElement,
  pw: number,
  ph: number,
  blur: number,
  offsetY: number,
  alpha: number,
): HTMLCanvasElement {
  const c = newCanvas(pw, ph);
  const ctx = c.getContext('2d')!;
  ctx.filter = `blur(${blur.toFixed(2)}px)`;
  ctx.drawImage(sprite, 0, offsetY);
  ctx.filter = 'none';
  ctx.globalCompositeOperation = 'source-in';
  ctx.fillStyle = `rgba(46,34,26,${alpha.toFixed(3)})`;
  ctx.fillRect(0, 0, pw, ph);
  return c;
}

function drawGroundShadow(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  const cx = width / 2;
  const cy = height * 0.978;
  const rx = width * 0.22;
  const ry = height * 0.012;
  const g = ctx.createRadialGradient(cx, cy, 0, cx, cy, rx);
  g.addColorStop(0, 'rgba(60,48,40,0.3)');
  g.addColorStop(0.6, 'rgba(60,48,40,0.12)');
  g.addColorStop(1, 'rgba(60,48,40,0)');
  ctx.save();
  ctx.translate(cx, cy);
  ctx.scale(1, ry / rx);
  ctx.fillStyle = g;
  ctx.beginPath();
  ctx.arc(0, 0, rx, 0, Math.PI * 2);
  ctx.fill();
  ctx.restore();
}

function newCanvas(w: number, h: number): HTMLCanvasElement {
  const c = document.createElement('canvas');
  c.width = w;
  c.height = h;
  return c;
}
