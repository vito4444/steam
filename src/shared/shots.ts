export interface ShotCheck {
  label: string;
  passed: boolean;
  detail?: string;
}

/** Legacy recipe keys resolve only to the eight retained CERE-25 demo assets. */
export const ASSET_ALIAS: Readonly<Record<string, string>> = {
  top_011: 'c10n_top_011',
  top_014: 'c10n_top_011',
  top_393: 'c10n_top_393',
  top_026: 'c10n_top_015',
  top_1209: 'c10n_top_015',
  top_009: 'c10n_top_011',
  bottom_102: 'c10n_bottom_102',
  bottom_4025: 'c10n_bottom_102',
  dress_1087: 'c10n_top_393',
  dress_2105: 'c10n_top_015',
  outerwear_075: 'c10n_top_015',
  shoes_318: 'c10n_shoes_2178',
  shoes_269: 'c10n_shoes_2178',
  shoes_268: 'c10n_shoes_2178',
  shoes_259: 'c10n_shoes_276',
  shoes_295: 'c10n_shoes_276',
  shoes_283: 'c10n_shoes_2178',
  shoes_276: 'c10n_shoes_276',
  shoes_2188: 'c10n_shoes_2178',
  bag_321: 'c10n_bag_382',
  bag_325: 'c10n_bag_382',
  bag_349: 'c10n_bag_382',
  accessory_401: 'c10n_accessory_433',
  accessory_428: 'c10n_accessory_433',
};

export const resolveRecipeAssetId = (id: string): string => ASSET_ALIAS[id] ?? id;

export const SHOT_RECIPES = {
  main: ['top_393', 'bottom_102', 'shoes_295', 'bag_325'],
  dressing: ['top_014', 'bottom_102', 'outerwear_075', 'accessory_401', 'shoes_268'],
  occlusion: ['top_014', 'outerwear_075', 'accessory_401', 'shoes_269'],
  compareFirst: ['top_393', 'bottom_102', 'shoes_295'],
  compareSecond: ['dress_1087', 'shoes_259', 'bag_321'],
  looksFirst: ['top_014', 'bottom_102', 'shoes_268'],
  looksSecond: ['dress_1087', 'shoes_259', 'bag_321'],
} as const;

export interface FigureRecipe {
  name: string;
  ids: readonly string[];
  tuck?: Readonly<Record<string, 'in' | 'out'>>;
  variants: readonly ('off' | 'on')[];
}

export const FIGURE_RECIPES: readonly FigureRecipe[] = [
  { name: 'sweater', ids: ['top_011', 'shoes_318'], variants: ['off', 'on'] },
  { name: 'jacket_scarf', ids: ['top_014', 'outerwear_075', 'accessory_401', 'shoes_269'], variants: ['off', 'on'] },
  { name: 'gown_boots', ids: ['dress_1087', 'shoes_259', 'bag_321'], variants: ['off', 'on'] },
  { name: 'knit_shorts', ids: ['top_393', 'bottom_102', 'shoes_295', 'bag_325'], variants: ['off', 'on'] },
  {
    name: 'tuck_out',
    ids: ['top_014', 'bottom_102', 'shoes_268'],
    tuck: { top_014: 'out' },
    variants: ['on'],
  },
  {
    name: 'tuck_in',
    ids: ['top_014', 'bottom_102', 'shoes_268'],
    tuck: { top_014: 'in' },
    variants: ['on'],
  },
  { name: 'limit_hanging', ids: ['top_026', 'bottom_102', 'shoes_295'], variants: ['on'] },
  { name: 'limit_packaged', ids: ['top_1209', 'bottom_4025', 'accessory_428', 'shoes_283'], variants: ['on'] },
  { name: 'limit_flatlay', ids: ['top_009', 'bottom_102', 'shoes_276'], variants: ['on'] },
  { name: 'limit_lace', ids: ['dress_2105', 'shoes_2188', 'bag_349'], variants: ['on'] },
];
