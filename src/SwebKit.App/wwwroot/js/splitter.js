// Horizontal splitter: drag to resize the right pane width.
// Usage: SwebKitSplitter.init(splitterElement, rightPaneElement, { minWidth, maxWidth })
window.SwebKitSplitter = {
    init: function (splitterEl, rightPaneEl, options) {
        const minW = options?.minWidth ?? 200;
        const maxW = options?.maxWidth ?? 800;
        let startX, startWidth;

        function onMouseDown(e) {
            e.preventDefault();
            startX = e.clientX;
            startWidth = rightPaneEl.getBoundingClientRect().width;
            document.addEventListener('mousemove', onMouseMove);
            document.addEventListener('mouseup', onMouseUp);
            document.body.style.cursor = 'col-resize';
            document.body.style.userSelect = 'none';
            splitterEl.classList.add('active');
        }

        function onMouseMove(e) {
            // Dragging left increases right pane width
            const delta = startX - e.clientX;
            const newWidth = Math.min(maxW, Math.max(minW, startWidth + delta));
            rightPaneEl.style.width = newWidth + 'px';
            rightPaneEl.style.flex = '0 0 ' + newWidth + 'px';
        }

        function onMouseUp() {
            document.removeEventListener('mousemove', onMouseMove);
            document.removeEventListener('mouseup', onMouseUp);
            document.body.style.cursor = '';
            document.body.style.userSelect = '';
            splitterEl.classList.remove('active');
        }

        splitterEl.addEventListener('mousedown', onMouseDown);

        // Return a dispose handle
        return {
            dispose: function () {
                splitterEl.removeEventListener('mousedown', onMouseDown);
            }
        };
    }
};
