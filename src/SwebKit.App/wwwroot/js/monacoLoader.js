window.SwebKitUi = window.SwebKitUi || {};

window.SwebKitUi.ensureMonacoLoaded = (() => {
  let loadPromise;

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

  return () => {
    if (window.monaco && window.BlazorMonaco) {
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
        .catch((error) => {
          loadPromise = undefined;
          throw error;
        });
    }

    return loadPromise;
  };
})();
