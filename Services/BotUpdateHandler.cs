using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq; // Обов'язково для роботи команди /corrections!
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
    private readonly ConcurrentDictionary<long, BotSettings> _chatSettings;

    public BotUpdateHandler(IGoogleSheetsService sheetsService)
    {
        _sheetsService = sheetsService;
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
            var threadId = message.MessageThreadId; // Отримуємо ID гілки (теми) для Форумів

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
                    messageThreadId: threadId, // Бот відповідає в ту саму гілку!
                    replyMarkup: markup, parseMode: ParseMode.Html, cancellationToken: token);
                return;
            }

            if (text.StartsWith("/help"))
            {
                string helpText = 
                    "🤖 <b>Довідка по Crystal Manager</b> 🤖\n\n" +
                    "📌 <b>Команди (тільки для адмінів):</b>\n" +
                    "🔹 /rise_up — відкриває панель керування розсилкою. І після тегає їх і вказує на незавершені завдання.\n" +
                    "🔹 /setting — відкриває меню налаштувань бота. Поки тут нічого ноухау немає.\n" +
                    "🔹 /refresh — оновлює базу даних з Google Таблиці.\n" +
                    "🔹 /cast — запис касту в таблицю. А точніше відповідаєш на повідомлення з акторами і ролями їхніми пишеш назва аркуша і номер серії і воно записує ці ролі і акторів в таблицю.\n" +
                    "🔹 /corrections — запис правок в таблицю. Відповідаєш на повідомлення з правками в яких на початку повідомлення має вже стояти назва аркушу і номер серії і воно записує в примітки таймінги правок а також відмічає що акторові треба виконати правки.\n" +
                    "🔹 /help — показує це повідомлення.";

                await bot.SendTextMessageAsync(chatId, helpText, 
                    messageThreadId: threadId, 
                    parseMode: ParseMode.Html, cancellationToken: token);
                return;
            }

            if (text.StartsWith("/refresh"))
            {
                var waitMsg = await bot.SendTextMessageAsync(chatId, "🔄 Оновлюю базу даних з Google Таблиці...", 
                    messageThreadId: threadId, cancellationToken: token);
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
                    await bot.SendTextMessageAsync(chatId, "На даний момент активних завдань немає (Або пропишіть /refresh)", 
                        messageThreadId: threadId, cancellationToken: token);
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
                        messageThreadId: threadId, 
                        replyMarkup: KeyboardBuilder.Build(session), parseMode: ParseMode.Html, cancellationToken: token);
                    
                    string sessionKey = $"{chatId}_{waitMsg.MessageId}";
                    _activeSessions[sessionKey] = session;
                }
                return;
            }

            if (text.StartsWith("/cast"))
            {
                if (message.ReplyToMessage == null || string.IsNullOrEmpty(message.ReplyToMessage.Text))
                {
                    await bot.SendTextMessageAsync(chatId, "❌ Команду /cast треба писати У ВІДПОВІДЬ (Reply) на повідомлення зі списком акторів.", 
                        messageThreadId: threadId, cancellationToken: token);
                    return;
                }

                var args = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (args.Length < 3)
                {
                    await bot.SendTextMessageAsync(chatId, "❌ Неправильний формат. Використовуйте: /cast НазваВкладки НомерСерії\nПриклад: /cast Зомбі 5", 
                        messageThreadId: threadId, cancellationToken: token);
                    return;
                }

                string sheetName = args[1];
                string episode = args[2];

                var lines = message.ReplyToMessage.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                var castList = new List<(string Character, string Actor)>();
                string deadline = "";

                foreach (var line in lines)
                {
                    string cleanLine = line.Trim();
                    int dashIndex = cleanLine.IndexOf('-');
                    if (dashIndex > 0)
                    {
                        string character = cleanLine.Substring(0, dashIndex).Trim();
                        string actor = cleanLine.Substring(dashIndex + 1).Trim();
                        if (actor.StartsWith("@")) actor = actor.Substring(1);
                        castList.Add((character, actor));
                    }
                    else
                    {
                        if (cleanLine.Any(char.IsDigit)) 
                        {
                            deadline = cleanLine;
                        }
                    }
                }

                if (castList.Count == 0)
                {
                    await bot.SendTextMessageAsync(chatId, "❌ Не вдалося знайти акторів у повідомленні. Перевірте формат (Персонаж - Актор).", 
                        messageThreadId: threadId, cancellationToken: token);
                    return;
                }

                var waitMsg = await bot.SendTextMessageAsync(chatId, $"⏳ Записую {castList.Count} ролей у вкладку '{sheetName}'...", 
                    messageThreadId: threadId, cancellationToken: token);

                try
                {
                    await _sheetsService.AddCastListAsync(sheetName, episode, castList, deadline);
                    await bot.EditMessageTextAsync(chatId, waitMsg.MessageId, $"✅ Успішно додано {castList.Count} записів у '{sheetName}' (Серія {episode})!", cancellationToken: token);
                }
                catch (Exception ex)
                {
                    await bot.EditMessageTextAsync(chatId, waitMsg.MessageId, $"❌ Помилка запису в таблицю: {ex.Message}", cancellationToken: token);
                }
                return;
            }

            if (text.StartsWith("/corrections"))
            {
                if (message.ReplyToMessage == null || string.IsNullOrEmpty(message.ReplyToMessage.Text))
                {
                    await bot.SendTextMessageAsync(chatId, "❌ Команду /corrections треба писати У ВІДПОВІДЬ (Reply) на повідомлення з правками.", 
                        messageThreadId: threadId, cancellationToken: token);
                    return;
                }

                var lines = message.ReplyToMessage.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length < 2) return;

                var titleParts = lines[0].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (titleParts.Length < 2)
                {
                    await bot.SendTextMessageAsync(chatId, "❌ Не знайдено назви проєкту та серії у першому рядку.\nФормат: САЙКІ 1", 
                        messageThreadId: threadId, cancellationToken: token);
                    return;
                }
                
                string episode = titleParts.Last();
                string sheetName = string.Join(" ", titleParts.Take(titleParts.Length - 1));

                var corrections = new Dictionary<string, List<string>>();
                string currentTag = null;

                for (int i = 1; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    
                    if (line.StartsWith("@"))
                    {
                        currentTag = line;
                        if (!corrections.ContainsKey(currentTag))
                            corrections[currentTag] = new List<string>();
                    }
                    else if (!string.IsNullOrEmpty(currentTag))
                    {
                        int dashIndex = line.IndexOf('-');
                        if (dashIndex > 0)
                        {
                            string timeCode = line.Substring(0, dashIndex).Trim();
                            corrections[currentTag].Add(timeCode);
                        }
                        else
                        {
                            corrections[currentTag].Add(line);
                        }
                    }
                }

                if (corrections.Count == 0)
                {
                    await bot.SendTextMessageAsync(chatId, "❌ Не вдалося знайти теги акторів (@тег).", 
                        messageThreadId: threadId, cancellationToken: token);
                    return;
                }

                var waitMsg = await bot.SendTextMessageAsync(chatId, $"⏳ Записую правки для {corrections.Count} акторів у вкладку '{sheetName}'...", 
                    messageThreadId: threadId, cancellationToken: token);

                try
                {
                    await _sheetsService.UpdateCorrectionsAsync(sheetName, episode, corrections);
                    await bot.EditMessageTextAsync(chatId, waitMsg.MessageId, $"✅ Успішно додано правки у '{sheetName}' (Серія {episode})!", cancellationToken: token);
                }
                catch (Exception ex)
                {
                    await bot.EditMessageTextAsync(chatId, waitMsg.MessageId, $"❌ Помилка запису в таблицю: {ex.Message}", cancellationToken: token);
                }
                return;
            }
        }

        // Блок для кнопок (CallbackQuery)
        if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery != null)
        {
            var cq = update.CallbackQuery;
            var msgId = cq.Message.MessageId;
            var chatId = cq.Message.Chat.Id;
            var threadId = cq.Message.MessageThreadId; // Отримуємо ID гілки для кнопок

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

                SettingsManager.Save(_chatSettings);

                try { await bot.EditMessageReplyMarkupAsync(chatId, msgId, replyMarkup: KeyboardBuilder.BuildSettings(settings), cancellationToken: token); } catch { }
                try { await bot.AnswerCallbackQueryAsync(cq.Id, "Налаштування збережено!", cancellationToken: token); } catch { }
                return;
            }

            try { await bot.AnswerCallbackQueryAsync(cq.Id, cancellationToken: token); } catch { }

            string sessionKey = $"{chatId}_{msgId}";
            if (!_activeSessions.ContainsKey(sessionKey))
            {
                try { await bot.SendTextMessageAsync(chatId, "Ця сесія вже застаріла. Пропишіть /rise_up ще раз.", 
                    messageThreadId: threadId, cancellationToken: token); } catch { }
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
                                            await bot.SendTextMessageAsync(chatId, msgText, 
                                                messageThreadId: threadId, // Бот розсилає відповіді в ту саму гілку
                                                parseMode: ParseMode.Html, cancellationToken: token);
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
                        await bot.SendTextMessageAsync(chatId, "🏁 <b>Скликання завершено, бігом всі рабствувати</b>", 
                            messageThreadId: threadId, 
                            parseMode: ParseMode.Html, cancellationToken: token);
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