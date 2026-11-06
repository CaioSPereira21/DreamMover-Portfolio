using System;
using System.IO;

namespace DreamMover.Helpers
{
    public static class ProjectPaths
    {
        public static string Resolve(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            string expanded = Environment.ExpandEnvironmentVariables(path.Trim());
            return Path.GetFullPath(Path.IsPathRooted(expanded)
                ? expanded
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, expanded));
        }
    }
}
