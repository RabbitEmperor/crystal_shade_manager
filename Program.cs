using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Polling;
using crystal_shade_manager;
using crystal_shade_manager.Interfaces;
using crystal_shade_manager.State;
using crystal_shade_manager.Commands;
using crystal_shade_manager.Handlers;
using crystal_shade_manager.Services;
using Microsoft.AspNetCore.Builder;

Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine("Запуск Crystal Manager SOLID Edition...");

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.MapGet("/", () => "Crystal Manager is Alive!");
_ = app.RunAsync(); // Запускаємо в фоні

Console.WriteLine("🚀 Запуск Crystal Manager SOLID Edition...");

IGoogleSheetsService sheetsService = new GoogleSheetsService();
IStateManager stateManager = new StateManager();

Console.WriteLine("Отримання кешу з таблиці...");
try {
    stateManager.CachedUserMessages = await sheetsService.GetUserTaskMessagesAsync() ?? new Dictionary<string, List<string>>();
    Console.WriteLine($"Кеш оновлено: {stateManager.CachedUserMessages.Count}");
} catch (Exception ex) {
    Console.WriteLine($"Помилка завантаження: {ex.Message}");
}

var commands = new List<ITelegramCommand>
{
    new HelpCommand(),
    new SettingCommand(stateManager),
    new RefreshCommand(sheetsService, stateManager),
    new RiseUpCommand(stateManager),
    new CastCommand(sheetsService),
    new CorrectionsCommand(sheetsService),
    new TitlesRiseUpCommand(sheetsService) // <--- Наша нова команда
};

// Передаємо sheetsService в CallbackQueryHandler
var callbackHandler = new CallbackQueryHandler(stateManager, sheetsService);
var updateHandler = new BotUpdateHandler(commands, callbackHandler);

var botClient = new TelegramBotClient(Config.BotToken);
using var cts = new CancellationTokenSource();

botClient.StartReceiving(
    updateHandler: updateHandler.HandleUpdateAsync,
    errorHandler: updateHandler.HandleErrorAsync,
    receiverOptions: new ReceiverOptions { AllowedUpdates = [] },
    cancellationToken: cts.Token
);

Console.WriteLine("Бот працює. Натисніть Ctrl+C для виходу.");
await Task.Delay(-1);
cts.Cancel();