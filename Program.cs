using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;
using crystal_shade_manager;
using crystal_shade_manager.Interfaces;
using crystal_shade_manager.State;
using crystal_shade_manager.Commands;
using crystal_shade_manager.Handlers;
using crystal_shade_manager.Services;
using Microsoft.AspNetCore.Builder;

Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine("🚀 Запуск Crystal Manager SOLID Edition...");

// --- Веб-сервер для Render (Health Check) ---
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.MapMethods("/", new[] { "GET", "HEAD" }, () => "Crystal Manager is Alive!");
_ = app.RunAsync(); 

IGoogleSheetsService sheetsService = new GoogleSheetsService();
IStateManager stateManager = new StateManager();

Console.WriteLine("📊 Завантаження базових даних із таблиць...");
try {
    stateManager.CachedUserMessages = await sheetsService.GetUserTaskMessagesAsync() ?? new Dictionary<string, List<string>>();
    Console.WriteLine($"✅ Даних завантажено: {stateManager.CachedUserMessages.Count}");
    
    // --- ЗАЛІЗОБЕТОННИЙ ПОШУК КОРЕНЯ ПРОЄКТУ ---
    string projectRoot = AppDomain.CurrentDomain.BaseDirectory;
    while (!File.Exists(Path.Combine(projectRoot, "Program.cs")) && Directory.GetParent(projectRoot) != null)
    {
        projectRoot = Directory.GetParent(projectRoot).FullName;
    }
    
    string jokesPath = Path.Combine(projectRoot, "user_jokes.json");
    Console.WriteLine($"📂 Реальний шлях для збереження JSON: {jokesPath}");

    // Словник, який ігнорує регістр букв при перевірках
    var existingJokes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    // 1. Зчитуємо існуючий файл, якщо він є
    if (File.Exists(jokesPath))
    {
        try
        {
            string existingJson = await File.ReadAllTextAsync(jokesPath, Encoding.UTF8);
            var loadedJokes = JsonSerializer.Deserialize<Dictionary<string, string>>(existingJson);
            if (loadedJokes != null)
            {
                foreach (var kvp in loadedJokes)
                {
                    existingJokes[kvp.Key.Trim()] = kvp.Value;
                }
            }
            Console.WriteLine($"ℹ️ Зчитано існуючий файл. Знайдено записів: {existingJokes.Count}");
        }
        catch (Exception jsonEx)
        {
            Console.WriteLine($"⚠️ Помилка зчитування файлу жартів: {jsonEx.Message}");
        }
    }

    // 2. Отримуємо свіжі теги з таблиці команд
    var teamTags = await sheetsService.GetTeamTagsAsync();
    
    if (teamTags != null && teamTags.Count > 0)
    {
        // Створюємо новий чистий словник для збереження фінального результату
        var finalJokes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bool hasChanges = false;

        // Спочатку переносимо ВСІ старі записи, які вже були у файлі (щоб нічого не видалити)
        foreach (var kvp in existingJokes)
        {
            finalJokes[kvp.Key] = kvp.Value;
        }

        // Тепер проходимося по людях з таблиці й оновлюємо/додаємо їх
        foreach (var kvp in teamTags)
        {
            string rawTag = kvp.Value;
            if (string.IsNullOrEmpty(rawTag) || rawTag == "-") continue;
            
            string cleanUsername = rawTag.Replace("@", "").Trim();

            // Перевіряємо, чи є вже цей користувач у файлі (ігноруючи регістр)
            if (finalJokes.TryGetValue(cleanUsername, out string existingValue))
            {
                // Якщо користувач є, але там записана ДЕФОЛТНА фраза, і при цьому точний ключ (регістр) у таблиці змінився
                // (наприклад, у файлі було "crystalick_dragon", а в таблиці стало "CrysTalick_Dragon")
                if (existingValue.Contains("Ти просто котик!") && !finalJokes.ContainsKey(cleanUsername))
                {
                    // Видаляємо старий ключ з неправильним регістром і перезаписуємо правильним
                    finalJokes.Remove(cleanUsername);
                    finalJokes[cleanUsername] = $"🎉 @{cleanUsername}, <b>у тебе немає боргів! Ти просто котик!</b>";
                    hasChanges = true;
                }
                // ЯКЩО ФРАЗА КАСТОМНА (не містить "Ти просто котик!") — МИ ЇЇ НЕ ЧІПАЄМО ВЗАГАЛІ!
            }
            else
            {
                // Якщо користувача взагалі немає — додаємо як нового
                finalJokes[cleanUsername] = $"🎉 @{cleanUsername}, <b>у тебе немає боргів! Ти просто котик!</b>";
                hasChanges = true;
                Console.WriteLine($"✨ Додано нового актора з таблиці: {cleanUsername}");
            }
        }

        // Записуємо зміни у файл, тільки якщо реально з'явилися нові люди або оновилися дефолтні теги
        if (!File.Exists(jokesPath) || hasChanges)
        {
            var options = new JsonSerializerOptions 
            { 
                WriteIndented = true, 
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping 
            };
            
            string jsonTemplate = JsonSerializer.Serialize(finalJokes, options);
            await File.WriteAllTextAsync(jokesPath, jsonTemplate, Encoding.UTF8);
            Console.WriteLine($"✅ Файл user_jokes.json успішно синхронізовано! Усього акторів: {finalJokes.Count}");
        }
        else
        {
            Console.WriteLine("ℹ️ Змін у таблиці акторів не виявлено. Кастомні жарти захищено.");
        }
    }

} catch (Exception ex) {
    Console.WriteLine($"❌ Помилка ініціалізації: {ex.Message}");
}

var commands = new List<ITelegramCommand>
{
    new HelpCommand(),
    new SettingCommand(stateManager),
    new RefreshCommand(sheetsService, stateManager),
    new RiseUpCommand(stateManager),
    new CastCommand(sheetsService),
    new CorrectionsCommand(sheetsService),
    new TitlesRiseUpCommand(sheetsService),
    new DebtsCommand(sheetsService)
};

var callbackHandler = new CallbackQueryHandler(stateManager, sheetsService);
var updateHandler = new BotUpdateHandler(commands, callbackHandler);

var botClient = new TelegramBotClient(Config.BotToken);
using var cts = new CancellationTokenSource();

await botClient.DeleteWebhookAsync(cancellationToken: cts.Token);

var autoUpdater = new AutoUpdateService(sheetsService, stateManager);
autoUpdater.Start(cts.Token);

var receiverOptions = new ReceiverOptions
{
    AllowedUpdates = Array.Empty<UpdateType>(), 
    DropPendingUpdates = true 
};

botClient.StartReceiving(
    updateHandler: updateHandler.HandleUpdateAsync,
    errorHandler: updateHandler.HandleErrorAsync,
    receiverOptions: receiverOptions,
    cancellationToken: cts.Token
);

Console.WriteLine("🤖 Бот успішно запущений. Натисніть Ctrl+C для виходу.");

try {
    await Task.Delay(-1, cts.Token);
} catch (TaskCanceledException) {
}

cts.Cancel();