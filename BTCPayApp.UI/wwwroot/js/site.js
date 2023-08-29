// Theme Switch
const THEMES = ['light', 'dark']
const THEME_ATTR = 'data-btcpay-theme'
const systemColorMode = window.matchMedia('(prefers-color-scheme: dark)').matches ? THEMES[1] : THEMES[0]
function setTheme(mode) {
  if (THEMES.includes(mode) && mode !== systemColorMode) {
    document.documentElement.setAttribute(THEME_ATTR, mode)
  } else {
    document.documentElement.removeAttribute(THEME_ATTR)
  }
}
