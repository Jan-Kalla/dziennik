using System;
using System.IO;
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

        private static AppSettings LoadSettings()
        {
            try
            {
                // Sprawdzamy najpierw katalog BaseDirectory (tam gdzie leży skompilowany plik exe/dll)
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string configPath = Path.Combine(baseDir, "appsettings.json");

                // Jeśli nie ma w BaseDirectory, sprawdzamy katalog roboczy projektu
                if (!File.Exists(configPath))
                {
                    string fallbackPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
                    if (File.Exists(fallbackPath))
                    {
                        configPath = fallbackPath;
                    }
                }

                if (File.Exists(configPath))
                {
                    string json = File.ReadAllText(configPath);
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var parsed = JsonSerializer.Deserialize<AppSettings>(json, options);
                    if (parsed != null)
                    {
                        return parsed;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AppConfig] Błąd wczytywania appsettings.json: {ex.Message}");
            }

            // Fallback na zmienne środowiskowe, jeśli plik konfiguracyjny nie istnieje
            var fallback = new AppSettings();
            fallback.Supabase.Url = Environment.GetEnvironmentVariable("SUPABASE_URL") ?? string.Empty;
            fallback.Supabase.Key = Environment.GetEnvironmentVariable("SUPABASE_KEY") ?? string.Empty;
            return fallback;
        }
    }
}
