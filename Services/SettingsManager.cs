using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using crystal_shade_manager.Models;

namespace crystal_shade_manager.Services;

public static class SettingsManager
{
    private const string FilePath = "chat_settings.json";

    public static ConcurrentDictionary<long, BotSettings> Load()
    {
        if (!File.Exists(FilePath))
            return new ConcurrentDictionary<long, BotSettings>();

        try
        {
            var json = File.ReadAllText(FilePath);
            var dict = JsonSerializer.Deserialize<ConcurrentDictionary<long, BotSettings>>(json);
            return dict ?? new ConcurrentDictionary<long, BotSettings>();
        }
        catch
        {
            return new ConcurrentDictionary<long, BotSettings>();
        }
    }

    public static void Save(ConcurrentDictionary<long, BotSettings> settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Помилка збереження налаштувань: {ex.Message}");
        }
    }
}