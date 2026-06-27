namespace Motus;

/// <summary>
/// The page init script that draws an on-screen pseudo-cursor. Synthetic input dispatched over
/// the DevTools protocol never moves the OS pointer, and screencast capture records only the page
/// surface, so a recording or screenshot shows no cursor by default. This overlay listens to the
/// real DOM mouse events that synthetic input produces, draws a cursor sprite that follows the
/// pointer and reflects the element's CSS cursor style, and renders a click effect. It runs in the
/// top frame only and is injected once per context when <see cref="Abstractions.ContextOptions.ShowCursor"/> is set.
/// </summary>
internal static class CursorOverlayScript
{
    public const string Js = """
(function () {
  if (window.top !== window.self) return;        // top frame only; avoids duplicate cursors in iframes
  if (window.__motusCursor) return;              // inject once
  window.__motusCursor = true;

  var NS = "http://www.w3.org/2000/svg";
  // Sprites are drawn dark with a light outline so they read on any background. Each entry is
  // [svgMarkup, hotspotX, hotspotY] where the hotspot is the active point within a 28x28 box.
  function svg(inner) {
    return "data:image/svg+xml;utf8,<svg xmlns='" + NS + "' width='28' height='28' viewBox='0 0 28 28'>" + inner + "</svg>";
  }
  var stroke = "stroke='white' stroke-width='1.4' stroke-linejoin='round' stroke-linecap='round'";
  var SPRITES = {
    arrow:      [svg("<path d='M5 3 L5 22 L10 17 L13 23 L16 21.5 L13 16 L20 16 Z' fill='black' " + stroke + "/>"), 5, 3],
    hand:       [svg("<path d='M10 13 V5 a1.6 1.6 0 0 1 3.2 0 V12 V9 a1.6 1.6 0 0 1 3.2 0 v3 a1.6 1.6 0 0 1 3.2 0 v2 a1.6 1.6 0 0 1 3.2 0 v3 a5 5 0 0 1 -5 5 h-3.4 a5 5 0 0 1 -3.7 -1.7 l-4.2 -5.2 a1.7 1.7 0 0 1 2.5 -2.2 l1 1 Z' fill='black' " + stroke + "/>"), 10, 3],
    ibeam:      [svg("<g fill='none' " + stroke + " stroke-width='1.6'><path d='M14 4 V24'/><path d='M11 4 H17'/><path d='M11 24 H17'/></g>"), 14, 14],
    notallowed: [svg("<g fill='none' stroke='black' stroke-width='2.4'><circle cx='14' cy='14' r='9'/><path d='M8 8 L20 20'/></g><g fill='none' stroke='white' stroke-width='0.8'><circle cx='14' cy='14' r='9'/><path d='M8 8 L20 20'/></g>"), 14, 14],
    crosshair:  [svg("<g fill='none' stroke='black' stroke-width='1.8'><path d='M14 4 V24'/><path d='M4 14 H24'/></g><g fill='none' stroke='white' stroke-width='0.6'><path d='M14 4 V24'/><path d='M4 14 H24'/></g>"), 14, 14],
    move:       [svg("<g fill='black' " + stroke + "><path d='M14 3 L17 7 H11 Z'/><path d='M14 25 L11 21 H17 Z'/><path d='M3 14 L7 11 V17 Z'/><path d='M25 14 L21 17 V11 Z'/><path d='M13 7 H15 V21 H13 Z'/><path d='M7 13 H21 V15 H7 Z'/></g>"), 14, 14],
    wait:       [svg("<g fill='black' " + stroke + "><path d='M8 4 H20 L14 13 Z'/><path d='M8 24 H20 L14 15 Z'/></g><g fill='none' stroke='white' stroke-width='1'><path d='M8 4 H20'/><path d='M8 24 H20'/></g>"), 14, 14]
  };

  function spriteFor(keyword) {
    switch (keyword) {
      case "pointer": return SPRITES.hand;
      case "text": case "vertical-text": return SPRITES.ibeam;
      case "not-allowed": case "no-drop": return SPRITES.notallowed;
      case "grab": case "grabbing": return SPRITES.hand;
      case "move": case "all-scroll": return SPRITES.move;
      case "crosshair": case "cell": return SPRITES.crosshair;
      case "wait": case "progress": return SPRITES.wait;
      default: return SPRITES.arrow;
    }
  }

  // Cursor element. pointer-events:none so it never intercepts input and elementFromPoint
  // looks past it. Top-left of the sprite is offset by the active hotspot.
  var cursor = document.createElement("div");
  cursor.setAttribute("aria-hidden", "true");
  cursor.setAttribute("data-motus-cursor", "");
  var cs = cursor.style;
  cs.position = "fixed"; cs.left = "0"; cs.top = "0";
  cs.width = "28px"; cs.height = "28px";
  cs.zIndex = "2147483647";
  cs.pointerEvents = "none";
  cs.backgroundRepeat = "no-repeat";
  cs.backgroundSize = "28px 28px";
  cs.opacity = "0";                              // hidden until the first move
  cs.transition = "opacity 120ms ease";
  cs.willChange = "transform";

  var mounted = false;
  function mount() {
    if (mounted) return;
    var root = document.documentElement || document.body;
    if (!root) { requestAnimationFrame(mount); return; }
    root.appendChild(cursor);
    mounted = true;
  }
  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", mount, { once: true });
    mount();                                     // documentElement usually already exists
  } else {
    mount();
  }

  var x = 0, y = 0, currentKey = "";
  var rafPending = false;

  function applyHotspot(sprite) {
    cursor.style.backgroundImage = "url(\"" + sprite[0] + "\")";
    cursor.__hx = sprite[1]; cursor.__hy = sprite[2];
  }
  applyHotspot(SPRITES.arrow);

  function render() {
    cursor.style.transform = "translate(" + (x - (cursor.__hx || 0)) + "px," + (y - (cursor.__hy || 0)) + "px)";
  }

  function refreshStyle() {
    rafPending = false;
    var el = document.elementFromPoint(x, y);
    var raw = "auto";
    if (el) { try { raw = getComputedStyle(el).cursor || "auto"; } catch (e) {} }

    // Honor a custom url(...) cursor when present, otherwise map the keyword to a sprite.
    var urlMatch = raw.match(/url\((['"]?)([^'")]+)\1\)(?:\s+(\d+)\s+(\d+))?/);
    if (urlMatch) {
      var key = "url:" + urlMatch[2];
      if (key !== currentKey) {
        currentKey = key;
        cursor.setAttribute("data-cursor-key", "url");
        cursor.style.backgroundImage = "url(\"" + urlMatch[2] + "\")";
        cursor.__hx = urlMatch[3] ? parseInt(urlMatch[3], 10) : 0;
        cursor.__hy = urlMatch[4] ? parseInt(urlMatch[4], 10) : 0;
        render();
      }
      return;
    }
    var kw = raw.split(",").pop().trim().split(/\s+/)[0];
    if (kw !== currentKey) {
      currentKey = kw;
      cursor.setAttribute("data-cursor-key", kw);
      applyHotspot(spriteFor(kw));
      render();
    }
  }

  function onMove(e) {
    x = e.clientX; y = e.clientY;
    if (cursor.style.opacity !== "1") cursor.style.opacity = "1";
    render();
    if (!rafPending) { rafPending = true; requestAnimationFrame(refreshStyle); }
    if (pressed) trail();
  }

  // Click effects. A ripple ring expands and fades from the press point, colored by button.
  // While a button is held a marker stays under the cursor and a fading dot follows drags.
  var pressed = false;
  function buttonColor(button) {
    if (button === 2) return "rgba(255,99,99,0.9)";   // right
    if (button === 1) return "rgba(255,209,102,0.9)";  // middle
    return "rgba(64,156,255,0.95)";                    // left
  }
  function ripple(px, py, color, big) {
    var r = document.createElement("div");
    var s = r.style;
    var size = big ? 14 : 10;
    s.position = "fixed"; s.left = (px - size) + "px"; s.top = (py - size) + "px";
    s.width = (size * 2) + "px"; s.height = (size * 2) + "px";
    s.borderRadius = "50%";
    s.border = "2px solid " + color;
    s.boxSizing = "border-box";
    s.zIndex = "2147483646";
    s.pointerEvents = "none";
    (document.documentElement || document.body).appendChild(r);
    var anim = r.animate(
      [{ transform: "scale(0.3)", opacity: 0.9 }, { transform: "scale(" + (big ? 2.6 : 2.0) + ")", opacity: 0 }],
      { duration: big ? 450 : 350, easing: "ease-out" });
    anim.onfinish = function () { r.remove(); };
  }
  function trail() {
    var d = document.createElement("div");
    var s = d.style;
    s.position = "fixed"; s.left = (x - 3) + "px"; s.top = (y - 3) + "px";
    s.width = "6px"; s.height = "6px"; s.borderRadius = "50%";
    s.background = "rgba(64,156,255,0.5)";
    s.zIndex = "2147483645"; s.pointerEvents = "none";
    (document.documentElement || document.body).appendChild(d);
    var anim = d.animate([{ opacity: 0.5 }, { opacity: 0 }], { duration: 300, easing: "ease-out" });
    anim.onfinish = function () { d.remove(); };
  }

  function onDown(e) { pressed = true; ripple(e.clientX, e.clientY, buttonColor(e.button), false); }
  function onUp() { pressed = false; }
  function onDbl(e) { ripple(e.clientX, e.clientY, buttonColor(e.button), true); }

  var opts = { capture: true, passive: true };
  window.addEventListener("mousemove", onMove, opts);
  window.addEventListener("mousedown", onDown, opts);
  window.addEventListener("mouseup", onUp, opts);
  window.addEventListener("dblclick", onDbl, opts);
})();
""";
}
