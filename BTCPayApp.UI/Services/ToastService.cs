using System;
using System.Threading.Tasks;

namespace BTCPayApp.UI.Services
{
    public class ToastService
    {
        public event Action<string, bool>? OnToastShow;
        public event Action? OnToastHide;

        public void ShowToast(string message, bool isError = false)
        {
            OnToastShow?.Invoke(message, isError);
            
            // Auto-hide after 3 seconds
            _ = Task.Delay(3000).ContinueWith(_ => OnToastHide?.Invoke());
        }

        public void ShowSuccess(string message)
        {
            ShowToast(message, false);
        }

        public void ShowError(string message)
        {
            ShowToast(message, true);
        }

        public void HideToast()
        {
            OnToastHide?.Invoke();
        }
    }
}
