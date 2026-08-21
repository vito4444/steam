import { useEffect, useMemo, useRef, useState } from 'react';

import { estimateTryOn, planTryOn, type TryOnProviderStatus } from '@shared/tryon';
import type { WornEntry } from '@/state/store';
import { assetToTryOnGarment, categoryForTryOn } from '@/tryon/cloud';
import { IconSparkle, IconX } from '@/ui/icons';
import { captureBase } from './handles';

type Phase = 'idle' | 'preparing' | 'generating' | 'success' | 'error' | 'cancelling';

interface Props {
  status: TryOnProviderStatus | null;
  worn: WornEntry[];
  outfitKey: string;
  onResult: (imageDataUrl: string | null) => void;
}

export function CloudTryOnPanel({ status, worn, outfitKey, onResult }: Props) {
  const [phase, setPhase] = useState<Phase>('idle');
  const [message, setMessage] = useState('未生成时继续显示本地分层预览');
  const [consentOpen, setConsentOpen] = useState(false);
  const [consentChecked, setConsentChecked] = useState(false);
  const [sessionConsent, setSessionConsent] = useState(false);
  const [lastCached, setLastCached] = useState(false);
  const requestIdRef = useRef<string | null>(null);

  const active = useMemo(() => worn.filter((entry) => !entry.hidden), [worn]);
  const previewPlan = useMemo(() => {
    if (!status) return null;
    return planTryOn(active.map(({ asset }) => ({
      id: asset.id,
      name: asset.name,
      category: categoryForTryOn(asset.category),
      imageDataUrl: '',
    })), status.profile);
  }, [active, status]);
  const estimate = status && previewPlan ? estimateTryOn(previewPlan, status.profile) : null;
  const running = phase === 'preparing' || phase === 'generating' || phase === 'cancelling';

  useEffect(() => {
    if (requestIdRef.current) window.pixelfit.tryOn.cancel(requestIdRef.current);
    requestIdRef.current = null;
    setPhase('idle');
    setMessage('搭配已变化，需要重新生成');
    setLastCached(false);
    onResult(null);
  }, [outfitKey, onResult]);

  useEffect(() => {
    const target = window as unknown as { __pixelfitCloudConsent?: () => void };
    target.__pixelfitCloudConsent = () => setConsentOpen(true);
    return () => { delete target.__pixelfitCloudConsent; };
  }, []);

  const generate = async (confirmedConsent = sessionConsent) => {
    if (!status?.configured || !confirmedConsent || active.length === 0 || running) return;
    const clientRequestId = crypto.randomUUID();
    requestIdRef.current = clientRequestId;
    setPhase('preparing');
    setMessage('正在整理人物底图与衣物图片…');
    setLastCached(false);
    onResult(null);

    try {
      const baseImageDataUrl = await captureBase(0.75);
      if (!baseImageDataUrl) throw new Error('人物画布尚未准备好');
      const garments = await Promise.all(active.map(({ asset }) => assetToTryOnGarment(asset)));
      setPhase('generating');
      setMessage(`正在上传并生成 1/${garments.length}… 服务端会继续处理整套搭配`);
      const result = await window.pixelfit.tryOn.generate({
        clientRequestId,
        consent: true,
        baseImageDataUrl,
        garments,
      });
      if (requestIdRef.current !== clientRequestId) return;
      requestIdRef.current = null;
      if (!result.ok) {
        setPhase('error');
        setMessage(`${result.error}；本地分层预览仍可用`);
        return;
      }
      onResult(result.imageDataUrl);
      setLastCached(result.cached);
      setPhase('success');
      setMessage(result.cached
        ? '已命中本地缓存，本次没有调用云端、没有重复计费'
        : `生成完成 · ${(result.elapsedMs / 1000).toFixed(1)} 秒 · ${result.providerRequestIds.length} 次云端输出`);
    } catch (error) {
      requestIdRef.current = null;
      setPhase('error');
      setMessage(`${error instanceof Error ? error.message : String(error)}；本地分层预览仍可用`);
    }
  };

  const cancel = () => {
    if (!requestIdRef.current) return;
    window.pixelfit.tryOn.cancel(requestIdRef.current);
    setPhase('cancelling');
    setMessage('正在取消；人物画布继续显示本地预览');
  };

  const requestGeneration = () => {
    if (sessionConsent) void generate(true);
    else setConsentOpen(true);
  };

  return (
    <>
      <aside className={`cloud-tryon-panel ${phase}`}>
        <div className="cloud-tryon-head">
          <span className={`status-dot ${status?.configured ? 'ok' : 'warn'}`} />
          <strong>{status?.name ?? '正在读取云端配置…'}</strong>
          {lastCached && <span className="tag ok">缓存</span>}
        </div>

        <div className="cloud-tryon-meta">
          {estimate ? (
            <>
              <span>{estimate.billableImages} 张输出</span>
              <span>{estimate.cny !== undefined
                ? '约 ¥' + estimate.cny.toFixed(2) + '（US$' + estimate.usd.toFixed(3) + '）'
                : '约 US$' + estimate.usd.toFixed(3)}</span>
              <span>约 {estimate.typicalSeconds.min === estimate.typicalSeconds.max
                ? `${estimate.typicalSeconds.min}s`
                : `${estimate.typicalSeconds.min}–${estimate.typicalSeconds.max}s`}</span>
            </>
          ) : <span>读取估算中</span>}
        </div>

        {previewPlan && previewPlan.unsupported.length > 0 && (
          <p className="cloud-warning">
            当前 provider 不支持：{previewPlan.unsupported.map((item) => item.name).join('、')}，不会上传或计费。
          </p>
        )}
        <p className="cloud-message">{status?.configured ? message : status?.missingConfiguration ?? message}</p>

        <div className="cloud-actions">
          {running ? (
            <button className="btn sm ghost" onClick={cancel} disabled={phase === 'cancelling'}>
              <IconX size={13} />
              {phase === 'cancelling' ? '取消中' : '取消生成'}
            </button>
          ) : (
            <button
              className="btn sm primary"
              disabled={!status?.configured || active.length === 0}
              onClick={requestGeneration}
            >
              <IconSparkle size={13} />
              {phase === 'error' ? '重试高清试穿' : '生成高清试穿'}
            </button>
          )}
          <button className="btn sm ghost" onClick={() => setConsentOpen(true)}>上传说明</button>
          {phase === 'success' && (
            <button
              className="btn sm ghost"
              onClick={() => { onResult(null); setPhase('idle'); setMessage('已切回本地分层预览'); }}
            >
              看本地预览
            </button>
          )}
        </div>
      </aside>

      {consentOpen && (
        <div className="modal-backdrop cloud-consent" onClick={() => setConsentOpen(false)}>
          <div
            className="modal"
            role="dialog"
            aria-modal="true"
            aria-labelledby="cloud-consent-title"
            onClick={(event) => event.stopPropagation()}
          >
            <div className="consent-kicker">上传前确认</div>
            <h3 id="cloud-consent-title">AI 试穿会把照片发送给 {status?.name ?? '云端服务商'}</h3>
            <p className="consent-copy">
              仅在你点“同意并生成”后，人物底图和本套支持的衣物图才会上传。
              {status?.privacySummary} PixelFit 会把成功结果缓存在本机，避免同一套搭配重复计费。
            </p>
            <ul className="consent-list">
              <li>请确认你拥有照片和衣物图的使用权，并已取得可识别人物的必要同意。</li>
              <li>生成图可能改变细节；文字、logo、叠穿关系需要人工复核。</li>
              <li>关闭、超时、断网或失败时，应用保留本地分层预览，不会白屏。</li>
            </ul>
            <label className="consent-check">
              <input
                type="checkbox"
                checked={consentChecked}
                onChange={(event) => setConsentChecked(event.target.checked)}
              />
              <span>我已阅读并同意本次上传</span>
            </label>
            <div className="row" style={{ justifyContent: 'flex-end' }}>
              <button className="btn ghost" onClick={() => setConsentOpen(false)}>取消</button>
              <button
                className="btn primary"
                disabled={!consentChecked || !status?.configured || active.length === 0}
                onClick={() => {
                  setSessionConsent(true);
                  setConsentOpen(false);
                  void generate(true);
                }}
              >
                同意并生成
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  );
}
