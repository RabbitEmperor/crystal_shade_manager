using System;
using System.Collections.Generic;
using System.Text;
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

// --- Блок для Render (Health Check) ---
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.MapMethods("/", new[] { "GET", "HEAD" }, () => "Crystal Manager is Alive!");
_ = app.RunAsync(); 

IGoogleSheetsService sheetsService = new GoogleSheetsService();
IStateManager stateManager = new StateManager();

Console.WriteLine("📦 Завантаження даних із таблиць...");
try {
    stateManager.CachedUserMessages = await sheetsService.GetUserTaskMessagesAsync() ?? new Dictionary<string, List<string>>();
    Console.WriteLine($"✅ Даних завантажено: {stateManager.CachedUserMessages.Count}");
} catch (Exception ex) {
    Console.WriteLine($"❌ Помилка завантаження: {ex.Message}");
}

var commands = new List<ITelegramCommand>
{
    new HelpCommand(),
    new SettingCommand(stateManager),
    new RefreshCommand(sheetsService, stateManager),
    new RiseUpCommand(stateManager),
    new CastCommand(sheetsService),
    new CorrectionsCommand(sheetsService),
    new TitlesRiseUpCommand(sheetsService) 
};

var callbackHandler = new CallbackQueryHandler(stateManager, sheetsService);
var updateHandler = new BotUpdateHandler(commands, callbackHandler);

var botClient = new TelegramBotClient(Config.BotToken);
using var cts = new CancellationTokenSource();

// Очищаємо вебхуки та старі повідомлення перед стартом
// Це вирішує проблему дублювання відповідей, якщо бот був офлайн
await botClient.DeleteWebhookAsync(cancellationToken: cts.Token);

var receiverOptions = new ReceiverOptions
{
    AllowedUpdates = Array.Empty<UpdateType>(), // Отримувати всі типи оновлень
    DropPendingUpdates = true // Ігнорувати повідомлення, що надійшли поки бот не працював
};

botClient.StartReceiving(
    updateHandler: updateHandler.HandleUpdateAsync,
    errorHandler: updateHandler.HandleErrorAsync,
    receiverOptions: receiverOptions,
    cancellationToken: cts.Token
);

Console.WriteLine("🤖 Бот запущений. Натисніть Ctrl+C для зупинки.");

// Тримаємо програму запущеною
try {
    await Task.Delay(-1, cts.Token);
} catch (TaskCanceledException) {
    // Нормальне завершення при скасуванні токена
}

cts.Cancel();