// =========================================================================
// auto-save.js
//
// Tiny LocalStorage-backed draft store for the planner (#78). The C# side
// (Planner.razor) owns the serialisation shape; this layer just shuttles
// opaque JSON strings in and out under a caller-supplied key and arranges
// a synchronous flush on page unload.
//
// LocalStorage is intentional for v1: single planner draft per browser,
// well under quota, and getItem/setItem are synchronous which we need for
// the beforeunload flush (IndexedDB's async API would race the unload).
// =========================================================================

(function () {
    if (window.erpAutoSave) return; // idempotent against hot-reload double-load

    const PREFIX = 'erp-draft:';

    // Latest in-memory snapshot per key, captured by Blazor as the user
    // edits. On `beforeunload` we flush it to localStorage synchronously so
    // a fast tab-close doesn't lose the last 500ms of edits sitting in the
    // C# debounce timer.
    const pending = new Map();

    function fullKey(key) {
        return PREFIX + key;
    }

    function saveDraft(key, json) {
        try {
            window.localStorage.setItem(fullKey(key), json);
            pending.delete(key);
            return true;
        } catch (e) {
            // Quota / private-mode / disabled storage - swallow. We treat
            // auto-save as best-effort; the user can still save to server.
            console.warn('erpAutoSave.saveDraft failed', e);
            return false;
        }
    }

    function loadDraft(key) {
        try {
            return window.localStorage.getItem(fullKey(key));
        } catch (e) {
            console.warn('erpAutoSave.loadDraft failed', e);
            return null;
        }
    }

    function clearDraft(key) {
        try {
            window.localStorage.removeItem(fullKey(key));
            pending.delete(key);
            return true;
        } catch (e) {
            console.warn('erpAutoSave.clearDraft failed', e);
            return false;
        }
    }

    // Blazor calls this on every change with the JSON it *would* eventually
    // debounce-save. If the user closes the tab inside the debounce window
    // the beforeunload handler will flush this synchronously below.
    function stagePending(key, json) {
        pending.set(key, json);
    }

    window.addEventListener('beforeunload', function () {
        // Synchronous flush - beforeunload is the last chance and async APIs
        // (IndexedDB, fetch) are unreliable here. localStorage is sync.
        pending.forEach(function (json, key) {
            try {
                window.localStorage.setItem(fullKey(key), json);
            } catch (_) { /* best effort */ }
        });
        pending.clear();
    });

    window.erpAutoSave = {
        saveDraft: saveDraft,
        loadDraft: loadDraft,
        clearDraft: clearDraft,
        stagePending: stagePending,
    };
})();
