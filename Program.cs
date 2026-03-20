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
using crystal_shade_manager.Services; // Якщо GoogleSheetsService лежить там

Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine("Запуск Crystal Manager SOLID Edition...");

// 1. Ініціалізація загальних сервісів
IGoogleSheetsService sheetsService = new GoogleSheetsService();
IStateManager stateManager = new StateManager();

// Первинне завантаження кешу (як було у вас раніше)
Console.WriteLine("⏳ Завантажую дані з таблиці...");
try {
    stateManager.CachedUserMessages = await sheetsService.GetUserTaskMessagesAsync() ?? new Dictionary<string, List<string>>();
    Console.WriteLine($"✅ Знайдено рабів: {stateManager.CachedUserMessages.Count}");
} catch (Exception ex) {
    Console.WriteLine($"❌ Помилка первинного завантаження: {ex.Message}");
}

// 2. Реєстрація всіх команд
var commands = new List<ITelegramCommand>
{
    new HelpCommand(),
    new SettingCommand(stateManager),
    new RefreshCommand(sheetsService, stateManager),
    new RiseUpCommand(stateManager),
    new CastCommand(sheetsService),
    new CorrectionsCommand(sheetsService)
};

// 3. Створення обробників
var callbackHandler = new CallbackQueryHandler(stateManager);
var updateHandler = new BotUpdateHandler(commands, callbackHandler);

// 4. Запуск бота
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