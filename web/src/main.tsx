/* =============================================================================
 * File        : src/main.tsx
 * Project     : Smart Solar Microgrid Trading System - Web Application
 * Module      : Application Bootstrap
 * Description : Starts React and mounts the application in the browser.
 * Author      : IT22246332 - Sandun
 * Created     : 2026-09-23
 * =============================================================================
 */

import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import '@fontsource-variable/geist';
import '@fontsource-variable/geist-mono';
import App from './App';
import './styles/tokens.css';
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
