const COLOR_MODES = ["light", "dark"];
const THEME_ATTR = "data-btcpay-theme";
const STORE_ATTR = "btcpay-theme";
const userColorMode = window.localStorage.getItem(STORE_ATTR);
const initialColorMode = COLOR_MODES.includes(userColorMode) ? userColorMode : null;

function setColorMode (mode) {
  if (COLOR_MODES.includes(mode)) {
    window.localStorage.setItem(STORE_ATTR, mode);
    document.documentElement.setAttribute(THEME_ATTR, mode);
  } else {
    window.localStorage.removeItem(STORE_ATTR);
    document.documentElement.removeAttribute(THEME_ATTR);
  }
}

setColorMode(initialColorMode);
