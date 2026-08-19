using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace VoiceToTextPro.Services
{
    public interface IKeystrokeInjector
    {
        Task InjectTextAsync(string text);
        Task InjectKeyAsync(ushort vkCode);
    }

    public class KeystrokeInjectorService : IKeystrokeInjector
    {
        private static readonly Lazy<KeystrokeInjectorService> s_instance = new(() => new KeystrokeInjectorService());
        public static KeystrokeInjectorService Instance => s_instance.Value;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYDOWN = 0x0000;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_UNICODE = 0x0004;

        public Task InjectTextAsync(string text)
        {
            if (string.IsNullOrEmpty(text)) return Task.CompletedTask;

            return Task.Run(() =>
            {
                try
                {
                    IntPtr handle = GetForegroundWindow();
                    GetWindowThreadProcessId(handle, out uint processId);

                    // Safety check: Avoid injecting into VoiceToTextPro itself
                    uint currentPid = (uint)Environment.ProcessId;
                    if (processId == currentPid)
                    {
                        LoggerService.InfoLocalized("Log_INJECTOR_423967", "صرف‌نظر از تزریق کلید: پنجره فعال، خود VoiceToTextPro است.", "INJECTOR");
                        return;
                    }

                    foreach (char c in text)
                    {
                        INPUT[] inputs = new INPUT[2];

                        // Key down
                        inputs[0] = new INPUT
                        {
                            type = INPUT_KEYBOARD,
                            U = new InputUnion
                            {
                                ki = new KEYBDINPUT
                                {
                                    wVk = 0,
                                    wScan = (ushort)c,
                                    dwFlags = KEYEVENTF_UNICODE,
                                    time = 0,
                                    dwExtraInfo = IntPtr.Zero
                                }
                            }
                        };

                        // Key up
                        inputs[1] = new INPUT
                        {
                            type = INPUT_KEYBOARD,
                            U = new InputUnion
                            {
                                ki = new KEYBDINPUT
                                {
                                    wVk = 0,
                                    wScan = (ushort)c,
                                    dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP,
                                    time = 0,
                                    dwExtraInfo = IntPtr.Zero
                                }
                            }
                        };

                        SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
                    }

                    LoggerService.InfoLocalized("Log_INJECTOR_382167", "تزریق مستقیم متن با موفقیت انجام شد ({0} کاراکتر)", "INJECTOR", text.Length);
                }
                catch (Exception ex)
                {
                    LoggerService.ErrorLocalized("Log_INJECTOR_351fd2", "خطا در تزریق مستقیم کلید: {0}", "INJECTOR", ex.Message);
                }
            });
        }

        public Task InjectKeyAsync(ushort vkCode)
        {
            return Task.Run(() =>
            {
                try
                {
                    INPUT[] inputs = new INPUT[2];

                    inputs[0] = new INPUT
                    {
                        type = INPUT_KEYBOARD,
                        U = new InputUnion
                        {
                            ki = new KEYBDINPUT
                            {
                                wVk = vkCode,
                                wScan = 0,
                                dwFlags = KEYEVENTF_KEYDOWN,
                                time = 0,
                                dwExtraInfo = IntPtr.Zero
                            }
                        }
                    };

                    inputs[1] = new INPUT
                    {
                        type = INPUT_KEYBOARD,
                        U = new InputUnion
                        {
                            ki = new KEYBDINPUT
                            {
                                wVk = vkCode,
                                wScan = 0,
                                dwFlags = KEYEVENTF_KEYUP,
                                time = 0,
                                dwExtraInfo = IntPtr.Zero
                            }
                        }
                    };

                    SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
                }
                catch (Exception ex)
                {
                    LoggerService.ErrorLocalized("Log_INJECTOR_15eb08", "خطا در تزریق کلید مجازی: {0}", "INJECTOR", ex.Message);
                }
            });
        }
    }
}
