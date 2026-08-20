/**
 * 画板 Look 的保存。
 *
 * 复用现有的 Look 存储（`looks:save`），不另立一套：
 *   - `board` 挂上画板布局，`kind: 'board'` 标明这套是画板 Look；
 *   - `slots` 照样按槽位填上素材 id —— 这样一套画板 Look 也能被模特视图载入试穿，
 *     两个视图看的是同一套搭配的两个面。
 *   - 封面是画板成图本身（缩到 0.32 倍），Look 库里那张卡片就是拼贴图。
 */

import type { Asset, Look } from '@shared/types';
import type { BoardLook } from '@shared/board';
import type { Slot } from '@shared/spec';
import { renderBoard } from './paint';

export interface SaveBoardLookInput {
  board: BoardLook;
  assets: Asset[];
  name: string;
  occasion: string[];
  /** 覆盖保存时传原 Look 的 id */
  id?: string;
}

function slotsFromBoard(board: BoardLook, assets: Asset[]): Partial<Record<Slot, string | null>> {
  const byId = new Map(assets.map((a) => [a.id, a]));
  const slots: Partial<Record<Slot, string | null>> = {};
  // 画板允许同槽位并排摆两件（比如两条裤子对比），模特只能穿一件，取最靠前的那件
  const ordered = [...board.items].sort((a, b) => b.z - a.z);
  for (const item of ordered) {
    if (item.kind !== 'asset' || !item.assetId) continue;
    const asset = byId.get(item.assetId);
    if (!asset) continue;
    if (slots[asset.slot] === undefined) slots[asset.slot] = asset.id;
  }
  return slots;
}

function newLookId(): string {
  const alphabet = '0123456789ABCDEFGHJKMNPQRSTVWXYZ';
  let out = '';
  for (let i = 0; i < 16; i++) out += alphabet[Math.floor(Math.random() * alphabet.length)];
  return `look_${out}`;
}

export async function saveBoardLook(input: SaveBoardLookInput): Promise<Look> {
  const { board, assets, name, occasion } = input;
  const cover = await renderBoard(board, assets, 0.42, { crop: 'content' });
  const now = new Date().toISOString();
  const look: Look = {
    schema_version: 2,
    id: input.id ?? newLookId(),
    name,
    kind: 'board',
    base: { body: 'base_f02', skin: 2, hair: 'h01', hair_color: '#4A3428' },
    slots: slotsFromBoard(board, assets),
    z_overrides: {},
    hidden_slots: [],
    fit_overrides: {},
    occasion,
    tags: [],
    background: 'none',
    board,
    cover: 'cover.png',
    favorite: false,
    created_at: now,
    updated_at: now,
  };
  await window.pixelfit.looks.save(look, cover);
  return look;
}
