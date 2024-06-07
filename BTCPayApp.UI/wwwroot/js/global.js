Interop = {
  getWidth(el) {
    return el.clientWidth;
  },
  openModal(selector) {
    const modal = new bootstrap.Modal(selector);
    modal.show();
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
      if (THEME_COLOR_MODES.includes(mode)) {
        document.documentElement.setAttribute(THEME_ATTR, mode);
      } else {
        document.documentElement.removeAttribute(THEME_ATTR);
      }
    }
  }
}

function delegate(eventType, selector, handler, root) {
  (root || document).addEventListener(eventType, function(event) {
    const target = event.target.closest(selector)
    if (target) {
      event.target = target
      if (handler.call(this, event) === false) {
        event.preventDefault()
      }
    }
  })
}

const DEBOUNCE_TIMERS = {}
function debounce(key, fn, delay = 250) {
  clearTimeout(DEBOUNCE_TIMERS[key])
  DEBOUNCE_TIMERS[key] = setTimeout(fn, delay)
}

function formatDateTimes(format) {
  // select only elements which haven't been initialized before, those without data-localized
  document.querySelectorAll("time[datetime]:not([data-localized])").forEach($el => {
    const date = new Date($el.getAttribute("datetime"));
    // https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Global_Objects/Intl/DateTimeFormat/DateTimeFormat
    const { dateStyle = 'short', timeStyle = 'short' } = $el.dataset;
    // initialize and set localized attribute
    $el.dataset.localized = new Intl.DateTimeFormat('default', { dateStyle, timeStyle }).format(date);
    // set text to chosen mode
    const mode = format || $el.dataset.initial;
    if ($el.dataset[mode]) $el.innerText = $el.dataset[mode];
  });
}

function confirmCopy(el, message) {
  const hasIcon = !!el.innerHTML.match('icon-actions-copy')
  const confirmHTML = `<span class="text-success">${message}</span>`;
  if (hasIcon) {
    el.innerHTML = el.innerHTML.replace('#actions-copy', '#checkmark');
  } else {
    const { width, height } = el.getBoundingClientRect();
    el.dataset.clipboardInitial = el.innerHTML;
    el.style.minWidth = width + 'px';
    el.style.minHeight = height + 'px';
    el.innerHTML = confirmHTML;
  }
  el.dataset.clipboardConfirming = true;
  if (el.dataset.clipboardHandler) {
    clearTimeout(parseInt(el.dataset.clipboardHandler));
  }
  const timeoutId = setTimeout(function () {
    if (hasIcon) {
      el.innerHTML = el.innerHTML.replace('#checkmark', '#actions-copy');
    } else if (el.innerHTML === confirmHTML) {
      el.innerHTML = el.dataset.clipboardInitial;
    }
    delete el.dataset.clipboardConfirming;
    el.dataset.clipboardHandler = null;
  }, 2500);
  el.dataset.clipboardHandler = timeoutId.toString();
}

window.copyToClipboard = async function (e, data) {
  e.preventDefault();
  const item = e.target.closest('[data-clipboard]') || e.target.closest('[data-clipboard-target]') || e.target;
  const confirm = item.dataset.clipboardConfirmElement
    ? document.querySelector(item.dataset.clipboardConfirmElement) || item
    : item.querySelector('[data-clipboard-confirm]') || item;
  const message = confirm.getAttribute('data-clipboard-confirm') || 'Copied';
  // Check compatibility and permissions:
  // https://web.dev/async-clipboard/#security-and-permissions
  let hasPermission = true;
  if (navigator.clipboard && navigator.permissions) {
    try {
      const permissionStatus = await navigator.permissions.query({ name: 'clipboard-write', allowWithoutGesture: false });
      hasPermission = permissionStatus.state === 'granted';
    } catch (err) {}
  }
  if (navigator.clipboard && hasPermission) {
    await navigator.clipboard.writeText(data);
    confirmCopy(confirm, message);
  } else {
    const copyEl = document.createElement('textarea');
    copyEl.style.position = 'absolute';
    copyEl.style.opacity = '0';
    copyEl.value = data;
    document.body.appendChild(copyEl);
    copyEl.select();
    document.execCommand('copy');
    copyEl.remove();
    confirmCopy(confirm, message);
  }
  item.blur();
}

window.copyUrlToClipboard = function (e) {
  window.copyToClipboard(e, window.location)
}

document.addEventListener("DOMContentLoaded", function () {
  delegate('click', '[data-clipboard]', function (e) {
    const target = e.target.closest('[data-clipboard]');
    const data = target.getAttribute('data-clipboard') ||  target.innerText || target.value;
    window.copyToClipboard(e, data)
  })
  delegate('click', '[data-clipboard-target]', function (e) {
    const selector = e.target.closest('[data-clipboard-target]').getAttribute('data-clipboard-target');
    const target = document.querySelector(selector)
    const data = target.innerText || target.value;
    window.copyToClipboard(e, data)
  })
})
