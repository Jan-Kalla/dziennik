using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace GradebookApp.Services
{
    public class SupabaseSettings
    {
        public string Url { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
    }

    public class AppSettings
    {
        public SupabaseSettings Supabase { get; set; } = new SupabaseSettings();
    }

    public static class AppConfig
    {
        private static AppSettings? _settings;

        public static AppSettings Settings
        {
            get
            {
                if (_settings == null)
                {
                    _settings = LoadSettings();
                }
                return _settings;
            }
        }

        public static void Reload()
        {
            _settings = LoadSettings();
        }

        public static bool IsValid(AppSettings? settings)
        {
            if (settings?.Supabase == null) return false;
            var url = settings.Supabase.Url;
            var key = settings.Supabase.Key;
            return !string.IsNullOrWhiteSpace(url)
                && !string.IsNullOrWhiteSpace(key)
                && !url.Contains("<")
                && !key.Contains("<");
        }

        private static AppSettings? LoadFromFile(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    return JsonSerializer.Deserialize<AppSettings>(json, options);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AppConfig] Błąd wczytywania {filePath}: {ex.Message}");
            }
            return null;
        }

        private static AppSettings? LoadFromEmbeddedResource()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resourceNames = assembly.GetManifestResourceNames();
                var resourceName = resourceNames.FirstOrDefault(n => n.EndsWith("appsettings.json", StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrEmpty(resourceName))
                {
                    using var stream = assembly.GetManifestResourceStream(resourceName);
                    if (stream != null)
                    {
                        using var reader = new StreamReader(stream);
                        string json = reader.ReadToEnd();
                        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        return JsonSerializer.Deserialize<AppSettings>(json, options);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AppConfig] Błąd wczytywania zasobu osadzonego: {ex.Message}");
            }
            return null;
        }

        private static AppSettings LoadSettings()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string baseConfigPath = Path.Combine(baseDir, "appsettings.json");

            // 1. Sprawdzamy fizyczny plik w katalogu aplikacji (BaseDirectory - obok .exe)
            var fileSettings = LoadFromFile(baseConfigPath);
            if (IsValid(fileSettings))
            {
                return fileSettings!;
            }

            // 2. Jeśli nie ma w BaseDirectory, sprawdzamy bieżący katalog roboczy (CurrentDirectory)
            string currentDirPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
            if (!string.Equals(baseConfigPath, currentDirPath, StringComparison.OrdinalIgnoreCase))
            {
                var currentDirSettings = LoadFromFile(currentDirPath);
                if (IsValid(currentDirSettings))
                {
                    return currentDirSettings!;
                }
            }

            // 3. Sprawdzamy zasób osadzony wewnątrz pliku .exe / .dll (EmbeddedResource)
            var embeddedSettings = LoadFromEmbeddedResource();
            if (IsValid(embeddedSettings))
            {
                // Automatycznie zapisujemy plik konfiguracyjny obok .exe, aby użytkownik widział konfigurację
                try
                {
                    if (!File.Exists(baseConfigPath))
                    {
                        var json = JsonSerializer.Serialize(embeddedSettings, new JsonSerializerOptions { WriteIndented = true });
                        File.WriteAllText(baseConfigPath, json);
                    }
                }
                catch
                {
                    // Ignorujemy błędy braku uprawnień zapisu (np. uruchomienie z katalogu tylko do odczytu)
                }

                try
                {
                    if (!string.Equals(baseConfigPath, currentDirPath, StringComparison.OrdinalIgnoreCase) && !File.Exists(currentDirPath))
                    {
                        var json = JsonSerializer.Serialize(embeddedSettings, new JsonSerializerOptions { WriteIndented = true });
                        File.WriteAllText(currentDirPath, json);
                    }
                }
                catch
                {
                }

                return embeddedSettings!;
            }

            // 4. Fallback na zmienne środowiskowe
            string? envUrl = Environment.GetEnvironmentVariable("SUPABASE_URL");
            string? envKey = Environment.GetEnvironmentVariable("SUPABASE_KEY");
            if (!string.IsNullOrWhiteSpace(envUrl) && !string.IsNullOrWhiteSpace(envKey))
            {
                return new AppSettings
                {
                    Supabase = new SupabaseSettings { Url = envUrl, Key = envKey }
                };
            }

            // 5. Zwracamy pusty obiekt (CloudSync zgłosi wtedy czytelny komunikat)
            return new AppSettings();
        }

        public static void SaveSettings(AppSettings settings)
        {
            try
            {
                string baseConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(baseConfigPath, json);
                _settings = settings;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AppConfig] Błąd zapisu appsettings.json: {ex.Message}");
            }
        }
    }
}
