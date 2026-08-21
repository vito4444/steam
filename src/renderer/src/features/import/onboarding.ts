export interface StorageLike {
  getItem(key: string): string | null;
  setItem(key: string, value: string): void;
}

export const ONBOARDING_KEY = 'pixelfit:onboarding:import-first:v2';

export const onboardingPending = (storage: StorageLike) =>
  storage.getItem(ONBOARDING_KEY) !== 'complete';

export const completeOnboarding = (storage: StorageLike) => {
  storage.setItem(ONBOARDING_KEY, 'complete');
};
