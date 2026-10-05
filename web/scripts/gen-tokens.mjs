// Generates src/styles/tokens.css: light + dark colour variables as RGB triplets, so
// Tailwind can compose alpha (`bg-slate-900/50`) and dark mode needs no per-class variants.
// Run with: npm run tokens
import { createRequire } from 'node:module';
import { writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../', import.meta.url));
const require = createRequire(root);
const colors = require('tailwindcss/colors');

const STEPS = [50, 100, 200, 300, 400, 500, 600, 700, 800, 900, 950];

function triplet(hex) {
  const n = parseInt(hex.slice(1), 16);
  return `${(n >> 16) & 255} ${(n >> 8) & 255} ${n & 255}`;
}

// Neutral ramp: zinc, used for every `slate-*` class already in the code.
const slateLight = Object.fromEntries(STEPS.map((s) => [s, colors.zinc[s]]));
// Dark neutral ramp is hand tuned: 500 must stay readable as secondary text on the surface.
const slateDark = {
  50: '#0a0a0c',
  100: '#1d1d21',
  200: '#2a2a30',
  300: '#3a3a42',
  400: '#6b6b75',
  500: '#9a9aa4',
  600: '#b4b4bd',
  700: '#cfcfd6',
  800: '#e6e6ea',
  900: '#f4f4f6',
  950: '#fafafa',
};

const families = {
  solar: colors.amber,
  amber: colors.amber,
  grid: colors.emerald,
  red: colors.red,
  blue: colors.blue,
  violet: colors.violet,
};

const light = [];
const dark = [];

for (const s of STEPS) {
  light.push(`  --c-slate-${s}: ${triplet(slateLight[s])};`);
  dark.push(`  --c-slate-${s}: ${triplet(slateDark[s])};`);
}
for (const [name, ramp] of Object.entries(families)) {
  for (const s of STEPS) {
    const mirrored = 1000 - s; // 50 <-> 950, 100 <-> 900 ... 500 stays
    light.push(`  --c-${name}-${s}: ${triplet(ramp[s])};`);
    dark.push(`  --c-${name}-${s}: ${triplet(ramp[mirrored])};`);
  }
}

const css = `/* =============================================================================
 * File        : src/styles/tokens.css
 * Project     : Smart Solar Microgrid Trading System - Web Application
 * Description : Colour tokens as RGB triplets. Tailwind reads them through
 *               rgb(var(--c-*) / <alpha-value>), so the same utility classes
 *               render correctly in light and dark mode.
 *
 *               Light ramps are Tailwind's zinc / amber / emerald / red / blue /
 *               violet. Dark ramps mirror each step (50 <-> 950) so a tinted
 *               callout stays a dark tint with light text. The neutral dark ramp
 *               is tuned by hand so secondary text keeps a 4.5:1 contrast.
 * ============================================================================= */

:root {
  color-scheme: light;
  --c-white: 255 255 255;
  --c-on-accent: 24 24 27;
  --c-accent: 251 191 36;
  --c-accent-hover: 245 158 11;
  --c-accent-press: 217 119 6;
  --c-chrome: 18 18 20;
  --c-chrome-line: 255 255 255;
  --c-chrome-text: 161 161 170;
  --c-chrome-hi: 250 250 250;
${light.join('\n')}
}

@media (prefers-color-scheme: dark) {
  :root {
    color-scheme: dark;
    --c-white: 20 20 23;
    --c-on-accent: 24 24 27;
    --c-accent: 251 191 36;
    --c-accent-hover: 245 158 11;
    --c-accent-press: 217 119 6;
    --c-chrome: 10 10 12;
    --c-chrome-line: 255 255 255;
    --c-chrome-text: 161 161 170;
    --c-chrome-hi: 250 250 250;
${dark.map((l) => '  ' + l).join('\n')}
  }
}
`;

writeFileSync(root + 'src/styles/tokens.css', css);
console.log('wrote tokens.css', css.length, 'bytes');
