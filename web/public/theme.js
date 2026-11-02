// Task 7t. Loaded as a blocking <script> in index.html's <head> so the page's first paint
// already has the right palette (a plain file, not inline, so the CSP needs no hash).
// The one implementation of "which palette applies": React calls window.anitetherTheme.apply.
// Themes/modes mirror api/Data/Themes.cs and the palettes in src/index.css.
;(function () {
  var THEMES = ['default', 'crimson', 'slate']
  var MODES = ['system', 'dark', 'light']
  var media = window.matchMedia('(prefers-color-scheme: light)')
  var current = { theme: 'default', mode: 'system' }

  function read(key) {
    try {
      return localStorage.getItem(key)
    } catch (e) {
      return null
    }
  }

  function render() {
    var root = document.documentElement
    root.dataset.theme = current.theme
    // "system" is resolved here, so the CSS only ever sees dark or light.
    root.dataset.mode = current.mode === 'system' ? (media.matches ? 'light' : 'dark') : current.mode
  }

  function apply(theme, mode) {
    current.theme = THEMES.indexOf(theme) >= 0 ? theme : 'default'
    current.mode = MODES.indexOf(mode) >= 0 ? mode : 'system'
    try {
      localStorage.setItem('theme', current.theme)
      localStorage.setItem('colorMode', current.mode)
    } catch (e) {}
    render()
  }

  window.anitetherTheme = {
    apply: apply,
    get: function () {
      return { theme: current.theme, mode: current.mode }
    },
  }
  media.addEventListener('change', render)
  apply(read('theme'), read('colorMode'))
})()
