using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace CleanImageViewer
{
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            Application app = new Application();

            string initialFile = null;
            if (args != null && args.Length > 0)
            {
                string candidate = args[0];
                if (File.Exists(candidate))
                {
                    initialFile = Path.GetFullPath(candidate);
                }
            }

            MainWindow win = new MainWindow(initialFile);

            // Set window icon from app.ico or executable
            try
            {
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                string iconPath = Path.Combine(appDir, "app.ico");
                if (File.Exists(iconPath))
                {
                    win.Icon = BitmapFrame.Create(new Uri(Path.GetFullPath(iconPath)));
                }
                else
                {
                    using (System.Drawing.Icon sysIcon = System.Drawing.Icon.ExtractAssociatedIcon(Process.GetCurrentProcess().MainModule.FileName))
                    {
                        if (sysIcon != null)
                        {
                            win.Icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                                sysIcon.Handle,
                                Int32Rect.Empty,
                                BitmapSizeOptions.FromEmptyOptions());
                        }
                    }
                }
            }
            catch { }

            app.Run(win);
        }
    }
}
