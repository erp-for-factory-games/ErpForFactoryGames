// Plan export / import helpers (issue #79).
//
// `downloadJson` writes the supplied text to a blob, triggers a synthetic <a download> click,
// and revokes the URL — the standard browser pattern for "download a string as a file"
// without a server round-trip. Called from Planner.razor via IJSRuntime.
//
// `readFileAsText` reads a single File from a <input type="file"> element as UTF-8 text,
// returning a Promise<string>. Blazor's InputFile streaming works fine for big uploads,
// but plan JSON is small (kilobytes) so the direct FileReader path keeps the C# side
// trivial.
(function () {
    window.erpPlan = window.erpPlan || {};

    window.erpPlan.downloadJson = function (filename, contents) {
        const blob = new Blob([contents], { type: 'application/json;charset=utf-8' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = filename;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        // Revoke after a tick — Safari occasionally needs the URL alive until the
        // download dialog appears.
        setTimeout(() => URL.revokeObjectURL(url), 0);
    };

    window.erpPlan.readFileAsText = function (inputElement) {
        return new Promise((resolve, reject) => {
            if (!inputElement || !inputElement.files || inputElement.files.length === 0) {
                resolve(null);
                return;
            }
            const file = inputElement.files[0];
            const reader = new FileReader();
            reader.onload = () => resolve(reader.result);
            reader.onerror = () => reject(reader.error || new Error('File read failed.'));
            reader.readAsText(file);
        });
    };
})();
