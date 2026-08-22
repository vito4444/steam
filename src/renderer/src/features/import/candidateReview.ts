import type {
  PhotoImportCandidate,
  PhotoImportQualityReason,
} from '@shared/ipc';

const REASON_LABEL: Record<string, string> = {
  SUBJECT_TOO_SMALL: '主体面积',
  SUBJECT_TOO_LARGE: '前景面积',
  ISOLATED_COMPONENTS: '零散残留',
  EXCESSIVE_HOLES: '透明孔洞',
  LOW_BBOX_FILL: '主体完整度',
  SUBJECT_TRUNCATED: '画面边界',
  JAGGED_EDGE: '边缘平滑度',
  MASK_STRUCTURE_UNRELIABLE: '边缘结构',
  EDGE_TOO_SHARP: '边缘过渡',
  ALPHA_TRANSITION_MISSING: '半透明过渡',
  ALPHA_TRANSITION_EXCESSIVE: '半透明范围',
};

const LOWER_BOUND_REASONS = new Set([
  'SUBJECT_TOO_SMALL',
  'LOW_BBOX_FILL',
  'ALPHA_TRANSITION_MISSING',
]);

const formatNumber = (value: number) => {
  const magnitude = Math.abs(value);
  if (magnitude >= 100) return value.toFixed(0);
  if (magnitude >= 10) return value.toFixed(1);
  return value.toFixed(3);
};

const formatValue = (value: number | boolean) =>
  typeof value === 'boolean' ? (value ? '是' : '否') : formatNumber(value);

export function candidateReasonLine(reason: PhotoImportQualityReason): string {
  const label = REASON_LABEL[reason.code] ?? reason.metric;
  if (typeof reason.value === 'boolean' || typeof reason.threshold === 'boolean') {
    return `${label}：实测 ${formatValue(reason.value)}，要求 ${formatValue(reason.threshold)}`;
  }
  const lowerBound = LOWER_BOUND_REASONS.has(reason.code);
  const operator = lowerBound ? '≥' : '≤';
  const difference = Math.abs(reason.value - reason.threshold);
  const direction = reason.value < reason.threshold ? '低于' : '超出';
  return `${label}：实测 ${formatNumber(reason.value)}，门槛 ${operator} ${formatNumber(reason.threshold)}，${direction} ${formatNumber(difference)}`;
}

export function candidateStateLabel(
  state: PhotoImportCandidate['state'],
): string {
  if (state === 'ready') return '合格 · 已入库';
  if (state === 'needs_optimization') return '待优化 · 已入库';
  return '严重失败 · 需重试';
}

export function candidateVisibilityLabel(
  visibility: PhotoImportCandidate['visibility'],
): string {
  return visibility === 'partial' ? '部分可见，未补画遮挡区域' : '主体完整可见';
}

export function summarizeCandidates(
  candidates: Pick<PhotoImportCandidate, 'state'>[],
): string {
  const ready = candidates.filter((candidate) => candidate.state === 'ready').length;
  const optimization = candidates.filter(
    (candidate) => candidate.state === 'needs_optimization',
  ).length;
  const retry = candidates.filter((candidate) => candidate.state === 'retry').length;
  return `${candidates.length} 个候选：${ready} 件合格，${optimization} 件待优化，${retry} 件需重试`;
}
