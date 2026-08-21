import React from 'react';
import { createRoot } from 'react-dom/client';

import App from './App';
import { StoreProvider } from './state/store';
import { BoardProvider } from './features/board/boardStore';
import './styles.css';

createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <StoreProvider>
      <BoardProvider>
        <App />
      </BoardProvider>
    </StoreProvider>
  </React.StrictMode>,
);
