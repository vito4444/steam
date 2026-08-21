import { useEffect, useMemo, useRef, useState } from 'react';

import { BODY_LABEL, BODY_TYPES } from '@shared/spec';
import { OCCASIONS, occasionLabel } from '@shared/board';
import type { LinkImportResult, PhotoImportResult } from '@shared/ipc';
import type {
  TryOnProviderId,
  TryOnProviderStatus,
  TryOnSettingsState,
} from '@shared/tryon';
import { toneSwatches, useStore } from '@/state/store';
import { boardHandles } from '@/features/board/boardStore';
import { engineCatalog, type EngineId } from '@/tryon/registry';
import {
  errorMessage,
  linkFeedback,
  manualFeedback,
  photoFeedback,
  type ImportFeedback,
} from '@/features/import/importFeedback';
import {
  IconBoard, IconFolder, IconImport, IconLooks, IconRefresh, IconSparkle, IconTrash,
} from '@/ui/icons';

type LookKind = 'all' | 'board' | 'model';

/**
 * Look 库。
 *
 * 一张卡片就是一套搭配的成图：画板 Look 的封面是拼贴图本身（CERE-21），
 * 模特 Look 的封面是穿好的模特图。场景标签用来筛选 —— 需求里的
 * 工作 / 休闲 / 约会 / 运动 / 度假 / 旅游 / 居家七类。
 */
export function LooksView() {
  const { looks, applyLook, deleteLook, setView, notify } = useStore();
  const [kind, setKind] = useState<LookKind>('all');
  const [occasion, setOccasion] = useState<string | null>(null);

  const counts = useMemo(() => {
    const out: Record<string, number> = {};
    for (const l of looks) for (const o of l.occasion ?? []) out[o] = (out[o] ?? 0) + 1;
    return out;
  }, [looks]);

  const filtered = useMemo(() => looks.filter((l) => {
    const isBoard = !!l.board;
    if (kind === 'board' && !isBoard) return false;
    if (kind === 'model' && isBoard) return false;
    if (occasion && !(l.occasion ?? []).includes(occasion)) return false;
    return true;
  }), [looks, kind, occasion]);

  const open = (id: string) => {
    const look = looks.find((l) => l.id === id);
    if (!look) return;
    if (look.board && boardHandles.load) {
      boardHandles.load(look.board);
      setView('board');
      notify(`已在画板打开「${look.name}」`);
      return;
    }
    applyLook(look);
  };

  if (looks.length === 0) {
    return (
      <div className="view">
        <div className="view-head">
          <h2>我的 Look</h2>
          <p>保存下来的搭配会出现在这里：画板 Look 点开回到画板继续改，模特 Look 点开整套穿回模特身上。</p>
        </div>
        <div className="empty" style={{ minHeight: 320 }}>
          <div className="empty-art">
            <IconLooks size={32} />
          </div>
          <h3>还没有保存的搭配</h3>
          <p>去画板摆一套，点右侧「保存为 Look」；或在模特视图穿一套后保存。</p>
        </div>
      </div>
    );
  }

  return (
    <div className="view">
      <div className="view-head">
        <h2>我的 Look</h2>
        <p>
          Look 存的是素材引用与版面参数，不是压平的图片，所以以后重新修图或替换素材，
          所有搭配会自动跟着更新。
        </p>
      </div>

      <div className="filter-row" style={{ padding: '0 0 4px' }}>
        <div className="seg">
          {([['all', '全部'], ['board', '画板'], ['model', '模特']] as [LookKind, string][]).map(
            ([k, label]) => (
              <button key={k} className={kind === k ? 'active' : ''} onClick={() => setKind(k)}>
                {label}
              </button>
            ),
          )}
        </div>
        <button
          className={`chip${occasion === null ? ' active' : ''}`}
          onClick={() => setOccasion(null)}
        >
          不限场景
        </button>
        {OCCASIONS.map((o) => (
          <button
            key={o.key}
            className={`chip${occasion === o.key ? ' active' : ''}`}
            onClick={() => setOccasion((cur) => (cur === o.key ? null : o.key))}
          >
            {o.label}
            {counts[o.key] ? ` ${counts[o.key]}` : ''}
          </button>
        ))}
      </div>

      {filtered.length === 0 ? (
        <div className="empty" style={{ minHeight: 240 }}>
          <div className="empty-art">
            <IconLooks size={30} />
          </div>
          <h3>这个筛选下还没有搭配</h3>
          <p>换个场景标签，或者去画板摆一套新的。</p>
        </div>
      ) : (
        <div className="look-grid">
          {filtered.map((l) => (
            <div key={l.id} className="look-card">
              <button
                className="look-cover"
                style={{ width: '100%', display: 'block' }}
                onClick={() => open(l.id)}
                title={l.board ? '在画板中打开' : '穿回模特身上'}
              >
                {l.coverUrl ? <img src={l.coverUrl} alt={l.name} /> : <IconSparkle size={28} />}
              </button>
              <div className="look-meta">
                <div className="name">{l.name}</div>
                <div className="sub" style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                  <span>
                    {Object.values(l.slots).filter(Boolean).length} 件
                    {l.board ? '' : ` · ${BODY_LABEL[l.base.body]}`}
                  </span>
                  <span style={{ flex: 1 }} />
                  <button className="icon-btn" title="删除" onClick={() => void deleteLook(l.id)}>
                    <IconTrash size={13} />
                  </button>
                </div>
                <div className="look-tags">
                  <span className="look-tag kind">
                    {l.board ? <IconBoard size={10} /> : null}
                    {l.board ? ' 画板' : '模特'}
                  </span>
                  {(l.occasion ?? []).map((o) => (
                    <span key={o} className="look-tag">{occasionLabel(o)}</span>
                  ))}
                </div>
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

interface ImportViewProps {
  onboarding: boolean;
  onOnboardingComplete: () => void;
}

export function ImportView({ onboarding, onOnboardingComplete }: ImportViewProps) {
  const { pipeline, importFiles, refresh, notify, setView } = useStore();
  const [linkUrl, setLinkUrl] = useState('');
  const [busy, setBusy] = useState(false);
  const [feedback, setFeedback] = useState<ImportFeedback | null>(null);
  const linkInputRef = useRef<HTMLInputElement>(null);
  const manualDropzoneRef = useRef<HTMLDivElement>(null);

  const showFeedback = (next: ImportFeedback) => {
    setFeedback(next);
    notify(next.message);
    if (next.canOpenWardrobe) onOnboardingComplete();
  };

  const showRefreshFailure = (
    accepted: ImportFeedback,
    imported: number,
  ) => {
    showFeedback({
      ...accepted,
      tone: 'warning',
      message: `已导入的 ${imported} 件素材已入库，但列表刷新失败，请重启应用后查看。${accepted.repair ? ` ${accepted.message}` : ''}`,
      canOpenWardrobe: false,
    });
  };

  const focusLinkInput = () => {
    linkInputRef.current?.scrollIntoView({ behavior: 'smooth', block: 'center' });
    linkInputRef.current?.focus();
  };

  const focusManualRepair = () => {
    manualDropzoneRef.current?.scrollIntoView({ behavior: 'smooth', block: 'center' });
    manualDropzoneRef.current?.focus();
  };

  const importPhotos = async () => {
    setBusy(true);
    setFeedback(null);
    try {
      let result: PhotoImportResult;
      try {
        result = await window.pixelfit.pipeline.importPhotos();
      } catch (error) {
        showFeedback(photoFeedback(new Error(errorMessage(error))));
        return;
      }

      const accepted = photoFeedback(result);
      showFeedback(accepted);
      if (result.imported > 0) {
        try {
          await refresh();
        } catch {
          showRefreshFailure(accepted, result.imported);
        }
      }
    } finally {
      setBusy(false);
    }
  };

  const importLink = async () => {
    if (!linkUrl.trim()) {
      setFeedback({
        tone: 'warning',
        message: '请先粘贴一个商品链接。',
        repair: false,
        canOpenWardrobe: false,
      });
      focusLinkInput();
      return;
    }
    setBusy(true);
    setFeedback(null);
    try {
      let result: LinkImportResult;
      try {
        result = await window.pixelfit.link.import(linkUrl.trim());
      } catch (error) {
        showFeedback(linkFeedback(new Error(errorMessage(error))));
        return;
      }

      const accepted = linkFeedback(result);
      showFeedback(accepted);
      if (result.imported > 0) {
        try {
          await refresh();
        } catch {
          showRefreshFailure(accepted, result.imported);
        }
      }
    } finally {
      setBusy(false);
    }
  };

  const importManualFiles = async () => {
    setBusy(true);
    setFeedback(null);
    try {
      const outcome = await importFiles();
      showFeedback(manualFeedback(outcome));
    } catch (error) {
      showFeedback({
        tone: 'error',
        message: errorMessage(error),
        repair: true,
        canOpenWardrobe: false,
      });
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="view">
      {onboarding ? (
        <section className="import-hero" data-testid="first-run-guide">
          <div className="import-hero-copy">
            <span className="import-kicker">建立你的个人衣橱</span>
            <h1>先把你真的会穿的衣服放进来</h1>
            <p>
              选一张衣物清晰的照片，PixelFit 会在本地预处理并通过质量门检查；
              只有通过的单品才会进入衣橱。
            </p>
            <div className="import-choice-grid">
              <button
                className="btn primary"
                disabled={busy || !pipeline?.automatic}
                onClick={() => void importPhotos()}
              >
                <IconImport size={15} />
                拍照 / 选图导入
              </button>
              <button className="btn ghost" disabled={busy} onClick={focusLinkInput}>
                <IconFolder size={15} />
                粘贴商品链接
              </button>
            </div>
            <button
              className="import-skip"
              onClick={() => {
                onOnboardingComplete();
                setView('wardrobe');
              }}
            >
              先看看示例
            </button>
            <p className="import-demo-note">内置的 8 件衣物只是示例素材，不会冒充你的个人衣橱。</p>
          </div>
          <ol className="import-register">
            <li><span>01</span>选择衣物清晰的照片</li>
            <li><span>02</span>在本地完成预处理与质量检查</li>
            <li><span>03</span>通过检查的单品进入衣橱</li>
          </ol>
        </section>
      ) : null}

      <div className="view-head">
        <h2>导入素材</h2>
        <p>
          选择照片后，本地管线会预处理并执行质量检查。通过的单品进入衣橱；
          未通过的内容保持在衣橱之外，可在外部修补后以透明 PNG/WebP 重新导入。
        </p>
      </div>

      {feedback ? (
        <div
          className={`import-feedback ${feedback.tone}`}
          data-testid="import-feedback"
          aria-live="polite"
        >
          <div>
            <strong>{feedback.tone === 'success' ? '导入完成' : '导入提示'}</strong>
            <p>{feedback.message}</p>
          </div>
          <div className="row">
            {feedback.repair ? (
              <button
                className="btn ghost"
                data-testid="manual-repair"
                onClick={focusManualRepair}
              >
                去手动修补导入
              </button>
            ) : null}
            {feedback.canOpenWardrobe ? (
              <button className="btn primary" onClick={() => setView('wardrobe')}>
                去衣橱试穿
              </button>
            ) : null}
          </div>
        </div>
      ) : null}

      <div className="info-card">
        <h4>
          <span className={`status-dot ${pipeline?.installed ? 'ok' : 'warn'}`} />
          照片识别管线
        </h4>
        <p>{pipeline?.message ?? '正在检测…'}</p>
        <div className="row" style={{ marginTop: 12 }}>
          <button
            className="btn primary"
            disabled={busy || !pipeline?.automatic}
            onClick={() => void importPhotos()}
          >
            <IconImport size={14} />
            选择照片并自动识别
          </button>
          <span className="tag ok">CPU-only · 不上传</span>
        </div>
        <div className="steps">
          <div className="step">
            <div className="n">1</div>
            <div className="t">选择照片</div>
            <div className="d">从本机选择已经拍好的衣物照片。</div>
          </div>
          <div className="step">
            <div className="n">2</div>
            <div className="t">本地预处理</div>
            <div className="d">在设备上识别衣物并生成候选素材。</div>
          </div>
          <div className="step">
            <div className="n">3</div>
            <div className="t">质量检查</div>
            <div className="d">未通过的候选不会静默进入衣橱。</div>
          </div>
          <div className="step done">
            <div className="n">4</div>
            <div className="t">入衣橱</div>
            <div className="d">只有通过质量门的单品才会完成入库。</div>
          </div>
        </div>
      </div>

      <div className="info-card">
        <h4>从商品链接导入</h4>
        <p>
          自动解析公开商品页的主图、标题与价格，再交给同一套本地抠图质量门。
          淘宝、京东等登录墙不会绕过，会明确回落到“保存主图后手动导入”。
        </p>
        <div className="row" style={{ marginTop: 12 }}>
          <input
            ref={linkInputRef}
            className="import-link-input"
            type="url"
            aria-label="商品链接"
            value={linkUrl}
            placeholder="粘贴淘宝 / 京东 / 网易严选 / Shopify 商品链接"
            onChange={(event) => setLinkUrl(event.target.value)}
            onKeyDown={(event) => {
              if (event.key === 'Enter') void importLink();
            }}
          />
          <button className="btn primary" disabled={busy} onClick={() => void importLink()}>
            {busy ? '处理中…' : '解析并导入'}
          </button>
        </div>
      </div>

      <div
        ref={manualDropzoneRef}
        className="dropzone"
        data-testid="manual-dropzone"
        tabIndex={-1}
      >
        <div className="empty-art">
          <IconImport size={30} />
        </div>
        <h3 style={{ fontSize: 14, fontWeight: 600 }}>手动导入透明 PNG/WebP</h3>
        <p style={{ color: 'var(--text-3)', fontSize: 12.5, maxWidth: 460, lineHeight: 1.7 }}>
          自动链路拒绝的内容不会进入衣橱。请先在外部工具中修补为透明 PNG/WebP，
          再回到这里选择文件导入。
        </p>
        <button className="btn primary" disabled={busy} onClick={() => void importManualFiles()}>
          选择文件
        </button>
      </div>
    </div>
  );
}

export function BaseView() {
  const { outfit, dispatch, bases } = useStore();
  const base = bases[outfit.body];
  const swatches = toneSwatches(base);

  return (
    <div className="view">
      <div className="view-head">
        <h2>模特基底</h2>
        <p>
          基底素材由应用提供，不来自用户照片。内置 F02 / M02 两套写实模特，
          每套独立读取人体遮罩与锚点，默认使用 F02。
        </p>
      </div>

      <div className="info-card">
        <h4>
          <span className={`status-dot ${base?.source === 'library' ? 'ok' : 'warn'}`} />
          当前底图：{base?.pack ?? '—'}（{base?.source === 'library' ? '素材库' : '内置'}）
        </h4>
        <p>
          画布 {base?.canvas.w} × {base?.canvas.h}，肤色 {swatches.length} 档。
          画布尺寸与锚点全部读自底图包的 <code>manifest.json</code>，
          换一版底图不需要改代码 —— 把底图包解压到
          <code>library/base/&lt;体型&gt;/</code> 即可自动接管。
        </p>
        <ul>
          <li><code>canvas</code>：底图画布尺寸，衣物贴合的坐标系。</li>
          <li><code>anchors</code>：肩 / 胸 / 腰 / 胯 / 膝 / 踝等锚点，衣物按它对位。</li>
          <li><code>tones</code>：每档肤色一套烘焙好的图层；只有一档也合法。</li>
        </ul>
        <p>
          衣物的缩放不读这份 manifest，而是实测底图轮廓：在锚点所在的行上量身体
          有多宽，再按版型宽松量把衣服缩到那个宽度。所以底图换成真人照片后，
          衣物尺寸会自动跟着人体走。
        </p>
      </div>

      <div className="info-card">
        <h4>体型与肤色</h4>
        <div className="row" style={{ marginTop: 8 }}>
          <div className="seg">
            {BODY_TYPES.map((b) => (
              <button
                key={b}
                className={outfit.body === b ? 'active' : ''}
                onClick={() => dispatch({ type: 'set', patch: { body: b } })}
              >
                {BODY_LABEL[b]}
              </button>
            ))}
          </div>
          <div className="filter-row" style={{ padding: 0 }}>
            {swatches.map((t, i) => (
              <button
                key={t + i}
                title={base?.tones[i]?.name ?? `肤色 ${i + 1}`}
                className={`swatch${outfit.skin === i + 1 ? ' active' : ''}`}
                style={{ background: t }}
                onClick={() => dispatch({ type: 'set', patch: { skin: i + 1 } })}
              />
            ))}
          </div>
        </div>
      </div>
    </div>
  );
}

export function SettingsView() {
  const { root, refresh, notify, assets, looks } = useStore();
  const [busy, setBusy] = useState(false);

  const backup = async () => {
    setBusy(true);
    const res = await window.pixelfit.library.backup();
    setBusy(false);
    notify(res.ok ? `已备份 ${res.files} 个文件` : `备份失败：${res.error}`);
  };

  return (
    <div className="view">
      <div className="view-head">
        <h2>设置</h2>
        <p>素材库以文件为唯一真相：备份就是复制目录，迁移就是拷走目录。</p>
      </div>

      <div className="info-card">
        <h4>
          <IconFolder size={15} />
          素材库位置
        </h4>
        <p>
          <code>{root || '…'}</code>
        </p>
        <p style={{ marginTop: 6 }}>
          当前 {assets.length} 件素材、{looks.length} 套 Look。
          每件素材是一个目录（<code>meta.json</code> + <code>cutout.png</code> + <code>thumb.png</code>），
          哪天应用不在了，剩下的仍然是一堆能直接打开的图和 JSON。
        </p>
        <div className="row" style={{ marginTop: 12 }}>
          <button className="btn sm" onClick={() => void window.pixelfit.library.revealRoot()}>
            在资源管理器中打开
          </button>
          <button className="btn sm" disabled={busy} onClick={() => void backup()}>
            立即备份
          </button>
          <button
            className="btn sm ghost"
            onClick={async () => {
              const r = await window.pixelfit.library.importPack();
              if (r.canceled) return;
              await refresh();
              notify(
                r.failed
                  ? `导入 ${r.imported} 件，${r.failed} 件失败`
                  : `已从「${r.pack}」导入 ${r.imported} 件`,
              );
            }}
          >
            <IconRefresh size={14} />
            导入素材包
          </button>
        </div>
      </div>

      <EnginePicker />

      <div className="info-card">
        <h4>素材包</h4>
        <p>
          应用不再自带任何手绘占位单品 —— 衣橱里只应该出现真实照片素材。
          素材包是一个目录：<code>index.json</code> 加若干透明底 PNG，
          导入时按 alpha 包围盒裁剪、生成缩略图，不做任何风格化处理。
          格式见交付文档 <code>docs/asset-contract.md</code>。
        </p>
      </div>
    </div>
  );
}

/**
 * 渲染引擎选择。
 *
 * 分层贴图有天花板（肩宽 / 透视 / 褶皱对不齐时的贴纸感），CERE-8 正在验证
 * 生成式试穿。渲染是可替换的一层，这里就是那个替换开关；没接入的引擎
 * 如实写明未接入与回落行为，不做成灰按钮了事。
 */
function EnginePicker() {
  const { engineId, setEngineId } = useStore();
  const catalog = engineCatalog();
  const [provider, setProvider] = useState<TryOnProviderStatus | null>(null);
  const [settings, setSettings] = useState<TryOnSettingsState | null>(null);
  const [selectedProvider, setSelectedProvider] = useState<TryOnProviderId>('fashn');
  const [apiKey, setApiKey] = useState('');
  const [saving, setSaving] = useState(false);
  const [saveMessage, setSaveMessage] = useState('');

  useEffect(() => {
    void Promise.all([
      window.pixelfit.tryOn.status(),
      window.pixelfit.tryOn.settings(),
    ]).then(([nextProvider, nextSettings]) => {
      setProvider(nextProvider);
      setSettings(nextSettings);
      setSelectedProvider(nextSettings.selectedProvider);
    });
  }, []);

  const saveProvider = async (clearApiKey = false) => {
    setSaving(true);
    setSaveMessage('');
    try {
      const next = await window.pixelfit.tryOn.saveSettings({
        selectedProvider,
        apiKey: clearApiKey ? undefined : apiKey || undefined,
        clearApiKey,
      });
      setSettings(next);
      setProvider(await window.pixelfit.tryOn.status());
      setApiKey('');
      setSaveMessage(clearApiKey ? '已清除本机保存的密钥' : '供应商设置已保存');
    } catch (error) {
      setSaveMessage('保存失败：' + (error instanceof Error ? error.message : String(error)));
    } finally {
      setSaving(false);
    }
  };

  const selectedOption = settings?.providers.find((item) => item.id === selectedProvider);

  return (
    <div className="info-card">
      <h4>换装渲染引擎</h4>
      <p>
        界面只依赖引擎接口，不依赖某一种渲染方式。换引擎不影响衣橱数据与 Look。
      </p>
      {catalog.map(({ id, engine }) => {
        const st = id === 'vton'
          ? {
              available: provider?.configured ?? false,
              reason: provider
                ? provider.configured
                  ? `${provider.name} 已配置；仅在明确点击生成后上传`
                  : `${provider.name}：${provider.missingConfiguration}`
                : '正在读取云端配置…',
            }
          : engine.status();
        return (
          <div
            key={id}
            className={`engine-row${engineId === id ? ' active' : ''}`}
            onClick={() => setEngineId(id as EngineId)}
          >
            <div style={{ flex: 1 }}>
              <div className="name">{engine.name}</div>
              <div className="desc">
                {engine.description}
                {st.reason ? ` — ${st.reason}` : ''}
              </div>
            </div>
            <span className={`tag${st.available ? ' ok' : ''}`}>
              {st.available ? '可用' : '未接入'}
            </span>
          </div>
        );
      })}
      <div className="provider-config" aria-label="云试穿供应商设置">
        <div className="provider-config-head">
          <div>
            <strong>云端供应商</strong>
            <p>密钥用 Windows 安全存储加密，界面只显示是否已配置，不回显明文。</p>
          </div>
          <span className={`tag${selectedOption?.configured ? ' ok' : ''}`}>
            {selectedOption?.configured ? '密钥就绪' : '需要 Key'}
          </span>
        </div>
        <div className="provider-controls">
          <label>
            <span>服务商</span>
            <select
              value={selectedProvider}
              onChange={(event) => {
                setSelectedProvider(event.target.value as TryOnProviderId);
                setApiKey('');
                setSaveMessage('');
              }}
            >
              {settings?.providers.map((item) => (
                <option key={item.id} value={item.id}>{item.label}</option>
              ))}
            </select>
          </label>
          <label className="provider-key">
            <span>自备 API Key</span>
            <input
              type="password"
              value={apiKey}
              maxLength={512}
              autoComplete="off"
              spellCheck={false}
              placeholder={selectedOption?.keySource === 'saved'
                ? '已安全保存；输入新 Key 可覆盖'
                : selectedOption?.keySource === 'environment'
                  ? '当前来自环境变量；输入可覆盖'
                  : '粘贴 Key（保存后立即清空）'}
              onChange={(event) => setApiKey(event.target.value)}
            />
          </label>
        </div>
        {selectedOption && (
          <p className="provider-capability">
            支持 {selectedOption.profile.supportedCategories.join(' / ')} ·
            {selectedOption.profile.billingUnit === 'outfit' ? ' 按整套输出计费' : ' 按单品步骤计费'} ·
            {selectedOption.profile.cnyPerStep !== undefined
              ? ` 约 ¥${selectedOption.profile.cnyPerStep.toFixed(2)}/张`
              : ` 约 US$${selectedOption.profile.usdPerStep.toFixed(3)}/步`}
          </p>
        )}
        <div className="row provider-actions">
          <button
            className="btn sm primary"
            disabled={saving || !settings}
            onClick={() => void saveProvider(false)}
          >
            {saving ? '保存中…' : '保存并切换'}
          </button>
          <button
            className="btn sm ghost"
            disabled={saving || selectedOption?.keySource !== 'saved'}
            onClick={() => void saveProvider(true)}
          >
            清除本机 Key
          </button>
          <span className="provider-save-message" role="status" aria-live="polite">{saveMessage}</span>
        </div>
      </div>
      {selectedOption && (
        <div className="provider-facts">
          <span>默认不上传</span>
          <span>{selectedOption.profile.billingUnit === 'outfit' ? '按整套输出计费' : '按单品计费'}</span>
          <span>成功结果本地缓存</span>
          <p>{selectedOption.privacySummary}</p>
        </div>
      )}
    </div>
  );
}
