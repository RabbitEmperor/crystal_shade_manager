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
    
    // --- ЗАЛІЗОБЕТОННИЙ ПОШУК КОРЕНЯ ПРОЄКТУ НА LINUX ---
    string projectRoot = AppDomain.CurrentDomain.BaseDirectory;
    while (!File.Exists(Path.Combine(projectRoot, "Program.cs")) && Directory.GetParent(projectRoot) != null)
    {
        projectRoot = Directory.GetParent(projectRoot).FullName;
    }
    
    string jokesPath = Path.Combine(projectRoot, "user_jokes.json");
    Console.WriteLine($"📂 Реальний шлях для збереження JSON: {jokesPath}");

    // Створюємо словник із підтримкою ігнорування регістру букв
    var defaultJokes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    // 1. Якщо файл вже є — зчитуємо його
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
                    defaultJokes[kvp.Key.Trim()] = kvp.Value;
                }
            }
            Console.WriteLine($"ℹ️ Зчитано існуючий файл. Знайдено записів: {defaultJokes.Count}");
        }
        catch (Exception jsonEx)
        {
            Console.WriteLine($"⚠️ Помилка зчитування файлу жартів: {jsonEx.Message}");
        }
    }

    // 2. Витягуємо свіжі теги з таблиці
    var teamTags = await sheetsService.GetTeamTagsAsync();
    
    if (teamTags != null && teamTags.Count > 0)
    {
        bool isUpdated = false;

        foreach (var kvp in teamTags)
        {
            string rawTag = kvp.Value;
            if (string.IsNullOrEmpty(rawTag) || rawTag == "-") continue;
            
            // Залишаємо оригінальний регістр із таблиці, але чистимо від @ та пробілів
            string cleanUsername = rawTag.Replace("@", "").Trim();

            // Завдяки StringComparer.OrdinalIgnoreCase перевірка ContainsKey знайде користувача незалежно від регістру
            if (!defaultJokes.ContainsKey(cleanUsername))
            {
                defaultJokes[cleanUsername] = $"🎉 @{cleanUsername}, <b>у тебе немає боргів! Ти просто котик!</b>";
                isUpdated = true;
                Console.WriteLine($"✨ Додано нового актора: {cleanUsername}");
            }
        }

        // Записуємо, якщо файлу не було або додалися нові люди
        if (!File.Exists(jokesPath) || isUpdated)
        {
            var options = new JsonSerializerOptions 
            { 
                WriteIndented = true, 
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping 
            };
            
            string jsonTemplate = JsonSerializer.Serialize(defaultJokes, options);
            await File.WriteAllTextAsync(jokesPath, jsonTemplate, Encoding.UTF8);
            Console.WriteLine($"✅ Файл user_jokes.json успішно збережено в корінь проєкту! Усього: {defaultJokes.Count}");
        }
        else
        {
            Console.WriteLine("ℹ️ Змін у таблиці акторів не виявлено. Файл залишено без змін.");
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