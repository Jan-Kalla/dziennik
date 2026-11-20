using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GradebookApp.Services
{
    public static class PdfFileResolver
    {
        private static string? _cachedAppRoot;

        /// <summary>
        /// Zwraca główny folder aplikacji (katalog nadrzędny zawierający bazę gradebook.db lub projekt).
        /// </summary>
        public static string GetApplicationRootDirectory()
        {
            if (!string.IsNullOrEmpty(_cachedAppRoot) && Directory.Exists(_cachedAppRoot))
                return _cachedAppRoot;

            var baseDir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            var dir = baseDir;

            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "gradebook.db")) ||
                    File.Exists(Path.Combine(dir.FullName, "GradebookApp.csproj")) ||
                    Directory.Exists(Path.Combine(dir.FullName, "Pdfs")))
                {
                    _cachedAppRoot = dir.FullName;
                    return dir.FullName;
                }
                dir = dir.Parent;
            }

            _cachedAppRoot = AppDomain.CurrentDomain.BaseDirectory;
            return _cachedAppRoot;
        }

        /// <summary>
        /// Zwraca domyślny folder do przechowywania plików PDF (folder Pdfs w głównym katalogu aplikacji).
        /// </summary>
        public static string GetDefaultPdfsDirectory()
        {
            string appRoot = GetApplicationRootDirectory();
            string target = Path.Combine(appRoot, "Pdfs");
            if (!Directory.Exists(target))
            {
                Directory.CreateDirectory(target);
            }
            return target;
        }

        /// <summary>
        /// Generuje czystą, czytelną i unikalną nazwę pliku bez długiego 30-znakowego prefiksu GUID.
        /// Jeśli plik o danej nazwie już istnieje w folderze docelowym, dodaje standardowy przyrostek (1), (2), itd.
        /// </summary>
        public static string GenerateUniquePdfFileName(string targetFolder, string originalFileName)
        {
            string cleanName = Path.GetFileName(originalFileName);
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                cleanName = cleanName.Replace(c, '_');
            }

            if (string.IsNullOrWhiteSpace(cleanName))
                cleanName = "Dokument.pdf";

            string destPath = Path.Combine(targetFolder, cleanName);
            if (!File.Exists(destPath))
            {
                return cleanName;
            }

            string nameWithoutExt = Path.GetFileNameWithoutExtension(cleanName);
            string ext = Path.GetExtension(cleanName);
            int counter = 1;

            while (File.Exists(Path.Combine(targetFolder, $"{nameWithoutExt} ({counter}){ext}")))
            {
                counter++;
            }

            return $"{nameWithoutExt} ({counter}){ext}";
        }

        /// <summary>
        /// Zwraca listę folderów, w których program szuka plików PDF.
        /// </summary>
        public static List<string> GetCandidateSearchDirectories()
        {
            var list = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string defaultPdfs = GetDefaultPdfsDirectory();
            if (Directory.Exists(defaultPdfs)) list.Add(defaultPdfs);

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            if (Directory.Exists(baseDir)) list.Add(baseDir);

            string basePdfs = Path.Combine(baseDir, "Pdfs");
            if (Directory.Exists(basePdfs)) list.Add(basePdfs);

            string appRoot = GetApplicationRootDirectory();
            if (Directory.Exists(appRoot)) list.Add(appRoot);

            // Sprawdzamy foldery w obrębie bin/Debug, bin/Release
            try
            {
                string binDir = Path.Combine(appRoot, "bin");
                if (Directory.Exists(binDir))
                {
                    var pdfDirs = Directory.GetDirectories(binDir, "Pdfs", SearchOption.AllDirectories);
                    foreach (var d in pdfDirs) list.Add(d);
                }
            }
            catch { }

            return list.ToList();
        }

        /// <summary>
        /// Inteligentnie odnajduje plik PDF na dysku, nawet jeśli ścieżka uległa zmianie,
        /// folder został przeniesiony, plik przeniesiono do podfolderu lub usunięto stary prefiks GUID.
        /// </summary>
        public static string? ResolvePdfPath(string? storedPath, string? originalFileName)
        {
            // 1. Sprawdzenie bezpośredniej ścieżki
            if (!string.IsNullOrWhiteSpace(storedPath) && File.Exists(storedPath))
            {
                return Path.GetFullPath(storedPath);
            }

            string storedFileName = !string.IsNullOrWhiteSpace(storedPath) ? Path.GetFileName(storedPath) : "";
            string origFileName = !string.IsNullOrWhiteSpace(originalFileName) ? Path.GetFileName(originalFileName) : "";

            // Wyciągnięcie nazwy po starym prefiksie (np. GUID_nazwa.pdf)
            string afterUnderscore = "";
            if (!string.IsNullOrEmpty(storedFileName) && storedFileName.Contains('_'))
            {
                afterUnderscore = storedFileName.Substring(storedFileName.IndexOf('_') + 1);
            }

            var searchDirs = GetCandidateSearchDirectories();

            // 2. Szukanie wprost w głównych folderach kandydatów
            foreach (var dir in searchDirs)
            {
                if (!string.IsNullOrEmpty(storedFileName))
                {
                    string candidate = Path.Combine(dir, storedFileName);
                    if (File.Exists(candidate)) return Path.GetFullPath(candidate);
                }

                if (!string.IsNullOrEmpty(origFileName))
                {
                    string candidate = Path.Combine(dir, origFileName);
                    if (File.Exists(candidate)) return Path.GetFullPath(candidate);
                }

                if (!string.IsNullOrEmpty(afterUnderscore))
                {
                    string candidate = Path.Combine(dir, afterUnderscore);
                    if (File.Exists(candidate)) return Path.GetFullPath(candidate);
                }
            }

            // 3. Przeszukiwanie podfolderów w folderach kandydatów (np. jeśli użytkownik uporządkował pliki w podkatalogi)
            foreach (var dir in searchDirs)
            {
                try
                {
                    var allPdfFiles = Directory.GetFiles(dir, "*.pdf", SearchOption.AllDirectories);

                    // A. Dokładna nazwa pliku z bazy (np. z prefiksem GUID)
                    if (!string.IsNullOrEmpty(storedFileName))
                    {
                        var match = allPdfFiles.FirstOrDefault(f => Path.GetFileName(f).Equals(storedFileName, StringComparison.OrdinalIgnoreCase));
                        if (match != null) return Path.GetFullPath(match);
                    }

                    // B. Oryginalna nazwa pliku (np. bez prefiksu)
                    if (!string.IsNullOrEmpty(origFileName))
                    {
                        var match = allPdfFiles.FirstOrDefault(f => Path.GetFileName(f).Equals(origFileName, StringComparison.OrdinalIgnoreCase));
                        if (match != null) return Path.GetFullPath(match);
                    }

                    // C. Nazwa po prefiksie (po znaku '_')
                    if (!string.IsNullOrEmpty(afterUnderscore))
                    {
                        var match = allPdfFiles.FirstOrDefault(f => Path.GetFileName(f).Equals(afterUnderscore, StringComparison.OrdinalIgnoreCase));
                        if (match != null) return Path.GetFullPath(match);
                    }

                    // D. Pliki kończące się oryginalną nazwą (np. inny prefiks)
                    if (!string.IsNullOrEmpty(origFileName))
                    {
                        var match = allPdfFiles.FirstOrDefault(f => 
                            Path.GetFileName(f).EndsWith("_" + origFileName, StringComparison.OrdinalIgnoreCase) ||
                            Path.GetFileName(f).EndsWith(origFileName, StringComparison.OrdinalIgnoreCase));
                        if (match != null) return Path.GetFullPath(match);
                    }
                }
                catch { }
            }

            // 4. Szersze przeszukanie całego drzewa aplikacji (na wypadek przeniesienia folderu w inne poddrzewo)
            try
            {
                string appRoot = GetApplicationRootDirectory();
                if (Directory.Exists(appRoot))
                {
                    var allAppPdfs = Directory.EnumerateFiles(appRoot, "*.pdf", SearchOption.AllDirectories);
                    foreach (var file in allAppPdfs)
                    {
                        string fName = Path.GetFileName(file);
                        if (!string.IsNullOrEmpty(storedFileName) && fName.Equals(storedFileName, StringComparison.OrdinalIgnoreCase))
                            return Path.GetFullPath(file);

                        if (!string.IsNullOrEmpty(origFileName) && fName.Equals(origFileName, StringComparison.OrdinalIgnoreCase))
                            return Path.GetFullPath(file);

                        if (!string.IsNullOrEmpty(afterUnderscore) && fName.Equals(afterUnderscore, StringComparison.OrdinalIgnoreCase))
                            return Path.GetFullPath(file);

                        if (!string.IsNullOrEmpty(origFileName) && (fName.EndsWith("_" + origFileName, StringComparison.OrdinalIgnoreCase) || fName.EndsWith(origFileName, StringComparison.OrdinalIgnoreCase)))
                            return Path.GetFullPath(file);
                    }
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Kopiuje istniejące pliki PDF z folderów tymczasowych/bin do głównego folderu Pdfs aplikacji (jednorazowa konsolidacja / zabezpieczenie).
        /// </summary>
        public static void EnsurePdfsConsolidated()
        {
            try
            {
                string defaultPdfs = GetDefaultPdfsDirectory();
                string appRoot = GetApplicationRootDirectory();
                string binDir = Path.Combine(appRoot, "bin");

                if (Directory.Exists(binDir))
                {
                    var binPdfs = Directory.GetFiles(binDir, "*.pdf", SearchOption.AllDirectories);
                    foreach (var file in binPdfs)
                    {
                        string fileName = Path.GetFileName(file);
                        string dest = Path.Combine(defaultPdfs, fileName);
                        if (!File.Exists(dest))
                        {
                            File.Copy(file, dest, overwrite: false);
                        }
                    }
                }
            }
            catch { }
        }
    }
}
