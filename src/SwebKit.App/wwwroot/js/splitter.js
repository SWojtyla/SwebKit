// Horizontal splitter: drag to resize the right pane width.
// Usage: SwebKitSplitter.init(splitterElement, rightPaneElement, { minWidth, maxWidth })
window.SwebKitSplitter = {
  init: function (splitterEl, rightPaneEl, options) {
    const minW = options?.minWidth ?? 200;
    const maxW = options?.maxWidth ?? 800;
    const initialWidth = options?.initialWidth;
    const dotNetRef = options?.dotNetRef;
    const onWidthChangedMethod =
      options?.onWidthChangedMethod ?? 'OnWidthChanged';
    let startX, startWidth;

    function applyWidth(width) {
      const newWidth = Math.min(maxW, Math.max(minW, width));
      rightPaneEl.style.width = newWidth + 'px';
      rightPaneEl.style.flex = '0 0 ' + newWidth + 'px';
      return newWidth;
    }

    function getWidth() {
      return Math.round(rightPaneEl.getBoundingClientRect().width);
    }

    function notifyWidthChanged() {
      if (!dotNetRef || !onWidthChangedMethod) {
        return;
      }

      dotNetRef
        .invokeMethodAsync(onWidthChangedMethod, getWidth())
        .catch(function () {
          // The .NET side may already be gone during teardown.
        });
    }

    if (typeof initialWidth === 'number' && Number.isFinite(initialWidth)) {
      applyWidth(initialWidth);
    }

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
      // Dragging left increases right pane width
      const delta = startX - e.clientX;
      applyWidth(startWidth + delta);
    }

    function onMouseUp() {
      document.removeEventListener('mousemove', onMouseMove);
      document.removeEventListener('mouseup', onMouseUp);
      document.body.style.cursor = '';
      document.body.style.userSelect = '';
      splitterEl.classList.remove('active');
      notifyWidthChanged();
    }

    splitterEl.addEventListener('mousedown', onMouseDown);

    // Return a dispose handle
    return {
      dispose: function () {
        splitterEl.removeEventListener('mousedown', onMouseDown);
      },
      getWidth: function () {
        return getWidth();
      },
    };
  },
};
