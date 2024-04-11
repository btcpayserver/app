// global constants
var THEME_ATTR = "data-btcpay-theme";
var THEME_STORE_ATTR = "btcpay-theme";

// local scope
const COLOR_MODES = ["light", "dark"];
const userColorMode = window.localStorage.getItem(THEME_STORE_ATTR);
const initialColorMode = COLOR_MODES.includes(userColorMode) ? userColorMode : null;

function setColorMode(mode) {
  if (COLOR_MODES.includes(mode)) {
    window.localStorage.setItem(THEME_STORE_ATTR, mode);
    document.documentElement.setAttribute(THEME_ATTR, mode);
  } else {
    window.localStorage.removeItem(THEME_STORE_ATTR);
    document.documentElement.removeAttribute(THEME_ATTR);
  }
}

setColorMode(initialColorMode);
