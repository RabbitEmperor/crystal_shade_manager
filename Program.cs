using System;
using System.Text;
using System.Threading;
using Telegram.Bot;
using Telegram.Bot.Polling;
using crystal_shade_manager;
using crystal_shade_manager.Services;

Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine("Запуск Crystal Manager...");

// 1. Створюємо сервіси (Dependency Injection)
IGoogleSheetsService sheetsService = new GoogleSheetsService();
var updateHandler = new BotUpdateHandler(sheetsService);

// 2. Завантажуємо дані при старті
await updateHandler.InitializeCacheAsync();

// 3. Запускаємо бота
var botClient = new TelegramBotClient(Config.BotToken);
using var cts = new CancellationTokenSource();

botClient.StartReceiving(
    updateHandler: updateHandler.HandleUpdateAsync,
    errorHandler: updateHandler.HandleErrorAsync,
    receiverOptions: new ReceiverOptions { AllowedUpdates = [] },
    cancellationToken: cts.Token
);

Console.WriteLine("Бот працює. Натисніть Enter, щоб вийти.");
await Task.Delay(-1);
cts.Cancel();