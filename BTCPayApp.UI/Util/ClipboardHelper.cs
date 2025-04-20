using Microsoft.JSInterop;

namespace BTCPayApp.UI.Util
{
    public class ClipboardHelper
    {
        private readonly IJSRuntime _jsRuntime;
        public ClipboardHelper(IJSRuntime jSRuntime)
        {
            _jsRuntime = jSRuntime;
        }

        public async Task CopyToClipboard(string? text)
        {
            await _jsRuntime.InvokeVoidAsync("navigator.clipboard.writeText", text);
        }
    }
}
