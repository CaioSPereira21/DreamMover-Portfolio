using System;
using System.Configuration;
using System.IO;

namespace DreamMover.Helpers
{
    public static class MugenPaths
    {
        public static string GetRootPath()
        {
            string configured = ConfigurationManager.AppSettings["MugenRootPath"];
            if (!string.IsNullOrWhiteSpace(configured))
                return RequireDirectory(configured, "Pasta raiz do M.U.G.E.N.");

            string charsPath = ConfigurationManager.AppSettings["MugenCharsPath"];
            if (!string.IsNullOrWhiteSpace(charsPath))
            {
                DirectoryInfo parent = Directory.GetParent(ProjectPaths.Resolve(charsPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (parent != null)
                    return RequireDirectory(parent.FullName, "Pasta raiz do M.U.G.E.N.");
            }

            throw new DirectoryNotFoundException("Configure MugenRootPath ou MugenCharsPath no Web.config.");
        }

        public static string GetStagesPath()
        {
            string configured = ConfigurationManager.AppSettings["MugenStagesPath"];
            string path = string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(GetRootPath(), "stages")
                : configured;

            return RequireDirectory(path, "Pasta stages do M.U.G.E.N.");
        }

        public static string GetCharsPath()
        {
            string configured = ConfigurationManager.AppSettings["MugenCharsPath"];
            string path = string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(GetRootPath(), "chars")
                : configured;

            return RequireDirectory(path, "Pasta chars do M.U.G.E.N.");
        }

        public static string GetExecutablePath()
        {
            string configured = ConfigurationManager.AppSettings["MugenExecutablePath"];
            string path = string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(GetRootPath(), "mugen.exe")
                : configured;

            path = ProjectPaths.Resolve(path);
            if (!File.Exists(path))
                throw new FileNotFoundException("Executavel do M.U.G.E.N. nao encontrado.", path);

            return ProjectPaths.Resolve(path);
        }

        private static string RequireDirectory(string path, string description)
        {
            string fullPath = ProjectPaths.Resolve(path);
            if (!Directory.Exists(fullPath))
                throw new DirectoryNotFoundException(description + " nao encontrada: " + fullPath);

            return fullPath;
        }
    }
}
