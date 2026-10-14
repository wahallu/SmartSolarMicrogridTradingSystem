# Smart Solar Microgrid - Web console

React 18, TypeScript, Vite and Tailwind CSS 3. Serves the public landing page, the
sign-in screen and the signed-in console for Backoffice officers and Grid Operators.

## Scripts

| Command | What it does |
|---|---|
| `npm run dev` | Vite dev server with hot reload |
| `npm run build` | Type-check, then production build into `dist/` |
| `npm run lint` | Oxlint |
| `npm run tokens` | Regenerate `src/styles/tokens.css` from the palette in `scripts/gen-tokens.mjs` |

The API address comes from `VITE_API_BASE_URL` and defaults to
`http://localhost:5218/api/v1`.

## Design system

The console follows one small set of rules. Change them in one place and every screen follows.

- **One accent.** Amber, always with dark ink on top of it (`text-accent-ink`). White on amber
  does not reach contrast, so it is never used. Green and red are reserved for state.
- **Neutrals are zinc.** The `slate-*` classes in the code resolve to zinc.
- **Surfaces are separated by borders, not shadows.** Shadows are kept for dialogs and toasts.
- **One radius scale.** Controls 6px (`rounded-lg`), surfaces 8px (`rounded-xl`), pills full.
- **Type.** Geist and Geist Mono, self-hosted through `@fontsource-variable`. Figures use
  `tabular-nums`.
- **Focus** is always a 2px ink ring, visible on amber and on white.

### Colour tokens and dark mode

Colours are CSS variables (RGB triplets) in `src/styles/tokens.css`. `tailwind.config.js` maps
every colour name to `rgb(var(--c-name-step) / <alpha-value>)`, so existing utilities such as
`bg-white`, `text-slate-600` or `bg-solar-50` work in both colour schemes with no `dark:`
variants. Dark mode follows `prefers-color-scheme`: each ramp is mirrored (50 to 950, 100 to
900 and so on) and the neutral ramp is tuned by hand so secondary text keeps 4.5:1 contrast.

Edit the palette in `scripts/gen-tokens.mjs`, then run `npm run tokens`. Do not edit
`tokens.css` by hand.

Fixed tokens that deliberately do not mirror: `accent`, `accent-hover`, `accent-press`,
`accent-ink` (the amber and its ink) and `chrome-*` (the always-dark navigation rail).

## Images

Photographs live in `public/images/` and are credited in `public/images/CREDITS.md`.
