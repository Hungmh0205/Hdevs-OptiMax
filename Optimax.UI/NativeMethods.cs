using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Optimax.UI
{
    public static class NativeMethods
    {
        [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

        public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        public const int DWMWA_MICA_EFFECT = 1029;
        public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

        public const int DWMSBT_MAINWINDOW = 2; // Mica
        public const int DWMSBT_TRANSIENTWINDOW = 3; // Acrylic
        public const int DWMSBT_TABBEDWINDOW = 4; // Mica Alt

        public static bool EnableMicaBackdrop(Window window, bool isDarkMode = true)
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero) return false;

                int darkMode = isDarkMode ? 1 : 0;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));

                int backdropType = DWMSBT_MAINWINDOW;
                int res = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int));
                if (res != 0)
                {
                    int micaOld = 1;
                    DwmSetWindowAttribute(hwnd, DWMWA_MICA_EFFECT, ref micaOld, sizeof(int));
                }

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
