using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ServerInfoSubmitter
{
    internal static class WinFormsHelper
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wp, string lp);

        private const uint EM_SETCUEBANNER = 0x1501;

        // Sets grey placeholder / cue-banner text in a TextBox (Net Framework 4.x equivalent of PlaceholderText)
        public static void SetCueBanner(TextBox tb, string hint)
        {
            void Apply() => SendMessage(tb.Handle, EM_SETCUEBANNER, (IntPtr)1, hint);

            if (tb.IsHandleCreated) Apply();
            else tb.HandleCreated += (s, e) => Apply();
        }
    }
}
