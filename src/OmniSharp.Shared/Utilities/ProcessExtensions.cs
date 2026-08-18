using System;
using System.Diagnostics;

namespace OmniSharp.Utilities
{
    public static class ProcessExtensions
    {
        public static void OnExit(this Process process, Action action)
        {
            process.Exited += (sender, e) =>
            {
                action();
            };
        }

        public static void KillChildrenAndThis(this Process process)
        {
            process.Kill(entireProcessTree: true);
        }
    }
}
