import React from 'react';

/** 统一 1.6px 线宽的线性图标，风格与现代精致 UI 一致（非复古像素图标） */
const base = {
  width: 18,
  height: 18,
  viewBox: '0 0 24 24',
  fill: 'none',
  stroke: 'currentColor',
  strokeWidth: 1.6,
  strokeLinecap: 'round' as const,
  strokeLinejoin: 'round' as const,
};

type P = { size?: number };

const wrap = (path: React.ReactNode) =>
  function Icon({ size = 18 }: P) {
    return (
      <svg {...base} width={size} height={size}>
        {path}
      </svg>
    );
  };

export const IconWardrobe = wrap(
  <>
    <rect x="3" y="3" width="18" height="18" rx="2.5" />
    <path d="M12 3v18M9 10.5v2M15 10.5v2" />
  </>,
);

export const IconImport = wrap(
  <>
    <path d="M12 15V4M12 4 8.5 7.5M12 4l3.5 3.5" />
    <path d="M4 15v3a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-3" />
  </>,
);

export const IconLooks = wrap(
  <>
    <path d="M4 7h7l2 2h7v9a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2z" />
    <path d="M9 13h6" />
  </>,
);

export const IconBody = wrap(
  <>
    <circle cx="12" cy="5.5" r="2.5" />
    <path d="M12 8v7M8 10.5 12 9l4 1.5M9.5 21l2-6M14.5 21l-2-6" />
  </>,
);

export const IconSettings = wrap(
  <>
    <circle cx="12" cy="12" r="3" />
    <path d="M12 3v2m0 14v2M3 12h2m14 0h2M5.6 5.6l1.4 1.4m10 10 1.4 1.4m0-12.8-1.4 1.4m-10 10-1.4 1.4" />
  </>,
);

export const IconSearch = wrap(
  <>
    <circle cx="11" cy="11" r="6.5" />
    <path d="m16 16 4 4" />
  </>,
);

export const IconEye = wrap(
  <>
    <path d="M2.5 12S6 5.5 12 5.5 21.5 12 21.5 12 18 18.5 12 18.5 2.5 12 2.5 12" />
    <circle cx="12" cy="12" r="2.6" />
  </>,
);

export const IconEyeOff = wrap(
  <>
    <path d="M4 4l16 16M9.6 5.9A9.6 9.6 0 0 1 12 5.6c6 0 9.5 6.4 9.5 6.4a17 17 0 0 1-3.3 4M6.2 8.1A17 17 0 0 0 2.5 12S6 18.4 12 18.4a9.4 9.4 0 0 0 3.4-.6" />
  </>,
);

export const IconX = wrap(<path d="M6 6l12 12M18 6 6 18" />);
export const IconMinus = wrap(<path d="M5 12h14" />);
export const IconSquare = wrap(<rect x="5.5" y="5.5" width="13" height="13" rx="2" />);
export const IconPlus = wrap(<path d="M12 5v14M5 12h14" />);
export const IconUndo = wrap(
  <>
    <path d="M4 9h11a5 5 0 0 1 0 10h-6" />
    <path d="M4 9l4-4M4 9l4 4" />
  </>,
);
export const IconStar = wrap(
  <path d="m12 4 2.4 5 5.6.8-4 3.9 1 5.5-5-2.7-5 2.7 1-5.5-4-3.9 5.6-.8z" />,
);
export const IconStarFill = function IconStarFill({ size = 18 }: P) {
  return (
    <svg {...base} width={size} height={size} fill="currentColor">
      <path d="m12 4 2.4 5 5.6.8-4 3.9 1 5.5-5-2.7-5 2.7 1-5.5-4-3.9 5.6-.8z" />
    </svg>
  );
};
export const IconTrash = wrap(
  <>
    <path d="M4.5 7h15M9.5 7V5.5A1.5 1.5 0 0 1 11 4h2a1.5 1.5 0 0 1 1.5 1.5V7" />
    <path d="M6.5 7l.8 12a2 2 0 0 0 2 1.9h5.4a2 2 0 0 0 2-1.9l.8-12" />
  </>,
);
export const IconDownload = wrap(
  <>
    <path d="M12 4v11M8.5 11.5 12 15l3.5-3.5" />
    <path d="M5 19h14" />
  </>,
);
export const IconCompare = wrap(
  <>
    <rect x="3" y="5" width="7.5" height="14" rx="1.6" />
    <rect x="13.5" y="5" width="7.5" height="14" rx="1.6" />
  </>,
);
export const IconLayers = wrap(
  <>
    <path d="m12 4 8 4.2-8 4.2-8-4.2z" />
    <path d="m4 13 8 4.2 8-4.2" />
  </>,
);
export const IconMove = wrap(
  <>
    <path d="M12 3v18M3 12h18" />
    <path d="m9 6 3-3 3 3M9 18l3 3 3-3M6 9l-3 3 3 3M18 9l3 3-3 3" />
  </>,
);
export const IconSparkle = wrap(
  <path d="M12 3.5 13.7 9l5.5 1.7-5.5 1.7L12 18l-1.7-5.6L4.8 10.7 10.3 9z" />,
);
export const IconFolder = wrap(
  <path d="M4 6.5h5.5l1.8 2H20a1.5 1.5 0 0 1 1.5 1.5v7.5A1.5 1.5 0 0 1 20 19H4a1.5 1.5 0 0 1-1.5-1.5V8A1.5 1.5 0 0 1 4 6.5" />,
);
export const IconRefresh = wrap(
  <>
    <path d="M20 11a8 8 0 0 0-13.7-5.1L4 8" />
    <path d="M4 4v4h4" />
    <path d="M4 13a8 8 0 0 0 13.7 5.1L20 16" />
    <path d="M20 20v-4h-4" />
  </>,
);

/** 搭配画板（CERE-21）：一张纸上摊开的几件单品 */
export const IconBoard = wrap(
  <>
    <rect x="3" y="3.5" width="18" height="17" rx="2.2" />
    <rect x="6" y="6.5" width="7" height="6" rx="1.2" />
    <rect x="6" y="14.5" width="7" height="3" rx="1.2" />
    <rect x="15.5" y="6.5" width="3" height="3" rx="1" />
    <rect x="15.5" y="12" width="3" height="5.5" rx="1.2" />
  </>,
);
