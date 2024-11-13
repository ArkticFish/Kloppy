using System.Runtime.InteropServices;
using System.Text;

namespace Kloppy
{
    public static class ClipboardHelper
    {
        private const uint CF_HDROP = 15;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool IsClipboardFormatAvailable(uint format);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetClipboardData(uint uFormat);

        [DllImport("shell32.dll")]
        private static extern uint DragQueryFile(IntPtr hDrop, uint iFile, StringBuilder lpszFile, int cch);

        public static List<string> GetFileDropList()
        {
            if (!IsClipboardFormatAvailable(CF_HDROP))
            {
                return null;
            }

            if (!OpenClipboard(IntPtr.Zero))
            {
                return null;
            }

            try
            {
                IntPtr hDrop = GetClipboardData(CF_HDROP);
                if (hDrop == IntPtr.Zero)
                {
                    return null;
                }

                uint fileCount = DragQueryFile(hDrop, 0xFFFFFFFF, null, 0);
                List<string> files = new List<string>();

                for (uint i = 0; i < fileCount; i++)
                {
                    StringBuilder fileName = new StringBuilder(260);
                    if (DragQueryFile(hDrop, i, fileName, fileName.Capacity) > 0)
                    {
                        files.Add(fileName.ToString());
                    }
                }

                return files;
            }
            finally
            {
                CloseClipboard();
            }
        }
    }
}
