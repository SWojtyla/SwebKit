// Vertical splitter: drag to resize a panel.
// Usage: SwebKitSplitter.init(splitterElement, paneElement, { leftPane, minWidth, maxWidth, initialWidth })
//   leftPane: true  → pane is to the LEFT  of the splitter (drag right = grow)
//   leftPane: false → pane is to the RIGHT of the splitter (drag left  = grow, default)
window.SwebKitSplitter = {
  init: function (splitterEl, paneEl, options) {
    if (
      !splitterEl ||
      !splitterEl.addEventListener ||
      !paneEl ||
      !paneEl.addEventListener
    ) {
      return {
        dispose: function () {},
        getWidth: function () {
          return 0;
        },
      };
    }

    const minW = options?.minWidth ?? 200;
    const maxW = options?.maxWidth ?? 1600;
    const leftPane = options?.leftPane ?? false;
    const initialWidth = options?.initialWidth;
    const dotNetRef = options?.dotNetRef;
    const onWidthChangedMethod =
      options?.onWidthChangedMethod ?? 'OnWidthChanged';
    let startX, startWidth;

    function applyWidth(width) {
      const newWidth = Math.min(maxW, Math.max(minW, width));
      paneEl.style.width = newWidth + 'px';
      paneEl.style.flex = '0 0 ' + newWidth + 'px';
      return newWidth;
    }

    function getWidth() {
      return Math.round(paneEl.getBoundingClientRect().width);
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
      // leftPane: drag right grows; rightPane: drag left grows
      const delta = leftPane ? e.clientX - startX : startX - e.clientX;
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
