/* =============================================================================
 * File        : vite.config.ts
 * Project     : Smart Solar Microgrid Trading System - Web Application
 * Module      : Build Configuration
 * Description : Configures the Vite development server and React build.
 * Author      : IT22246332 - Sandun
 * Created     : 2026-09-22
 * =============================================================================
 */

import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
})
