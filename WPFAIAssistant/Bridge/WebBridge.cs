using System.Runtime.InteropServices;

namespace WPFAIAssistant.Bridge
{
    /// <summary>
    /// This object is injected into the WebView2 page as window.bridge.
    /// JavaScript calls methods on it to communicate with WPF.
    /// </summary>
    [ClassInterface(ClassInterfaceType.AutoDual)]
    [ComVisible(true)]
    public class WebBridge
    {
        /// <summary>Raised when the user submits input from the JS side.</summary>
        public event Action<string>? InputReceived;

        /// <summary>Called from JavaScript: bridge.sendInput(text)</summary>
        public void SendInput(string text)
        {
            InputReceived?.Invoke(text);
        }

        /// <summary>Called from JavaScript to signal the page is ready.</summary>
        public void PageReady()
        {
            PageReadyReceived?.Invoke();
        }

        public event Action? PageReadyReceived;
    }
}
