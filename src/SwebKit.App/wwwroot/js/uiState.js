window.SwebKitUi = window.SwebKitUi || {};

window.SwebKitUi.getWindowWidth = function () {
  return window.innerWidth || 0;
};

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
