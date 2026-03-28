window.SwebKit = window.SwebKit || {};

window.SwebKit.registerKeyboardShortcuts = function (dotNetRef) {
  document.addEventListener('keydown', function (e) {
    const ctrl = e.ctrlKey;
    const alt = e.altKey;
    const shift = e.shiftKey;
    const key = e.key;

    // Skip if inside an input/textarea/select (except designated shortcuts)
    const inInput =
      ['INPUT', 'TEXTAREA', 'SELECT'].includes(
        document.activeElement?.tagName,
      ) || document.activeElement?.isContentEditable;

    if (ctrl && key === 'p' && !shift) {
      e.preventDefault();
      dotNetRef.invokeMethodAsync('OnShortcut', 'CommandPalette');
      return;
    }
    // Nav shortcuts match left menu order: 1=Dashboard 2=ServiceBus 3=AKS 4=Redis 5=Storage 6=Releases
    if (ctrl && key === '1') {
      e.preventDefault();
      dotNetRef.invokeMethodAsync('OnShortcut', 'NavDashboard');
      return;
    }
    if (ctrl && key === '2') {
      e.preventDefault();
      dotNetRef.invokeMethodAsync('OnShortcut', 'NavServiceBus');
      return;
    }
    if (ctrl && key === '3') {
      e.preventDefault();
      dotNetRef.invokeMethodAsync('OnShortcut', 'NavAks');
      return;
    }
    if (ctrl && key === '4') {
      e.preventDefault();
      dotNetRef.invokeMethodAsync('OnShortcut', 'NavRedis');
      return;
    }
    if (ctrl && key === '5') {
      e.preventDefault();
      dotNetRef.invokeMethodAsync('OnShortcut', 'NavStorage');
      return;
    }
    if (ctrl && key === '6') {
      e.preventDefault();
      dotNetRef.invokeMethodAsync('OnShortcut', 'NavReleases');
      return;
    }
    if (ctrl && key === ',') {
      e.preventDefault();
      dotNetRef.invokeMethodAsync('OnShortcut', 'NavSettings');
      return;
    }
    if (ctrl && key === 'Tab' && !shift) {
      e.preventDefault();
      dotNetRef.invokeMethodAsync('OnShortcut', 'TabNext');
      return;
    }
    if (ctrl && key === 'Tab' && shift) {
      e.preventDefault();
      dotNetRef.invokeMethodAsync('OnShortcut', 'TabPrev');
      return;
    }
    if (ctrl && key === 'w') {
      e.preventDefault();
      dotNetRef.invokeMethodAsync('OnShortcut', 'TabClose');
      return;
    }
    if (ctrl && key === '\\') {
      e.preventDefault();
      dotNetRef.invokeMethodAsync('OnShortcut', 'ToggleDetailsPane');
      return;
    }
    if (key === 'F5') {
      e.preventDefault();
      dotNetRef.invokeMethodAsync('OnShortcut', 'Refresh');
      return;
    }
    if (key === 'Escape') {
      dotNetRef.invokeMethodAsync('OnShortcut', 'Escape');
      return;
    }
    if (ctrl && key === 'Enter') {
      e.preventDefault();
      dotNetRef.invokeMethodAsync('OnShortcut', 'Execute');
      return;
    }
    if (ctrl && key === 'f' && !inInput) {
      e.preventDefault();
      dotNetRef.invokeMethodAsync('OnShortcut', 'FocusFilter');
      return;
    }
    if (alt && key === '1') {
      e.preventDefault();
      dotNetRef.invokeMethodAsync('OnShortcut', 'EnvDev');
      return;
    }
    if (alt && key === '2') {
      e.preventDefault();
      dotNetRef.invokeMethodAsync('OnShortcut', 'EnvTest');
      return;
    }
    if (alt && key === '3') {
      e.preventDefault();
      dotNetRef.invokeMethodAsync('OnShortcut', 'EnvAcc');
      return;
    }
    if (alt && key === '4') {
      e.preventDefault();
      dotNetRef.invokeMethodAsync('OnShortcut', 'EnvProd');
      return;
    }
    if (alt && shift && key === 'P') {
      e.preventDefault();
      dotNetRef.invokeMethodAsync('OnShortcut', 'FocusProjectSelector');
      return;
    }

    // ? shortcut — open keyboard shortcuts panel (not when typing)
    if (key === '?' && !inInput) {
      e.preventDefault();
      dotNetRef.invokeMethodAsync('OnShortcut', 'KeyboardShortcuts');
      return;
    }

    // Service Bus quick actions (only when not in text input)
    if (!inInput) {
      if (ctrl && key === 'e') {
        e.preventDefault();
        dotNetRef.invokeMethodAsync('OnShortcut', 'SbEditResubmit');
        return;
      }
      if (ctrl && key === 'r' && !shift) {
        e.preventDefault();
        dotNetRef.invokeMethodAsync('OnShortcut', 'SbReplay');
        return;
      }
      if (ctrl && shift && key === 'S') {
        e.preventDefault();
        dotNetRef.invokeMethodAsync('OnShortcut', 'SbSchedule');
        return;
      }
      if (ctrl && shift && key === 'P') {
        e.preventDefault();
        dotNetRef.invokeMethodAsync('OnShortcut', 'SbPeek');
        return;
      }
    }
  });
};

/**
 * Trap keyboard focus inside an element (Tab cycles, focus goes to first focusable).
 */
window.SwebKit.trapFocus = function (element) {
  if (!element) return;
  const selector =
    'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';
  const getFocusables = () =>
    Array.from(element.querySelectorAll(selector)).filter(
      (el) => !el.closest('[hidden]'),
    );
  element._trapHandler = (e) => {
    if (e.key !== 'Tab') return;
    const focusables = getFocusables();
    if (focusables.length === 0) return;
    const first = focusables[0];
    const last = focusables[focusables.length - 1];
    if (e.shiftKey) {
      if (document.activeElement === first) {
        e.preventDefault();
        last.focus();
      }
    } else {
      if (document.activeElement === last) {
        e.preventDefault();
        first.focus();
      }
    }
  };
  element.addEventListener('keydown', element._trapHandler);
  // Focus first focusable element (autofocus attribute handled by browser for inputs)
  const focusables = getFocusables();
  if (focusables.length > 0 && !element.contains(document.activeElement)) {
    focusables[0].focus();
  }
};

/**
 * Release the focus trap on an element.
 */
window.SwebKit.releaseTrap = function (element) {
  if (!element || !element._trapHandler) return;
  element.removeEventListener('keydown', element._trapHandler);
  delete element._trapHandler;
};

/**
 * Scrolls the currently focused command-item row into view inside the results list.
 */
window.SwebKit.scrollFocusedCommandIntoView = function () {
  const focused = document.querySelector('.cp-results .command-item.focused');
  if (focused) focused.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
};

/**
 * Focuses the AKS resource filter input.
 */
window.SwebKit.focusAksFilter = function () {
  const input = document.querySelector(
    '.aks-resource-panel .resource-filter-input',
  );
  if (input) {
    input.focus();
    input.select();
  }
};

/**
 * Triggers a file download from a string of text content.
 * @param {string} filename - Suggested file name.
 * @param {string} mimeType - MIME type (e.g. "application/json").
 * @param {string} content  - Text content to download.
 */
window.SwebKit.getSystemTheme = function () {
  return window.matchMedia &&
    window.matchMedia('(prefers-color-scheme: dark)').matches
    ? 'dark'
    : 'light';
};

window.SwebKit.getBrowserTimezoneOffset = function () {
  return -new Date().getTimezoneOffset();
};

window.SwebKit.downloadText = function (filename, mimeType, content) {
  const blob = new Blob([content], { type: mimeType });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
};

/**
 * Saves the currently focused element and returns a reference to it.
 * Used to restore focus after a modal or dialog closes.
 */
window.SwebKit.saveFocus = function () {
  return document.activeElement || document.body;
};

/**
 * Restores focus to a previously saved element.
 * @param {HTMLElement} element - The element to focus.
 */
window.SwebKit.restoreFocus = function (element) {
  if (element && typeof element.focus === 'function') {
    element.focus();
  }
};

/**
 * Scrolls an element to the bottom.
 * @param {HTMLElement} element
 */
window.SwebKit.scrollToBottom = function (element) {
  if (element) element.scrollTop = element.scrollHeight;
};
