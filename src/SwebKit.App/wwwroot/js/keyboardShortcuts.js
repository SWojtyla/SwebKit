window.SwebKit = window.SwebKit || {};

window.SwebKit.registerKeyboardShortcuts = function (dotNetRef) {
    document.addEventListener('keydown', function (e) {
        const ctrl = e.ctrlKey;
        const alt = e.altKey;
        const shift = e.shiftKey;
        const key = e.key;

        // Skip if inside an input/textarea/select (except designated shortcuts)
        const inInput = ['INPUT', 'TEXTAREA', 'SELECT'].includes(document.activeElement?.tagName);

        if (ctrl && key === 'p' && !shift) { e.preventDefault(); dotNetRef.invokeMethodAsync('OnShortcut', 'CommandPalette'); return; }
        if (ctrl && key === '1') { e.preventDefault(); dotNetRef.invokeMethodAsync('OnShortcut', 'NavProjects'); return; }
        if (ctrl && key === '2') { e.preventDefault(); dotNetRef.invokeMethodAsync('OnShortcut', 'NavServiceBus'); return; }
        if (ctrl && key === '3') { e.preventDefault(); dotNetRef.invokeMethodAsync('OnShortcut', 'NavObservability'); return; }
        if (ctrl && key === '4') { e.preventDefault(); dotNetRef.invokeMethodAsync('OnShortcut', 'NavAks'); return; }
        if (ctrl && key === ',') { e.preventDefault(); dotNetRef.invokeMethodAsync('OnShortcut', 'NavSettings'); return; }
        if (ctrl && key === 'Tab' && !shift) { e.preventDefault(); dotNetRef.invokeMethodAsync('OnShortcut', 'TabNext'); return; }
        if (ctrl && key === 'Tab' && shift) { e.preventDefault(); dotNetRef.invokeMethodAsync('OnShortcut', 'TabPrev'); return; }
        if (ctrl && key === 'w') { e.preventDefault(); dotNetRef.invokeMethodAsync('OnShortcut', 'TabClose'); return; }
        if (ctrl && key === '\\') { e.preventDefault(); dotNetRef.invokeMethodAsync('OnShortcut', 'ToggleDetailsPane'); return; }
        if (key === 'F5') { e.preventDefault(); dotNetRef.invokeMethodAsync('OnShortcut', 'Refresh'); return; }
        if (key === 'Escape') { dotNetRef.invokeMethodAsync('OnShortcut', 'Escape'); return; }
        if (ctrl && key === 'Enter') { e.preventDefault(); dotNetRef.invokeMethodAsync('OnShortcut', 'Execute'); return; }
        if (ctrl && key === 'f' && !inInput) { e.preventDefault(); dotNetRef.invokeMethodAsync('OnShortcut', 'FocusFilter'); return; }
        if (alt && key === '1') { e.preventDefault(); dotNetRef.invokeMethodAsync('OnShortcut', 'EnvDev'); return; }
        if (alt && key === '2') { e.preventDefault(); dotNetRef.invokeMethodAsync('OnShortcut', 'EnvTest'); return; }
        if (alt && key === '3') { e.preventDefault(); dotNetRef.invokeMethodAsync('OnShortcut', 'EnvAcc'); return; }
        if (alt && key === '4') { e.preventDefault(); dotNetRef.invokeMethodAsync('OnShortcut', 'EnvProd'); return; }
        if (alt && shift && key === 'P') { e.preventDefault(); dotNetRef.invokeMethodAsync('OnShortcut', 'FocusProjectSelector'); return; }
    });
};
