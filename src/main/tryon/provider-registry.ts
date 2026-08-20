import { AliyunProvider } from './aliyun';
import {
  type TryOnProviderId,
  type TryOnSettingsState,
} from '../../shared/tryon';
import { FalProvider } from './fal';
import { FashnProvider } from './fashn';
import type { CloudTryOnProvider, ProviderDependencies } from './provider';
import type { LoadedTryOnSettings } from './settings';

type Env = Record<string, string | undefined>;
const PROVIDER_IDS: TryOnProviderId[] = ['fashn', 'aliyun', 'fal'];

export function createProvider(
  env: Env = process.env,
  deps: ProviderDependencies = {},
  settings?: LoadedTryOnSettings,
): CloudTryOnProvider {
  const selected = selectedProvider(settings, env);
  if (selected === 'aliyun') {
    return new AliyunProvider({
      apiKey: settings?.apiKeys.aliyun ?? env.DASHSCOPE_API_KEY,
      ...deps,
    });
  }
  if (selected === 'fal') {
    return new FalProvider({ apiKey: settings?.apiKeys.fal ?? env.FAL_KEY, ...deps });
  }
  if (selected !== 'fashn') {
    return new FashnProvider({ apiKey: undefined, ...deps });
  }
  const model = env.PIXELFIT_VTON_MODEL === 'tryon-v1.6' ? 'tryon-v1.6' : 'tryon-max';
  const requestedMode = env.PIXELFIT_VTON_MODE;
  const mode = requestedMode === 'performance' || requestedMode === 'balanced' || requestedMode === 'quality'
    ? requestedMode
    : 'fast';
  const requestedResolution = env.PIXELFIT_VTON_RESOLUTION;
  const resolution = requestedResolution === '2k' || requestedResolution === '4k' ? requestedResolution : '1k';
  return new FashnProvider({
    apiKey: settings?.apiKeys.fashn ?? env.FASHN_API_KEY,
    model,
    mode,
    resolution,
    ...deps,
  });
}

export function createSettingsState(
  settings: LoadedTryOnSettings,
  env: Env = process.env,
): TryOnSettingsState {
  return {
    selectedProvider: selectedProvider(settings, env),
    providers: PROVIDER_IDS.map((id) => {
      const saved = Boolean(settings.apiKeys[id]?.trim());
      const fromEnvironment = Boolean(environmentKey(id, env)?.trim());
      const provider = createProvider(env, {}, {
        ...settings,
        selectedProvider: id,
        selectionSaved: true,
      });
      return {
        id,
        label: provider.profile.label,
        configured: saved || fromEnvironment,
        keySource: saved ? 'saved' : fromEnvironment ? 'environment' : 'none',
        profile: provider.profile,
        privacySummary: provider.privacySummary,
      };
    }),
  };
}

function environmentKey(id: TryOnProviderId, env: Env): string | undefined {
  if (id === 'fashn') return env.FASHN_API_KEY;
  if (id === 'aliyun') return env.DASHSCOPE_API_KEY;
  return env.FAL_KEY;
}

function selectedProvider(settings: LoadedTryOnSettings | undefined, env: Env): TryOnProviderId {
  if (settings && settings.selectionSaved !== false) return settings.selectedProvider;
  const fromEnvironment = env.PIXELFIT_VTON_PROVIDER?.trim().toLowerCase();
  return PROVIDER_IDS.includes(fromEnvironment as TryOnProviderId)
    ? fromEnvironment as TryOnProviderId
    : 'fashn';
}
