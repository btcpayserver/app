Interop = {
  getWidth(el) {
    return el.clientWidth;
  },
  closeModal(selector) {
    const $el = document.querySelector(selector);
    bootstrap.Modal.getInstance($el).hide();
  },
  addModalEvent(dotnetHelper, selector, eventName, methodName) {
    const $el= document.querySelector(selector);
    $el.addEventListener(`${eventName}.bs.modal`, async event => {
      await dotnetHelper.invokeMethodAsync(methodName);
    });
  },

  // theme
  setColorMode: window.setColorMode,
  setInstanceInfo(customThemeExtension, customThemeCssUrl) {
    const $tag = document.getElementById('CustomThemeLinkTag')
    if (customThemeExtension && customThemeCssUrl) {
      $tag.setAttribute('rel', 'stylesheet');
      $tag.setAttribute('href', customThemeCssUrl);
      document.documentElement.setAttribute(window.THEME_ATTR, customThemeExtension.toLowerCase());
    } else {
      $tag.removeAttribute('rel');
      $tag.removeAttribute('href');
      const mode = window.localStorage.getItem(window.THEME_STORE_ATTR);
      document.documentElement.setAttribute(window.THEME_ATTR, mode);
    }
  }
}
