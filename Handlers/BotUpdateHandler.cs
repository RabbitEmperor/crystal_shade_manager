using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using crystal_shade_manager.Interfaces;

namespace crystal_shade_manager.Handlers;

public class BotUpdateHandler
{
    private readonly IEnumerable<ITelegramCommand> _commands;
    private readonly CallbackQueryHandler _callbackHandler;

    public BotUpdateHandler(IEnumerable<ITelegramCommand> commands, CallbackQueryHandler callbackHandler)
    {
        _commands = commands;
        _callbackHandler = callbackHandler;
    }

    private async Task<bool> IsAdminAsync(ITelegramBotClient bot, long chatId, long userId, CancellationToken token)
    {
        try
        {
            var chat = await bot.GetChatAsync(chatId, token);
            if (chat.Type == ChatType.Private) return true;
            var member = await bot.GetChatMemberAsync(chatId, userId, token);
            return member.Status == ChatMemberStatus.Administrator || member.Status == ChatMemberStatus.Creator;
        }
        catch { return false; }
    }

    public async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken token)
    {
        if (update.Type == UpdateType.Message && update.Message?.Text != null)
        {
            var message = update.Message;
            string text = message.Text;

            if (text.StartsWith("/"))
            {
                // Визначаємо команду ДО перевірки на адміна
                string commandName = text.Split(' ')[0].Split('@')[0];
                
                // ДОЗВОЛЯЄМО /my_task ДЛЯ ВСІХ. Для інших команд — перевіряємо адмінку.
                if (commandName != "/my_task" && !await IsAdminAsync(bot, message.Chat.Id, message.From!.Id, token)) 
                {
                    return; // Якщо не адмін і не my_task — ігноруємо
                }

                var command = _commands.FirstOrDefault(c => c.Trigger == commandName);

                if (command != null)
                {
                    try { await command.ExecuteAsync(bot, message, token); }
                    catch (Exception ex) { Console.WriteLine($"Помилка {commandName}: {ex.Message}"); }
                }
            }
        }
        else if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery != null)
        {
            if (!await IsAdminAsync(bot, update.CallbackQuery.Message!.Chat.Id, update.CallbackQuery.From.Id, token))
            {
                await bot.AnswerCallbackQueryAsync(update.CallbackQuery.Id, "❌ Тільки адміни!", showAlert: true, cancellationToken: token);
                return;
            }
            await _callbackHandler.HandleAsync(bot, update.CallbackQuery, token);
        }
    }

    public Task HandleErrorAsync(ITelegramBotClient bot, Exception ex, CancellationToken token)
    {
        Console.WriteLine($"Telegram Error: {ex.Message}");
        return Task.CompletedTask;
    }
}