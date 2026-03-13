namespace Motus.Recorder.ActionCapture;

/// <summary>
/// Contains the JavaScript injection script that captures DOM events
/// and reports them to C# via the recorder binding.
/// </summary>
internal static class RecorderScript
{
    /// <summary>
    /// The JS source to inject via Page.addScriptToEvaluateOnNewDocument.
    /// Captures click, input, keydown, change, scroll, and blur events.
    /// </summary>
    internal static string GetSource(string bindingName) => $$"""
    (() => {
        if (window.__motus_recorder_installed__) return;
        window.__motus_recorder_installed__ = true;

        const binding = window['{{bindingName}}'];
        if (!binding) return;

        function getModifiers(e) {
            return (e.altKey ? 1 : 0) | (e.ctrlKey ? 2 : 0) | (e.metaKey ? 4 : 0) | (e.shiftKey ? 8 : 0);
        }

        function getElementInfo(el) {
            if (!el || !el.tagName) return {};
            const info = { tagName: el.tagName };
            if (el.type) info.inputType = el.type;
            return info;
        }

        document.addEventListener('mousedown', (e) => {
            binding(JSON.stringify({
                type: 'mousedown',
                timestamp: Date.now(),
                x: e.clientX,
                y: e.clientY,
                button: e.button === 0 ? 'left' : e.button === 1 ? 'middle' : 'right',
                clickCount: e.detail,
                modifiers: getModifiers(e),
                pageUrl: location.href,
                ...getElementInfo(e.target)
            }));
        }, true);

        document.addEventListener('mouseup', (e) => {
            binding(JSON.stringify({
                type: 'mouseup',
                timestamp: Date.now(),
                x: e.clientX,
                y: e.clientY,
                button: e.button === 0 ? 'left' : e.button === 1 ? 'middle' : 'right',
                modifiers: getModifiers(e),
                pageUrl: location.href,
                ...getElementInfo(e.target)
            }));
        }, true);

        document.addEventListener('input', (e) => {
            binding(JSON.stringify({
                type: 'input',
                timestamp: Date.now(),
                x: e.target.getBoundingClientRect?.()?.x,
                y: e.target.getBoundingClientRect?.()?.y,
                value: e.target.value ?? '',
                pageUrl: location.href,
                ...getElementInfo(e.target)
            }));
        }, true);

        document.addEventListener('keydown', (e) => {
            binding(JSON.stringify({
                type: 'keydown',
                timestamp: Date.now(),
                key: e.key,
                code: e.code,
                modifiers: getModifiers(e),
                pageUrl: location.href,
                ...getElementInfo(e.target)
            }));
        }, true);

        document.addEventListener('change', (e) => {
            const payload = {
                type: 'change',
                timestamp: Date.now(),
                x: e.target.getBoundingClientRect?.()?.x,
                y: e.target.getBoundingClientRect?.()?.y,
                pageUrl: location.href,
                ...getElementInfo(e.target)
            };

            if (e.target.tagName === 'SELECT') {
                payload.selectedValues = Array.from(e.target.selectedOptions).map(o => o.value);
            } else if (e.target.type === 'checkbox' || e.target.type === 'radio') {
                payload.checked = e.target.checked;
            }

            binding(JSON.stringify(payload));
        }, true);

        let scrollTimeout;
        document.addEventListener('scroll', () => {
            clearTimeout(scrollTimeout);
            scrollTimeout = setTimeout(() => {
                binding(JSON.stringify({
                    type: 'scroll',
                    timestamp: Date.now(),
                    scrollX: window.scrollX,
                    scrollY: window.scrollY,
                    pageUrl: location.href
                }));
            }, 50);
        }, true);

        document.addEventListener('blur', (e) => {
            binding(JSON.stringify({
                type: 'blur',
                timestamp: Date.now(),
                pageUrl: location.href,
                ...getElementInfo(e.target)
            }));
        }, true);
    })();
    """;
}
