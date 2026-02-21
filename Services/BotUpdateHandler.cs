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
    
    private readonly ConcurrentDictionary<string, RiseUpSession> _activeSessions = new();
    
    // Словник налаштувань тепер буде братися з файлу
    private readonly ConcurrentDictionary<long, BotSettings> _chatSettings;

    public BotUpdateHandler(IGoogleSheetsService sheetsService)
    {
        _sheetsService = sheetsService;
        // Завантажуємо збережені налаштування з жорсткого диска при запуску
        _chatSettings = SettingsManager.Load(); 
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

    private BotSettings GetSettings(long chatId)
    {
        return _chatSettings.GetOrAdd(chatId, _ => 
        {
            var newSettings = new BotSettings();
            // Якщо додався новий чат — відразу зберігаємо це у файл
            SettingsManager.Save(_chatSettings); 
            return newSettings;
        });
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
        catch
        {
            return false;
        }
    }

    public async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken token)
    {
        if (update.Type == UpdateType.Message && update.Message?.Text != null)
        {
            var message = update.Message;
            var text = message.Text;
            var chatId = message.Chat.Id;

            if (text.StartsWith("/"))
            {
                bool isAdmin = await IsAdminAsync(bot, chatId, message.From!.Id, token);
                if (!isAdmin) return; 
            }

            if (text.StartsWith("/setting"))
            {
                var settings = GetSettings(chatId);
                var markup = KeyboardBuilder.BuildSettings(settings);
                await bot.SendTextMessageAsync(chatId, "⚙️ <b>Налаштування бота (для цього чату):</b>\n<i>Зміни зберігаються автоматично</i>", 
                    replyMarkup: markup, parseMode: ParseMode.Html, cancellationToken: token);
                return;
            }

            if (text.StartsWith("/help"))
            {
                string helpText = 
                    "🤖 <b>Довідка по Crystal Manager</b> 🤖\n\n" +
                    "📌 <b>Команди (тільки для адмінів):</b>\n" +
                    "🔹 /rise_up — відкриває панель керування розсилкою.\n" +
                    "🔹 /setting — відкриває меню налаштувань бота.\n" +
                    "🔹 /refresh — оновлює базу даних з Google Таблиці.\n" +
                    "🔹 /help — показує це повідомлення.\n\n" +
                    "⚙️ <b>Як це працює:</b>\n" +
                    "Бот бере до уваги тільки тих людей, які вписані у вкладку «Команда», і тільки завдання зі статусом «виконується» або «правки».";

                await bot.SendTextMessageAsync(chatId, helpText, parseMode: ParseMode.Html, cancellationToken: token);
                return;
            }

            if (text.StartsWith("/refresh"))
            {
                var waitMsg = await bot.SendTextMessageAsync(chatId, "🔄 Оновлюю базу даних з Google Таблиці...", cancellationToken: token);
                try 
                {
                    _cachedUserMessages = await _sheetsService.GetUserTaskMessagesAsync() ?? new Dictionary<string, List<string>>();
                    await bot.EditMessageTextAsync(chatId, waitMsg.MessageId, $"✅ Базу оновлено! Активних рабів: {_cachedUserMessages.Count}", cancellationToken: token);
                }
                catch (Exception ex) 
                {
                    await bot.EditMessageTextAsync(chatId, waitMsg.MessageId, $"❌ Помилка оновлення: {ex.Message}", cancellationToken: token);
                }
                return;
            }

            if (text.StartsWith("/rise_up"))
            {
                if (_cachedUserMessages.Count == 0)
                {
                    await bot.SendTextMessageAsync(chatId, "На даний момент активних завдань немає (Або пропишіть /refresh)", cancellationToken: token);
                }
                else
                {
                    var settings = GetSettings(chatId);
                    var session = new RiseUpSession();
                    int index = 0;
                    foreach (var kvp in _cachedUserMessages)
                    {
                        session.Users.Add(kvp.Key);
                        session.Toggles[index] = settings.SelectAllByDefault;
                        session.Messages[index] = kvp.Value;
                        index++;
                    }
                    
                    var waitMsg = await bot.SendTextMessageAsync(chatId, "📋 <b>Оберіть рабів для розсилки:</b>", 
                        replyMarkup: KeyboardBuilder.Build(session), parseMode: ParseMode.Html, cancellationToken: token);
                    
                    string sessionKey = $"{chatId}_{waitMsg.MessageId}";
                    _activeSessions[sessionKey] = session;
                }
                return;
            }
        }

        if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery != null)
        {
            var cq = update.CallbackQuery;
            var msgId = cq.Message.MessageId;
            var chatId = cq.Message.Chat.Id;

            bool isAdmin = await IsAdminAsync(bot, chatId, cq.From.Id, token);
            if (!isAdmin)
            {
                await bot.AnswerCallbackQueryAsync(cq.Id, "❌ Тільки адміністратори можуть натискати ці кнопки!", showAlert: true, cancellationToken: token);
                return;
            }

            if (cq.Data.StartsWith("set_"))
            {
                var settings = GetSettings(chatId);

                if (cq.Data == "set_toggle_select")
                    settings.SelectAllByDefault = !settings.SelectAllByDefault;
                else if (cq.Data == "set_toggle_speed")
                    settings.SafeModeDelay = !settings.SafeModeDelay;

                // --- НАЙГОЛОВНІШЕ: Зберігаємо нові налаштування у файл! ---
                SettingsManager.Save(_chatSettings);

                try { await bot.EditMessageReplyMarkupAsync(chatId, msgId, replyMarkup: KeyboardBuilder.BuildSettings(settings), cancellationToken: token); } catch { }
                try { await bot.AnswerCallbackQueryAsync(cq.Id, "Налаштування збережено!", cancellationToken: token); } catch { }
                return;
            }

            try { await bot.AnswerCallbackQueryAsync(cq.Id, cancellationToken: token); } catch { }

            string sessionKey = $"{chatId}_{msgId}";
            if (!_activeSessions.ContainsKey(sessionKey))
            {
                try { await bot.SendTextMessageAsync(chatId, "Ця сесія вже застаріла. Пропишіть /rise_up ще раз.", cancellationToken: token); } catch { }
                return;
            }

            var session = _activeSessions[sessionKey];

            if (cq.Data.StartsWith("t_"))
            {
                int index = int.Parse(cq.Data.Substring(2));
                session.Toggles[index] = !session.Toggles[index]; 
                
                try { await bot.EditMessageReplyMarkupAsync(chatId, msgId, replyMarkup: KeyboardBuilder.Build(session), cancellationToken: token); } catch { }
            }
            else if (cq.Data == "send")
            {
                await bot.EditMessageTextAsync(chatId, msgId, "🚀 Розсилаю завдання обраним (працюю у фоні)...", cancellationToken: token);
                _activeSessions.TryRemove(sessionKey, out _);

                var settings = GetSettings(chatId);
                int delayTime = settings.SafeModeDelay ? 3100 : 1500;

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
                                    bool isSent = false;
                                    int retries = 0;

                                    while (!isSent && retries < 3)
                                    {
                                        try 
                                        {
                                            await bot.SendTextMessageAsync(chatId, msgText, parseMode: ParseMode.Html, cancellationToken: token);
                                            isSent = true; 
                                            await Task.Delay(delayTime, token); 
                                        }
                                        catch (Telegram.Bot.Exceptions.ApiRequestException apiEx) when (apiEx.ErrorCode == 429)
                                        {
                                            int retryAfter = apiEx.Parameters?.RetryAfter ?? 10;
                                            Console.WriteLine($"[Антиспам] Telegram просить паузу. Чекаю {retryAfter} секунд...");
                                            await Task.Delay((retryAfter * 1000) + 500, token);
                                            retries++;
                                        }
                                        catch (Exception e)
                                        {
                                            Console.WriteLine($"Не вдалося відправити повідомлення: {e.Message}");
                                            await Task.Delay(3000, token); 
                                            retries++;
                                        }
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