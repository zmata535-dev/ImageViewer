using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace CleanImageViewer
{
    /// <summary>
    /// Natural alphanumeric string comparer matching Windows Explorer sorting (e.g. img2 comes before img10).
    /// </summary>
    public class NaturalStringComparer : IComparer<string>
    {
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int StrCmpLogicalW(string x, string y);

        public int Compare(string x, string y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            try
            {
                return StrCmpLogicalW(x, y);
            }
            catch
            {
                return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
