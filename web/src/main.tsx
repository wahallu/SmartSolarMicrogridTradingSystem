/**
 * =============================================================================
 * File        : main.tsx
 * Project     : Smart Solar Microgrid Trading System - Web Application
 * Description : Browser entry point. Mounts the React tree into #root and pulls
 *               in the Tailwind stylesheet.
 * Author      : Smart Solar Microgrid Team - SE4040 Enterprise Application Dev
 * Created     : 2026-09-04
 * =============================================================================
 */

import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import App from './App';
import './index.css';

// The root element is declared in index.html; a missing one is a wiring error.
const container = document.getElementById('root');
if (!container) {
  throw new Error('Root element #root was not found in index.html.');
}

createRoot(container).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
