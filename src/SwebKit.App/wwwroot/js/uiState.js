window.SwebKitUi = window.SwebKitUi || {};

window.SwebKitUi.getWindowWidth = function () {
  return window.innerWidth || 0;
};

// Scroll a container element to its bottom (used by the AI Agent chat thread).
window.scrollElementToBottom = function (elementId) {
  var el = document.getElementById(elementId);
  if (el) {
    el.scrollTop = el.scrollHeight;
  }
};

// Agent panel drag-to-resize.
// Sets --agent-panel-width CSS variable on the shell element so the grid column animates.
window.SwebKitAgentPanel = (function () {
  var _disposeHandle = null;

  return {
    initResizer: function (resizerId, shellSelector, options) {
      // Dispose any previous handle before re-initialising.
      if (_disposeHandle) {
        _disposeHandle();
        _disposeHandle = null;
      }

      var splitterEl = document.getElementById(resizerId);
      var shellEl = document.querySelector(shellSelector);
      if (!splitterEl || !shellEl) return;

      var minW = (options && options.minWidth) || 200;
      var maxW = (options && options.maxWidth) || 800;
      var initialWidth = (options && options.initialWidth) || 340;
      var startX, startWidth;

      function getWidth() {
        var val = shellEl.style.getPropertyValue('--agent-panel-width');
        return parseInt(val) || initialWidth;
      }

      function applyWidth(w) {
        var clamped = Math.min(maxW, Math.max(minW, w));
        shellEl.style.setProperty('--agent-panel-width', clamped + 'px');
      }

      // Set initial inline width (takes precedence over class-based CSS var).
      applyWidth(initialWidth);

      function onMouseDown(e) {
        e.preventDefault();
        startX = e.clientX;
        startWidth = getWidth();
        document.addEventListener('mousemove', onMouseMove);
        document.addEventListener('mouseup', onMouseUp);
        document.body.style.cursor = 'col-resize';
        document.body.style.userSelect = 'none';
        splitterEl.classList.add('active');
      }

      function onMouseMove(e) {
        // Drag left = grow (right-side panel).
        var delta = startX - e.clientX;
        applyWidth(startWidth + delta);
      }

      function onMouseUp() {
        document.removeEventListener('mousemove', onMouseMove);
        document.removeEventListener('mouseup', onMouseUp);
        document.body.style.cursor = '';
        document.body.style.userSelect = '';
        splitterEl.classList.remove('active');
      }

      splitterEl.addEventListener('mousedown', onMouseDown);

      _disposeHandle = function () {
        splitterEl.removeEventListener('mousedown', onMouseDown);
        document.removeEventListener('mousemove', onMouseMove);
        document.removeEventListener('mouseup', onMouseUp);
      };
    },

    disposeResizer: function (shellSelector) {
      if (_disposeHandle) {
        _disposeHandle();
        _disposeHandle = null;
      }
      // Remove inline override so CSS class controls width again (triggers close animation).
      var shellEl = shellSelector
        ? document.querySelector(shellSelector)
        : null;
      if (shellEl) {
        shellEl.style.removeProperty('--agent-panel-width');
      }
    },
  };
})();

window.SwebKitUi.downloadTextFile = function (fileName, content, mimeType) {
  var blob = new Blob([content], { type: mimeType });
  var url = URL.createObjectURL(blob);
  var a = document.createElement('a');
  a.href = url;
  a.download = fileName;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
};

window.SwebKitUi.downloadBinaryFile = function (fileName, base64, mimeType) {
  var byteChars = atob(base64);
  var bytes = new Uint8Array(byteChars.length);
  for (var i = 0; i < byteChars.length; i++) {
    bytes[i] = byteChars.charCodeAt(i);
  }
  var blob = new Blob([bytes], { type: mimeType });
  var url = URL.createObjectURL(blob);
  var a = document.createElement('a');
  a.href = url;
  a.download = fileName;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
};

window.SwebKitUi.scrollToBottom = function (element) {
  if (element) {
    element.scrollTop = element.scrollHeight;
  }
};
