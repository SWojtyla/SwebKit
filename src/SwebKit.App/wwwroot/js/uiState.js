window.SwebKitUi = {
  getLocalStorage: function (key) {
    try {
      return window.localStorage.getItem(key);
    } catch {
      return null;
    }
  },
  setLocalStorage: function (key, value) {
    try {
      window.localStorage.setItem(key, value);
    } catch {
      // Ignore storage failures (private mode, quota).
    }
  },
  getWindowWidth: function () {
    return window.innerWidth || 0;
  },
};
