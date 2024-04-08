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
  }
}
