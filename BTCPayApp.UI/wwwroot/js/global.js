Interop = {
  eventListeners: {},
  getWidth(el) {
    return el.clientWidth;
  },
  openModal(selector) {
    const modal = new bootstrap.Modal(selector);
    modal.show();
  },
  closeModal(selector) {
    const $el = document.querySelector(selector);
    if (!$el) return console.warn('Selector does not exist:', selector);
    bootstrap.Modal.getInstance($el).hide();
  },
  addEventListener(dotnetHelper, selector, eventName, methodName) {
    const $el = document.querySelector(selector);
    if (!$el) return console.warn('Selector does not exist:', selector);
    const id = `${selector}_${eventName}_${methodName}`;
    Interop.eventListeners[id] = async event => {
      console.debug('Event listener invoked:', id);
      await dotnetHelper.invokeMethodAsync(methodName);
    }
    $el.addEventListener(eventName, Interop.eventListeners[id]);
    console.debug('Event listener added:', id);
  },
  removeEventListener(selector, eventName, methodName) {
    const $el = document.querySelector(selector);
    if (!$el) return console.warn('Selector does not exist:', selector);
    const id = `${selector}_${eventName}_${methodName}`;
    if (!Interop.eventListeners[id]) return console.warn('Event listener does not exist:', id);
    $el.removeEventListener(eventName, Interop.eventListeners[id]);
    delete Interop.eventListeners[id];
    console.debug('Event listener removed:', id);
  },
  removeEventListeners(...args) {
    for (arg of args) Interop.removeEventListener(...arg)
  },
  // theme
  setColorMode: window.setColorMode,
  setInstanceInfo(customThemeExtension, customThemeCssUrl, logoUrl) {
    const $tag = document.getElementById('CustomThemeLinkTag')
    if (customThemeExtension && customThemeCssUrl) {rel="icon"
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
    const $icon = document.querySelector('link[rel="icon"]')
    if (logoUrl) {
      if (!$icon.dataset.original) $icon.dataset.original = $icon.getAttribute('href');
      $icon.setAttribute('href', logoUrl);
    } else {
      $icon.setAttribute('href', $icon.dataset.original);
    }
  },
  // chartist
  renderLineChart (selector, labels, series, cryptoCode, rate, defaultCurrency, divisibility) {
    const valueTransform = value => rate ? DashboardUtils.displayCurrency(value, rate, defaultCurrency, divisibility) : value
    const labelCount = 6
    // https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Global_Objects/Intl/DateTimeFormat/DateTimeFormat
    const dateFormatter = new Intl.DateTimeFormat('default', { month: 'short', day: 'numeric' })
    const min = Math.min(...series);
    const max = Math.max(...series);
    const low = Math.max(min - ((max - min) / 5), 0);
    const pointCount = series.length;
    const labelEvery = pointCount / labelCount;
    new Chartist.Line(selector, {
      labels: labels,
      series: [series]
    }, {
      low,
      fullWidth: true,
      showArea: true,
      //lineSmooth: false,
      axisY: {
        //labelInterpolationFnc: valueTransform,
        showLabel: false,
        offset: 0
      },
      axisX: {
        labelInterpolationFnc(date, i) {
          return i % labelEvery == 0 ? dateFormatter.format(new Date(date)) : null
        }
      },
      plugins: [
        Chartist.plugins.tooltip2({
          template: '<div class="chartist-tooltip-value">{{value}}</div><div class="chartist-tooltip-line"></div>',
          offset: {
            x: 0,
            y: -16
          },
          valueTransformFunction(value, label) {
            return valueTransform(value) + ' ' + (rate ? defaultCurrency : cryptoCode)
          }
        })
      ]
    });
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

function toDefaultCurrency(amount, rate) {
  return Math.round((amount * rate) * 100) / 100;
}

function displayCurrency(amount, rate, currency, divisibility) {
  const value = rate ? toDefaultCurrency(amount, rate) : amount;
  const locale = currency === 'USD' ? 'en-US' : navigator.language;
  const isSats = currency === 'SATS';
  if (isSats) currency = 'BTC';
  const opts = { currency, style: 'decimal', minimumFractionDigits: divisibility };
  const val = new Intl.NumberFormat(locale, opts).format(value);
  return isSats ? val.replace(/[\\.,]/g, ' ') : val;
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
