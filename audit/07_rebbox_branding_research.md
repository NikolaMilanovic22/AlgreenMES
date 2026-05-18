# 07 — REBBOX MES rebranding research

Prepared for Nikola before any code is written, per his "Nemoj još da pišeš kod" gate.

## 1. Logo + name observations

Files received at `/Users/milosmitrovic/Downloads/rebbox/`:
- `logo.png` (without subtitle, **recommended**)
- `logo+subtitle.png` (alternative)

**Logo wordmark is "REBBOX®"** — double-B, bold italic, sans-serif, bright red on transparent/white background, registered trademark symbol (®). Nikola's message called this "Rebox MES" — flag for clarification: is the instance branded **REBBOX** (matching logo) or **Rebox** (different spelling)? They're different and the answer changes copy in emails / login page / browser tab title.

## 2. Logo color verification

User picked `#fb0102` from the logo file. Visually consistent with a pure-saturated red.

| Color | RGB | Used for |
|---|---|---|
| `#fb0102` | (251, 1, 2) | Proposed brand red |
| `#FF0000` | (255, 0, 0) | **Currently in use** as `processStatusColors[Blocked]` in `OrderListPage.tsx` (process state squares, background fill) |
| `#FF4D4F` | (255, 77, 79) | antd default `colorError` token, used throughout |

`#fb0102` and `#FF0000` differ by ≤4 in each channel — **visually indistinguishable.** They are the same red to any human user. Direct use of `#fb0102` as a primary token would collide with the most critical alarm signal in the app.

## 3. Status-red inventory (where red is already used as status/alarm)

Every place red appears in the dashboard FE today, and what its semantic role is. Brand red must avoid these contexts.

| File | Line | Use case | Color | Semantic |
|---|---|---|---|---|
| `apps/dashboard/src/pages/orders/OrderListPage.tsx` | 36-43 | `processStatusColors[Blocked]` — process state squares background fill | `#FF0000` (pure red) | **CRITICAL** — most visible status signal in the app |
| `apps/dashboard/src/components/StatusBadge.tsx` | 9 | `OrderStatus.Cancelled` Tag | antd `'red'` (~#f5222d) | Order cancelled |
| `apps/dashboard/src/components/StatusBadge.tsx` | 17 | `ProcessStatus.Blocked` Tag | antd `'red'` | Process blocked |
| `apps/dashboard/src/components/StatusBadge.tsx` | 25 | `RequestStatus.Rejected` Tag | antd `'red'` | Request rejected |
| `apps/dashboard/src/pages/coordinator/CoordinatorDashboard.tsx` | 75 | Critical warnings count | `colorError` | Number badge for critical count |
| `apps/dashboard/src/pages/coordinator/CoordinatorDashboard.tsx` | 121 | Overdue indicator | `colorError` | Worker overdue marker |
| `apps/dashboard/src/pages/coordinator/CoordinatorDashboard.tsx` | 182 | Worker check-in dot | `colorError` (when offline) | Status dot — green = online, red = offline |
| `apps/dashboard/src/pages/orders/OrderListPage.tsx` | 2566 | Block request banner | `colorErrorBg` + `colorErrorBorder` | Block notification panel |
| `apps/dashboard/src/pages/orders/OrderListPage.tsx`, `OrderAttachments.tsx` | multiple | PDF file icon | `colorError` | Aesthetic, NOT semantic (could be reclaimed for brand if needed) |
| `apps/dashboard/src/components/ErrorBoundary.tsx` | 11 | Error message text | `colorError` | App-crash error display |

Net: **red is the alarm/critical/blocked color across the entire dashboard.** The PDF icon is the only "could reclaim" candidate — every other use is semantic.

## 4. Proposed color strategy — three paths, pick one

### Path A — Wine red brand (Nikola's earlier suggestion) — RECOMMENDED

Use a **darker** red for brand (e.g. `#8E1F2C` burgundy or similar wine tone) and keep `#FF0000` / `#FF4D4F` for status. The two reds are visually distinct: brand reads as "premium / formal", status reads as "alarm".

**Trade-off:** doesn't match the logo's pure red. Header logo image stays `#fb0102`, but the rest of the brand chrome (primary buttons, accents, links, focus rings) uses wine. Logo and chrome don't perfectly match but the divergence is intentional and harmless — most brand systems do this anyway.

### Path B — Brand red only in typography + accent lines, never in backgrounds

Keep brand red `#fb0102` for: typography (headings, links), 1-2px accent lines (under header, around focus rings), icon strokes. Never use as background fill on cards, buttons, banners, tags. Status red stays as full background signal.

**Trade-off:** harder to enforce — every component would need a per-property review (which property uses brand red, which uses status). Style drift risk over time.

### Path C — Reclaim status red, repaint Blocked

Change `processStatusColors[Blocked]` from `#FF0000` to another distinctive color (orange? purple?). Then brand red is free for chrome use. Requires updating: `processStatusColors` in `OrderListPage.tsx`, `StatusBadge` red→other-color mapping, `colorError` overrides, Excel-export status colors, FE 403 toast styling.

**Trade-off:** breaks the "Excel parity" rule documented in CLAUDE.md ("intentional design, mirrors Excel parity"). Mile and Bojan/Sale workers are trained on red=blocked — repainting that re-trains every operator. Significant UX risk.

**Recommendation:** Path A. Cheapest, lowest user-impact, visually clean. Wine red is also more "premium" feeling which often suits MES products selling into industrial customers anyway. Logo stays pure red; chrome stays wine. The visual divergence in the header is normal — Coca-Cola's chrome isn't logo-red either.

## 5. PR structure proposal — granular, Faza 1 items

Per Nikola's preference for granular review. Each PR scoped to one concern, easy to revert if a detail goes wrong.

| PR # | Scope | BE/FE | Approx LOC | Reviewer |
|---|---|---|---|---|
| 1 | New repo `m1ck333/rebbox-mes-fe` scaffolded (clone of easy-mes-fe, rename `@easymes/*` → `@rebbox/*`, deploy.sh, theme placeholder) | FE only | ~500 (mostly renames) | Nikola |
| 2 | Logo assets (header logo, login screen, favicon, PWA manifest icons 192/512, apple-touch-icon) | FE only | ~10 + binaries | Nikola |
| 3 | Theme tokens — antd `ConfigProvider` with primary=wine, secondary, neutrals, custom typography scale | FE only | ~30 | Nikola |
| 4 | Login page background + "Welcome to REBBOX MES" copy + i18n | FE only | ~50 | Nikola |
| 5 | Browser tab title + meta description + i18n | FE only | ~5 | Nikola |
| 6 | Email template (invite + password-reset) with REBBOX header + footer | BE | ~100 | Nikola |
| 7 | New tenant `rebbox` in the BE migration + seeder (or manual SQL on droplet if no migration needed) | BE | ~20 | Nikola |

Total Faza 1: ~7 small PRs, each reviewable in < 5 min.

## 6. Repo strategy — concur with Nikola

**Fourth FE repo `m1ck333/rebbox-mes-fe`.** NOT a fork of easy-mes-fe. Reasons (echoing Nikola): easy-mes is internal demo/test; REBBOX is paying production. Mixing them means every easy-mes commit can potentially break the customer instance. The mirror cost goes from 3 FE repos to 4 FE repos but that's the price of production isolation.

**BE stays single-codebase, multi-tenant.** New tenant `rebbox` in `tenancy.tenants`. No fifth BE repo. Multi-tenant design is exactly for this.

## 7. Questions still open for Nikola

1. **Spelling clarification:** logo says **REBBOX®** (double-B). Nikola wrote "Rebox MES" (single-B). Which is correct for the instance name, browser title, emails, etc.?
2. **Path A confirmation:** Is wine brand red OK, or does the client expect their pure red (`#fb0102`) used as the primary chrome color?
3. **Faza 1 timeline:** Nikola asked for go-live date from Rebox sales contact. Still pending.
4. **Status of the `rebbox-mes-tablet` URL / domain:** tablet PWA needs its own subdomain (per the alblue / easy-mes pattern). Has the client purchased / set up DNS for `rebbox-mes.duckdns.org` and `rebbox-mes-tablet.duckdns.org`, or are we using a real domain (`rebbox.com/app`)?
5. **Droplet:** new droplet for REBBOX (like easy-mes has its own), or shared with easy-mes? My recommendation: new droplet — isolation. Plus separate Sentry environment tag `rebbox-prod`.

## 8. What we are NOT doing tonight

- Writing any code in any repo (per Nikola's gate)
- Picking the exact wine red hex value (need confirmation on Path A first)
- Creating `m1ck333/rebbox-mes-fe` (need Nikola's go-ahead)
- Provisioning a new droplet
- Adding the `rebbox` tenant to any DB

When Nikola confirms (a) Path A is OK, (b) the REBBOX spelling, (c) the deploy target — then we start with PR #1.
