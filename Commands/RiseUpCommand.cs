using System;
using System.Collections.Generic;
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
        if (message.Chat == null) return;

        var chatId = message.Chat.Id;
        var threadId = message.MessageThreadId;
        var cache = _state.CachedUserMessages;

        // ОДНА чітка перевірка кешу замість трьох дублікатів
        if (cache == null || cache.Count == 0)
        {
            await botClient.SendTextMessageAsync(
                chatId: chatId, 
                messageThreadId: threadId,
                text: "❌ <b>У кеші немає даних для розсилки.</b> Спершу виконайте команду /refresh", 
                parseMode: ParseMode.Html,
                cancellationToken: token
            );
            return;
        }

        var settings = _state.GetSettings(chatId);
        var session = new RiseUpSession();
        int index = 0;
        
        foreach (var kvp in cache)
        {
            session.Users.Add(kvp.Key);
            session.Toggles[index] = settings.SelectAllByDefault;
            session.Messages[index] = kvp.Value;
            index++;
        }
        
        // Виправляємо текст панелі, щоб не було знаків питання
        var waitMsg = await botClient.SendTextMessageAsync(
            chatId: chatId, 
            messageThreadId: threadId, 
            replyMarkup: KeyboardBuilder.Build(session), 
            text: "⚙️ <b>Панель керування розсилкою розгорнуто. Оберіть учасників:</b>", 
            parseMode: ParseMode.Html, 
            cancellationToken: token
        );
        
        _state.ActiveSessions[$"{chatId}_{waitMsg.MessageId}"] = session;
    }
}