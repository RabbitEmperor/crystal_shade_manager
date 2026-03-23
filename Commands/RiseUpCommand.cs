using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using crystal_shade_manager.Interfaces;
using crystal_shade_manager.Models;
using crystal_shade_manager.Helpers;

namespace crystal_shade_manager.Commands;

public class RiseUpCommand : ITelegramCommand
{
    private readonly IStateManager _state;
    public string Trigger => "/rise_up";

    
    public RiseUpCommand(IStateManager state)
    {
        _state = state;
    }

    public async Task ExecuteAsync(ITelegramBotClient botClient, Message message, CancellationToken token)
    {
        // === ЩИТ №1: Від битих повідомлень ===
        if (message.Chat == null) return;

        var chatId = message.Chat.Id;
        
        // === ЩИТ №2: Від порожнього (null) кешу ===
        var cache = _state.CachedUserMessages;
        if (cache == null || cache.Count == 0)
        {
            await botClient.SendTextMessageAsync(chatId, "❌ Немає завдань для розсилки. Оновіть таблицю командою /refresh", cancellationToken: token);
            return;
        }
        
        var threadId = message.MessageThreadId;

        if (_state.CachedUserMessages.Count == 0)
        {
            await botClient.SendTextMessageAsync(chatId, "Немає завдань (пропишіть /refresh)", messageThreadId: threadId, cancellationToken: token);
            return;
        }

        var settings = _state.GetSettings(chatId);
        var session = new RiseUpSession();
        int index = 0;
        
        if (cache == null || cache.Count == 0) 
        {
            // Вивести повідомлення "Кеш порожній"
            return;
        }
        
        foreach (var kvp in _state.CachedUserMessages)
        {
            session.Users.Add(kvp.Key);
            session.Toggles[index] = settings.SelectAllByDefault;
            session.Messages[index] = kvp.Value;
            index++;
        }
        
        var waitMsg = await botClient.SendTextMessageAsync(chatId, "📋 <b>Оберіть рабів для розсилки:</b>", 
            messageThreadId: threadId, replyMarkup: KeyboardBuilder.Build(session), parseMode: ParseMode.Html, cancellationToken: token);
        
        _state.ActiveSessions[$"{chatId}_{waitMsg.MessageId}"] = session;
    }
}