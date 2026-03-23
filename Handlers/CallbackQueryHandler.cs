using System;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using crystal_shade_manager.Interfaces;
using crystal_shade_manager.Helpers;

namespace crystal_shade_manager.Handlers;

public class CallbackQueryHandler
{
    private readonly IStateManager _state;

    public CallbackQueryHandler(IStateManager state)
    {
        _state = state;
    }

    public async Task HandleAsync(ITelegramBotClient bot, CallbackQuery cq, CancellationToken token)
    {
        // ЩИТ №1: Якщо Телеграм надіслав "биті" дані без повідомлення або без тексту кнопки — просто ігноруємо.
        // Це миттєво лагодить тест "HandleAsync_WhenDataIsNull_ShouldNotCrash"
        if (cq.Message == null || string.IsNullOrEmpty(cq.Data))
        {
            return;
        }

        var msgId = cq.Message.MessageId;
        var chatId = cq.Message.Chat.Id;
        var threadId = cq.Message.MessageThreadId;

        if (cq.Data.StartsWith("set_"))
        {
            var settings = _state.GetSettings(chatId);
            if (cq.Data == "set_toggle_select") settings.SelectAllByDefault = !settings.SelectAllByDefault;
            else if (cq.Data == "set_toggle_speed") settings.SafeModeDelay = !settings.SafeModeDelay;
            
            _state.SaveSettings();
            try { await bot.EditMessageReplyMarkupAsync(chatId, msgId, replyMarkup: KeyboardBuilder.BuildSettings(settings), cancellationToken: token); } catch { }
            try { await bot.AnswerCallbackQueryAsync(cq.Id, "Збережено!", cancellationToken: token); } catch { }
            return;
        }

        try { await bot.AnswerCallbackQueryAsync(cq.Id, cancellationToken: token); } catch { }

        string sessionKey = $"{chatId}_{msgId}";
        
        // ЩИТ №2: Перевіряємо, чи взагалі існує сховище сесій (_state.ActiveSessions != null), 
        // перш ніж намагатися щось з нього дістати. Це лагодить тест "HandleAsync_WhenDataIsUnknown_ShouldIgnore"
        if (_state.ActiveSessions == null || !_state.ActiveSessions.TryGetValue(sessionKey, out var session)) 
        {
            return;
        }

        if (cq.Data.StartsWith("t_"))
        {
            if (int.TryParse(cq.Data.Substring(2), out int index) && session.Toggles.ContainsKey(index))
            {
                session.Toggles[index] = !session.Toggles[index]; 
                try { await bot.EditMessageReplyMarkupAsync(chatId, msgId, replyMarkup: KeyboardBuilder.Build(session), cancellationToken: token); } catch { }
            }
        }
        else if (cq.Data == "send")
        {
            await bot.EditMessageTextAsync(chatId, msgId, "🚀 Розсилаю...", cancellationToken: token);
            _state.ActiveSessions.TryRemove(sessionKey, out _);

            int delayTime = _state.GetSettings(chatId).SafeModeDelay ? 3100 : 1500;

            _ = Task.Run(async () => 
            {
                foreach (var kvp in session.Toggles)
                {
                    if (!kvp.Value || !session.Messages.ContainsKey(kvp.Key)) continue;
                    
                    foreach (var msgText in session.Messages[kvp.Key])
                    {
                        try 
                        {
                            await bot.SendTextMessageAsync(chatId, msgText, messageThreadId: threadId, parseMode: ParseMode.Html, cancellationToken: token);
                            await Task.Delay(delayTime, token); 
                        }
                        catch { await Task.Delay(3000, token); } 
                    }
                }
                try { await bot.SendTextMessageAsync(chatId, "🏁 Розсилка завершена", messageThreadId: threadId, cancellationToken: token); } catch { }
            }, CancellationToken.None);
        }
    }
}