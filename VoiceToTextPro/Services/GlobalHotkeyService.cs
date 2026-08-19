using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace VoiceToTextPro.Services
{
    public class GlobalHotkeyService : IDisposable
    {
        private const int HOTKEY_ID = 9000;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint VK_SPACE = 0x20;
        private const int WM_HOTKEY = 0x0312;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private IntPtr _hWnd;
        private HwndSource? _source;
        private Action? _onHotkeyPressed;

        public void Register(Window window, Action onHotkeyPressed)
        {
            _onHotkeyPressed = onHotkeyPressed;
            _hWnd = new WindowInteropHelper(window).Handle;
            _source = HwndSource.FromHwnd(_hWnd);
            _source?.AddHook(HwndHook);

            bool registered = RegisterHotKey(_hWnd, HOTKEY_ID, MOD_CONTROL | MOD_SHIFT, VK_SPACE);
            if (registered)
            {
                LoggerService.InfoLocalized("Log_HotkeyRegistered", "کلید میانبر سراسری Ctrl+Shift+Space جهت ویجت سریع ثبت شد.", "HOTKEY");
            }
            else
            {
                LoggerService.WarnLocalized("Log_HotkeyFailed", "ثبت کلید میانبر سراسری با خطا مواجه شد.", "HOTKEY");
            }
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                _onHotkeyPressed?.Invoke();
                handled = true;
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            if (_hWnd != IntPtr.Zero)
            {
                UnregisterHotKey(_hWnd, HOTKEY_ID);
                _source?.RemoveHook(HwndHook);
                _hWnd = IntPtr.Zero;
            }
        }
    }
}
