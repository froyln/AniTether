/** The palette logic lives in public/theme.js (it must run before first paint);
 * this is its typed handle for React. Values mirror api/Data/Themes.cs. */
export const THEMES = [
  { id: 'default', label: 'Default (violet)', swatch: ['#0f1117', '#7c6cf2'] },
  { id: 'crimson', label: 'Crimson', swatch: ['#130d0f', '#ef5b72'] },
  { id: 'slate', label: 'Slate blue', swatch: ['#0d1218', '#5aa9ff'] },
] as const

export const MODES = [
  { id: 'system', label: 'Match system' },
  { id: 'dark', label: 'Dark' },
  { id: 'light', label: 'Light' },
] as const

type ThemeApi = { apply: (theme: string | null, mode: string | null) => void; get: () => { theme: string; mode: string } }

declare global {
  interface Window {
    anitetherTheme?: ThemeApi
  }
}

// Optional-chained: if theme.js failed to load (blocked, stale cached index.html), theming
// degrades to the CSS default instead of throwing inside login/refresh.
export const applyTheme = (theme: string | null, mode: string | null) => window.anitetherTheme?.apply(theme, mode)
export const currentTheme = () => window.anitetherTheme?.get() ?? { theme: 'default', mode: 'system' }
