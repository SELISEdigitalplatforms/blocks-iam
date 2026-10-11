(function () {
  var meta = document.querySelector('meta[name="csp-nonce"]');
  var nonce = meta ? meta.nonce || meta.getAttribute("nonce") : "";
  if (!nonce || nonce.indexOf("__") === 0) return;

  window.__webpack_nonce__ = nonce;

  var createElement = Document.prototype.createElement;
  Document.prototype.createElement = function (tagName) {
    var element = createElement.apply(this, arguments);
    if (typeof tagName === "string" && tagName.toLowerCase() === "style") {
      element.setAttribute("nonce", nonce);
    }
    return element;
  };
})();
