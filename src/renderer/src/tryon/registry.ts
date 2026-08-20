import { GenerativeTryOn } from './generative';
import { LayeredTryOn } from './layered';
import type { TryOnEngine } from './types';

export type EngineId = 'layered' | 'vton';

export const ENGINE_IDS: EngineId[] = ['layered', 'vton'];

export function createEngine(id: EngineId): TryOnEngine {
  return id === 'vton' ? new GenerativeTryOn() : new LayeredTryOn();
}

/** 界面用的引擎清单（每次现建一个实例只为读元信息，不 mount） */
export function engineCatalog(): { id: EngineId; engine: TryOnEngine }[] {
  return ENGINE_IDS.map((id) => ({ id, engine: createEngine(id) }));
}
