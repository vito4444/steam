import { describe, expect, it } from 'vitest';

import { defaultTuck } from '../src/shared/occlusion';

describe('defaultTuck photo priors', () => {
  it('tucks an unclassified photo top so the paired bottom remains visible', () => {
    expect(defaultTuck({}, 'top', true)).toBe('in');
  });

  it('keeps an explicitly hip-length photo top out', () => {
    expect(defaultTuck({ length: 'hip' }, 'top', true)).toBe('out');
  });

  it('never applies the photo-top prior to an outer layer', () => {
    expect(defaultTuck({}, 'outer', true)).toBe('out');
  });
});
