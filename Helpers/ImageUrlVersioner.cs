using System;
using System.Globalization;
using System.IO;
using System.Web.Hosting;

namespace DreamMover.Helpers
{
    public static class ImageUrlVersioner
    {
        public static string FromImages(string fileName, string fallbackFileName = "Undefined.png")
        {
            string selectedFileName = string.IsNullOrWhiteSpace(fileName)
                ? fallbackFileName
                : fileName;

            selectedFileName = Path.GetFileName(selectedFileName ?? string.Empty);
            if (string.IsNullOrWhiteSpace(selectedFileName))
                return string.Empty;

            string url = "/Imagens/" + Uri.EscapeDataString(selectedFileName);
            return WithVersion(url, "~/Imagens/" + selectedFileName);
        }

        public static string WithVersion(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return url;

            string path = RemoveQueryAndFragment(url);
            string applicationPath = path.StartsWith("~/", StringComparison.Ordinal)
                ? path
                : "~" + (path.StartsWith("/", StringComparison.Ordinal) ? path : "/" + path);

            return WithVersion(path, Uri.UnescapeDataString(applicationPath));
        }

        private static string WithVersion(string url, string applicationPath)
        {
            try
            {
                string physicalPath = HostingEnvironment.MapPath(applicationPath);
                if (string.IsNullOrWhiteSpace(physicalPath))
                {
                    string applicationRoot = AppDomain.CurrentDomain.GetData(".appPath") as string;
                    if (!string.IsNullOrWhiteSpace(applicationRoot))
                    {
                        string relativePath = applicationPath
                            .TrimStart('~', '/', '\\')
                            .Replace('/', Path.DirectorySeparatorChar);
                        physicalPath = Path.Combine(applicationRoot, relativePath);
                    }
                }

                if (!string.IsNullOrWhiteSpace(physicalPath) && File.Exists(physicalPath))
                {
                    string version = File.GetLastWriteTimeUtc(physicalPath).Ticks
                        .ToString(CultureInfo.InvariantCulture);
                    return RemoveQueryAndFragment(url) + "?v=" + version;
                }
            }
            catch (Exception)
            {
                // Fora de um host ASP.NET nao ha caminho fisico para versionar.
            }

            return url;
        }

        private static string RemoveQueryAndFragment(string url)
        {
            int queryIndex = url.IndexOfAny(new[] { '?', '#' });
            return queryIndex >= 0 ? url.Substring(0, queryIndex) : url;
        }
    }
}
