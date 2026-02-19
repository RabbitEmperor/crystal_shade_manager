using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using crystal_shade_manager.Models;
using crystal_shade_manager.Helpers;

namespace crystal_shade_manager.Services;

public class BotUpdateHandler
{
    private readonly IGoogleSheetsService _sheetsService;
    private Dictionary<string, List<string>> _cachedUserMessages = new();
    private readonly ConcurrentDictionary<int, RiseUpSession> _activeSessions = new();

    public BotUpdateHandler(IGoogleSheetsService sheetsService)
    {
        _sheetsService = sheetsService;
    }

    public async Task InitializeCacheAsync()
    {
        Console.WriteLine("⏳ Завантажую дані з таблиці...");
        try {
            _cachedUserMessages = await _sheetsService.GetUserTaskMessagesAsync() ?? new Dictionary<string, List<string>>();
            Console.WriteLine($"✅ Дані завантажено! Знайдено рабів із завданнями: {_cachedUserMessages.Count}");
        } catch (Exception ex) {
            Console.WriteLine($"❌ Помилка первинного завантаження: {ex.Message}");
        }
    }

    // --- НОВА ФУНКЦІЯ ПЕРЕВІРКИ НА АДМІНА ---
    private async Task<bool> IsAdminAsync(ITelegramBotClient bot, long chatId, long userId, CancellationToken token)
    {
        try
        {
            var chat = await bot.GetChatAsync(chatId, token);
            // Якщо ви пишете боту в особисті повідомлення (не в групу) - ви автоматично адмін
            if (chat.Type == ChatType.Private) return true;

            var member = await bot.GetChatMemberAsync(chatId, userId, token);
            return member.Status == ChatMemberStatus.Administrator || member.Status == ChatMemberStatus.Creator;
        }
        catch
        {
            return false; // Якщо сталася помилка перевірки (наприклад, бот не має прав) - забороняємо
        }
    }

    public async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken token)
    {
        // 1. ОБРОБКА ТЕКСТОВИХ КОМАНД
        if (update.Type == UpdateType.Message && update.Message?.Text != null)
        {
            var message = update.Message;
            var text = message.Text;

            // Перевіряємо, чи це взагалі команда
            if (text.StartsWith("/"))
            {
                // Перевіряємо права доступу
                bool isAdmin = await IsAdminAsync(bot, message.Chat.Id, message.From!.Id, token);
                if (!isAdmin)
                {
                    // Бот просто ігнорує звичайних користувачів (можна розкоментувати рядок нижче, щоб він сварився)
                    // await bot.SendTextMessageAsync(message.Chat.Id, "❌ У вас немає прав для керування ботом.", cancellationToken: token);
                    return;
                }
            }

            if (text.StartsWith("/help"))
            {
                string helpText = 
                    "🤖 <b>Довідка по Crystal Manager</b> 🤖\n\n" +
                    "Цей бот створений для зручного контролю та розсилки завдань рабам команди.\n\n" +
                    "📌 <b>Доступні команди (тільки для адмінів):</b>\n" +
                    "🔹 /rise_up — відкриває панель керування розсилкою.\n" +
                    "🔹 /refresh — примусово оновлює базу даних з Google Таблиці.\n" +
                    "🔹 /help — показує це повідомлення.\n\n" +
                    "⚙️ <b>Як це працює:</b>\n" +
                    "1. Бот бере до уваги <b>тільки</b> тих людей, які вписані у вкладку «Команда».\n" +
                    "2. У списки потрапляють лише завдання зі статусом «<i>виконується</i>» або «<i>правки</i>».\n" +
                    "3. Завдання групуються по серіях.";

                await bot.SendTextMessageAsync(message.Chat.Id, helpText, parseMode: ParseMode.Html, cancellationToken: token);
                return;
            }

            if (text.StartsWith("/refresh"))
            {
                var waitMsg = await bot.SendTextMessageAsync(message.Chat.Id, "🔄 Оновлюю базу даних з Google Таблиці...", cancellationToken: token);
                try 
                {
                    _cachedUserMessages = await _sheetsService.GetUserTaskMessagesAsync() ?? new Dictionary<string, List<string>>();
                    await bot.EditMessageTextAsync(message.Chat.Id, waitMsg.MessageId, $"✅ Базу оновлено! Активних рабів: {_cachedUserMessages.Count}", cancellationToken: token);
                }
                catch (Exception ex) 
                {
                    await bot.EditMessageTextAsync(message.Chat.Id, waitMsg.MessageId, $"❌ Помилка оновлення: {ex.Message}", cancellationToken: token);
                }
                return;
            }

            if (text.StartsWith("/rise_up"))
            {
                if (_cachedUserMessages.Count == 0)
                {
                    await bot.SendTextMessageAsync(message.Chat.Id, "На даний момент треба набирати ще більше проєктів (Або пропишіть /refresh)", cancellationToken: token);
                }
                else
                {
                    var session = new RiseUpSession();
                    int index = 0;
                    foreach (var kvp in _cachedUserMessages)
                    {
                        session.Users.Add(kvp.Key);
                        session.Toggles[index] = true;
                        session.Messages[index] = kvp.Value;
                        index++;
                    }
                    
                    var waitMsg = await bot.SendTextMessageAsync(message.Chat.Id, "📋 <b>Оберіть рабів для розсилки:</b>", 
                        replyMarkup: KeyboardBuilder.Build(session), parseMode: ParseMode.Html, cancellationToken: token);
                    
                    _activeSessions[waitMsg.MessageId] = session;
                }
                return;
            }
        }

        // 2. ОБРОБКА НАТИСКАННЯ КНОПОК
        if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery != null)
        {
            var cq = update.CallbackQuery;
            var msgId = cq.Message.MessageId;
            var chatId = cq.Message.Chat.Id;

            // Перевіряємо, чи натиснув кнопку адмін
            bool isAdmin = await IsAdminAsync(bot, chatId, cq.From.Id, token);
            if (!isAdmin)
            {
                // Видаємо спливаюче вікно з помилкою прямо в Telegram
                await bot.AnswerCallbackQueryAsync(cq.Id, "❌ Тільки адміністратори можуть натискати ці кнопки!", showAlert: true, cancellationToken: token);
                return;
            }

            try { await bot.AnswerCallbackQueryAsync(cq.Id, cancellationToken: token); } catch { }

            if (!_activeSessions.ContainsKey(msgId))
            {
                try { await bot.SendTextMessageAsync(chatId, "Ця сесія вже застаріла. Пропишіть /rise_up ще раз.", cancellationToken: token); } catch { }
                return;
            }

            var session = _activeSessions[msgId];

            if (cq.Data.StartsWith("t_"))
            {
                int index = int.Parse(cq.Data.Substring(2));
                session.Toggles[index] = !session.Toggles[index]; 
                
                try { await bot.EditMessageReplyMarkupAsync(chatId, msgId, replyMarkup: KeyboardBuilder.Build(session), cancellationToken: token); } catch { }
            }
            else if (cq.Data == "send")
            {
                await bot.EditMessageTextAsync(chatId, msgId, "🚀 Розсилаю завдання обраним (працюю у фоні)...", cancellationToken: token);
                _activeSessions.TryRemove(msgId, out _);

                _ = Task.Run(async () => 
                {
                    try
                    {
                        foreach (var kvp in session.Toggles)
                        {
                            if (kvp.Value) 
                            {
                                int uIndex = kvp.Key;
                                var messagesToSend = session.Messages[uIndex];
                                
                                foreach (var msgText in messagesToSend)
                                {
                                    try 
                                    {
                                        await bot.SendTextMessageAsync(chatId, msgText, parseMode: ParseMode.Html, cancellationToken: token);
                                        await Task.Delay(1500, token); 
                                    }
                                    catch (Exception e)
                                    {
                                        Console.WriteLine($"Не вдалося відправити повідомлення: {e.Message}");
                                        await Task.Delay(2000, token); 
                                    }
                                }
                            }
                        }
                        await bot.SendTextMessageAsync(chatId, "🏁 <b>Скликання завершено, бігом всі рабствувати</b>", parseMode: ParseMode.Html, cancellationToken: token);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Помилка під час фонової розсилки: {ex.Message}");
                    }
                });
            }
        }
    }

    public Task HandleErrorAsync(ITelegramBotClient bot, Exception ex, CancellationToken token)
    {
        Console.WriteLine($"Telegram API Error: {ex.Message}");
        return Task.CompletedTask;
    }
}