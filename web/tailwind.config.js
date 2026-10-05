/**
 * =============================================================================
 * File        : tailwind.config.js
 * Project     : Smart Solar Microgrid Trading System - Web Application
 * Description : Tailwind CSS 3 configuration. Colours resolve to CSS variables
 *               defined in src/styles/tokens.css, so every existing utility
 *               class (bg-white, text-slate-600, bg-solar-500 ...) follows the
 *               light / dark colour scheme without per-class dark: variants.
 *
 *               Brand: solar amber for the single accent, grid green and red
 *               only for state. Neutrals are zinc.
 * Author      : Smart Solar Microgrid Team - SE4040 Enterprise Application Dev
 * Created     : 2026-09-04
 * =============================================================================
 */

const STEPS = [50, 100, 200, 300, 400, 500, 600, 700, 800, 900, 950];

/** Builds { 50: 'rgb(var(--c-name-50) / <alpha-value>)', ... } for one ramp. */
function ramp(name) {
  return Object.fromEntries(
    STEPS.map((step) => [step, `rgb(var(--c-${name}-${step}) / <alpha-value>)`]),
  );
}

/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{js,ts,jsx,tsx}'],
  theme: {
    // A tighter radius scale reads as engineered rather than bubbly.
    borderRadius: {
      none: '0',
      sm: '2px',
      DEFAULT: '4px',
      md: '4px',
      lg: '6px',
      xl: '8px',
      '2xl': '12px',
      '3xl': '16px',
      full: '9999px',
    },
    extend: {
      colors: {
        white: 'rgb(var(--c-white) / <alpha-value>)',
        slate: ramp('slate'),
        solar: ramp('solar'),
        amber: ramp('solar'),
        grid: ramp('grid'),
        red: ramp('red'),
        blue: ramp('blue'),
        violet: ramp('violet'),
        // The one accent and the ink that sits on it. Fixed in both schemes.
        accent: {
          DEFAULT: 'rgb(var(--c-accent) / <alpha-value>)',
          hover: 'rgb(var(--c-accent-hover) / <alpha-value>)',
          press: 'rgb(var(--c-accent-press) / <alpha-value>)',
          ink: 'rgb(var(--c-on-accent) / <alpha-value>)',
        },
        // The dark navigation rail.
        chrome: {
          DEFAULT: 'rgb(var(--c-chrome) / <alpha-value>)',
          line: 'rgb(var(--c-chrome-line) / <alpha-value>)',
          text: 'rgb(var(--c-chrome-text) / <alpha-value>)',
          hi: 'rgb(var(--c-chrome-hi) / <alpha-value>)',
        },
      },
      fontFamily: {
        sans: [
          '"Geist Variable"',
          'Geist',
          'ui-sans-serif',
          'system-ui',
          '"Segoe UI"',
          'sans-serif',
        ],
        mono: ['"Geist Mono Variable"', '"Geist Mono"', 'ui-monospace', 'Consolas', 'monospace'],
      },
      boxShadow: {
        // Surfaces are separated by borders; shadows are reserved for overlays.
        card: '0 1px 0 0 rgb(24 24 27 / 0.03)',
        'card-hover': '0 12px 32px -12px rgb(24 24 27 / 0.28), 0 2px 6px -2px rgb(24 24 27 / 0.12)',
      },
      keyframes: {
        'fade-in': {
          '0%': { opacity: '0', transform: 'translateY(6px)' },
          '100%': { opacity: '1', transform: 'translateY(0)' },
        },
        shimmer: {
          '0%': { backgroundPosition: '-200% 0' },
          '100%': { backgroundPosition: '200% 0' },
        },
      },
      animation: {
        'fade-in': 'fade-in 0.28s cubic-bezier(0.16, 1, 0.3, 1) both',
        shimmer: 'shimmer 1.6s linear infinite',
      },
    },
  },
  plugins: [],
};
