window.SwebKitUi = window.SwebKitUi || {};

window.SwebKitUi.ensureMonacoLoaded = (() => {
  let loadPromise;
  let themesRegistered = false;

  function ensureScript(src) {
    const existing = document.querySelector(`script[src="${src}"]`);
    if (existing) {
      if (existing.dataset.loaded === 'true') {
        return Promise.resolve();
      }

      return new Promise((resolve, reject) => {
        existing.addEventListener(
          'load',
          () => {
            existing.dataset.loaded = 'true';
            resolve();
          },
          { once: true },
        );
        existing.addEventListener(
          'error',
          () => reject(new Error(`Failed to load script: ${src}`)),
          { once: true },
        );
      });
    }

    return new Promise((resolve, reject) => {
      const script = document.createElement('script');
      script.src = src;
      script.async = false;
      script.addEventListener(
        'load',
        () => {
          script.dataset.loaded = 'true';
          resolve();
        },
        { once: true },
      );
      script.addEventListener(
        'error',
        () => reject(new Error(`Failed to load script: ${src}`)),
        { once: true },
      );
      document.body.appendChild(script);
    });
  }

  function getAppTheme() {
    const el = document.querySelector('[data-theme]');
    return el?.getAttribute('data-theme') ?? 'dark';
  }

  function registerThemes() {
    if (themesRegistered || !window.monaco) return;
    themesRegistered = true;

    monaco.editor.defineTheme('swebkit-light', {
      base: 'vs',
      inherit: true,
      rules: [
        { token: '', foreground: '243142', background: 'F7F9FC' },
        { token: 'keyword', foreground: '2F80ED', fontStyle: 'bold' },
        { token: 'string', foreground: '2E8B57' },
        { token: 'number', foreground: 'B26A00' },
        { token: 'comment', foreground: '8A97A8', fontStyle: 'italic' },
        { token: 'type', foreground: '7A3E9D' },
        { token: 'delimiter', foreground: '6B7785' },
        { token: 'key', foreground: '314056' },
      ],
      colors: {
        'editor.background': '#F7F9FC',
        'editor.foreground': '#243142',
        'editorLineNumber.foreground': '#8A97A8',
        'editorLineNumber.activeForeground': '#42556E',
        'editorCursor.foreground': '#1F6FEB',
        'editor.selectionBackground': '#CFE3FF',
        'editor.inactiveSelectionBackground': '#E5F0FF',
        'editor.lineHighlightBackground': '#EEF3F9',
        'editorIndentGuide.background1': '#D8E0EA',
        'editorIndentGuide.activeBackground1': '#A8B8CC',
        'editorWhitespace.foreground': '#D8E0EA',
        'editorBracketMatch.background': '#E8F1FF',
        'editorBracketMatch.border': '#7BA7F7',
        'editorGutter.background': '#F7F9FC',
      },
    });

    monaco.editor.defineTheme('swebkit-dark', {
      base: 'vs-dark',
      inherit: true,
      rules: [
        { token: '', foreground: 'D7E0EA', background: '151A21' },
        { token: 'keyword', foreground: '6CB6FF', fontStyle: 'bold' },
        { token: 'string', foreground: '8BD49C' },
        { token: 'number', foreground: 'F2B866' },
        { token: 'comment', foreground: '6F8096', fontStyle: 'italic' },
        { token: 'type', foreground: 'C792EA' },
        { token: 'delimiter', foreground: '93A4B8' },
        { token: 'key', foreground: 'C9D5E4' },
      ],
      colors: {
        'editor.background': '#151A21',
        'editor.foreground': '#D7E0EA',
        'editorLineNumber.foreground': '#607289',
        'editorLineNumber.activeForeground': '#AFC0D4',
        'editorCursor.foreground': '#7CC4FF',
        'editor.selectionBackground': '#264F78',
        'editor.inactiveSelectionBackground': '#213246',
        'editor.lineHighlightBackground': '#212B3A',
        'editorIndentGuide.background1': '#2B3647',
        'editorIndentGuide.activeBackground1': '#49617D',
        'editorWhitespace.foreground': '#2B3647',
        'editorBracketMatch.background': '#22354A',
        'editorBracketMatch.border': '#5D9CEC',
        'editorGutter.background': '#151A21',
      },
    });
  }

  function applyTheme() {
    if (!window.monaco) return;
    registerThemes();
    const isDark = !getAppTheme().startsWith('light');
    monaco.editor.setTheme(isDark ? 'swebkit-dark' : 'swebkit-light');
  }

  // Exposed so Blazor can call it after an editor is initialised
  window.SwebKitUi.applyMonacoTheme = applyTheme;

  return () => {
    if (window.monaco && window.BlazorMonaco) {
      // Monaco already loaded — ensure correct theme for any new editor
      applyTheme();
      return Promise.resolve();
    }

    if (!loadPromise) {
      loadPromise = ensureScript('_content/BlazorMonaco/jsInterop.js')
        .then(() =>
          ensureScript(
            '_content/BlazorMonaco/lib/monaco-editor/min/vs/loader.js',
          ),
        )
        .then(() =>
          ensureScript(
            '_content/BlazorMonaco/lib/monaco-editor/min/vs/editor/editor.main.js',
          ),
        )
        .then(() => {
          applyTheme();
          // Watch the entire document for data-theme attribute changes
          new MutationObserver(applyTheme).observe(document.body, {
            subtree: true,
            attributes: true,
            attributeFilter: ['data-theme'],
          });
        })
        .catch((error) => {
          loadPromise = undefined;
          throw error;
        });
    }

    return loadPromise;
  };
})();
