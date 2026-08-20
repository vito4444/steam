import { useCallback, useEffect, useState } from 'react';

import { useStore, type View } from '@/state/store';
import { WardrobePanel } from '@/features/wardrobe/WardrobePanel';
import { StageView } from '@/features/stage/StageView';
import { OutfitPanel } from '@/features/dressing/OutfitPanel';
import { BaseView, ImportView, LooksView, SettingsView } from '@/features/views/Views';
import { BoardView } from '@/features/board/BoardView';
import {
  IconBoard, IconBody, IconImport, IconLooks, IconMinus, IconSettings, IconSquare,
  IconWardrobe, IconX,
} from '@/ui/icons';
import { registerShotHook } from '@/shots';
import {
  completeOnboarding,
  onboardingPending,
} from '@/features/import/onboarding';

const RAIL: { key: View; label: string; Icon: (p: { size?: number }) => JSX.Element }[] = [
  { key: 'wardrobe', label: '模特', Icon: IconWardrobe },
  { key: 'board', label: '画板', Icon: IconBoard },
  { key: 'import', label: '导入', Icon: IconImport },
  { key: 'looks', label: 'Look', Icon: IconLooks },
  { key: 'base', label: '基底', Icon: IconBody },
  { key: 'settings', label: '设置', Icon: IconSettings },
];

export default function App() {
  const store = useStore();
  const { ready, view, setView, assets, looks, toast } = store;
  const [onboarding, setOnboarding] = useState(() => {
    try {
      return onboardingPending(window.localStorage);
    } catch {
      return false;
    }
  });

  const finishOnboarding = useCallback(() => {
    try {
      completeOnboarding(window.localStorage);
    } catch {
      // Keep the current session usable when storage is unavailable.
    }
    setOnboarding(false);
  }, []);

  useEffect(() => registerShotHook(store), [store]);

  useEffect(() => {
    if (ready && onboarding) setView('import');
  }, [onboarding, ready, setView]);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
        e.preventDefault();
        setView('wardrobe');
        (document.querySelector('.search input') as HTMLInputElement | null)?.focus();
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [setView]);

  return (
    <div className="shell">
      <header className="titlebar">
        <div className="brand">
          <span className="brand-mark">P</span>
          PIXELFIT
        </div>
        <span className="meta">
          {ready ? `${assets.length} 件素材 · ${looks.length} 套 Look` : '正在载入素材库…'}
        </span>
        <div className="spacer" />
        <div className="win-controls">
          <button className="win-btn" onClick={() => window.pixelfit.window.minimize()}>
            <IconMinus size={15} />
          </button>
          <button className="win-btn" onClick={() => window.pixelfit.window.toggleMaximize()}>
            <IconSquare size={14} />
          </button>
          <button className="win-btn close" onClick={() => window.pixelfit.window.close()}>
            <IconX size={15} />
          </button>
        </div>
      </header>

      <div className="body">
        <nav className="rail">
          {RAIL.map(({ key, label, Icon }) => (
            <button
              key={key}
              className={`rail-btn${view === key ? ' active' : ''}`}
              onClick={() => setView(key)}
              title={label}
            >
              <Icon size={19} />
              <span>{label}</span>
            </button>
          ))}
        </nav>

        {view === 'wardrobe' ? (
          <>
            <WardrobePanel />
            <StageView />
            <OutfitPanel />
          </>
        ) : view === 'board' ? (
          <BoardView />
        ) : view === 'looks' ? (
          <LooksView />
        ) : view === 'import' ? (
          <ImportView
            onboarding={onboarding}
            onOnboardingComplete={finishOnboarding}
          />
        ) : view === 'base' ? (
          <BaseView />
        ) : (
          <SettingsView />
        )}
      </div>

      {toast && <div className="toast">{toast}</div>}
    </div>
  );
}
