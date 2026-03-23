using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using crystal_shade_manager.Interfaces;
using crystal_shade_manager.Helpers;
using crystal_shade_manager.Services;

namespace crystal_shade_manager.Commands;

public class HelpCommand : ITelegramCommand
{
    public string Trigger => "/help";
    public async Task ExecuteAsync(ITelegramBotClient bot, Message msg, CancellationToken token)
    {
        string text = 
            "🤖 <b>Довідка по Crystal Manager</b> 🤖\n\n" +
            "📌 <b>Команди (тільки для адмінів):</b>\n" +
            "🔹 /rise_up — відкриває панель керування розсилкою. І після тегає їх і вказує на незавершені завдання.\n" +
            "🔹 /setting — відкриває меню налаштувань бота. Поки тут нічого ноухау немає.\n" +
            "🔹 /refresh — оновлює базу даних з Google Таблиці.\n" +
            "🔹 /cast — запис касту в таблицю. А точніше відповідаєш на повідомлення з акторами і ролями їхніми пишеш назва аркуша і номер серії і воно записує ці ролі і акторів в таблицю.\n" +
            "🔹 /corrections — запис правок в таблицю. Відповідаєш на повідомлення з правками в яких на початку повідомлення має вже стояти назва аркушу і номер серії і воно записує в примітки таймінги правок а також відмічає що акторові треба виконати правки.\n" +
            "🔹 /help — показує це повідомлення.";
        await bot.SendTextMessageAsync(msg.Chat.Id, text, messageThreadId: msg.MessageThreadId, parseMode: ParseMode.Html, cancellationToken: token);
    }
}

public class SettingCommand : ITelegramCommand
{
    private readonly IStateManager _state;
    public string Trigger => "/setting";
    public SettingCommand(IStateManager state) { _state = state; }

    public async Task ExecuteAsync(ITelegramBotClient bot, Message msg, CancellationToken token)
    {
        var settings = _state.GetSettings(msg.Chat.Id);
        await bot.SendTextMessageAsync(msg.Chat.Id, "⚙️ <b>Налаштування:</b>", 
            messageThreadId: msg.MessageThreadId, replyMarkup: KeyboardBuilder.BuildSettings(settings), parseMode: ParseMode.Html, cancellationToken: token);
    }
}

public class RefreshCommand : ITelegramCommand
{
    private readonly IGoogleSheetsService _sheets;
    private readonly IStateManager _state;
    public string Trigger => "/refresh";

    public RefreshCommand(IGoogleSheetsService sheets, IStateManager state)
    {
        _sheets = sheets;
        _state = state;
    }

    public async Task ExecuteAsync(ITelegramBotClient bot, Message msg, CancellationToken token)
    {
        if (msg.Chat == null) return;
        var chatId = msg.Chat.Id;
        
        var waitMsg = await bot.SendTextMessageAsync(msg.Chat.Id, "🔄 Оновлюю БД...", messageThreadId: msg.MessageThreadId, cancellationToken: token);
        try 
        {
            _state.CachedUserMessages = await _sheets.GetUserTaskMessagesAsync() ?? new Dictionary<string, List<string>>();
            await bot.EditMessageTextAsync(msg.Chat.Id, waitMsg.MessageId, $"✅ Оновлено! Рабів: {_state.CachedUserMessages.Count}", cancellationToken: token);
        }
        catch (Exception ex) { await bot.EditMessageTextAsync(msg.Chat.Id, waitMsg.MessageId, $"❌ Помилка: {ex.Message}", cancellationToken: token); }
    }
}