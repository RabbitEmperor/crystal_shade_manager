using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using crystal_shade_manager.Interfaces;
using crystal_shade_manager.Services;
using crystal_shade_manager.Helpers;
using Telegram.Bot.Exceptions;

namespace crystal_shade_manager.Handlers;

public class CallbackQueryHandler
{
    private readonly IStateManager _state;
    private readonly IGoogleSheetsService _sheetsService;

    public CallbackQueryHandler(IStateManager state, IGoogleSheetsService sheetsService = null)
    {
        _state = state;
        _sheetsService = sheetsService;
    }

    // Безпечний метод для відправки повідомлень (анти-краш для топіків)
    private async Task SafeSendAsync(ITelegramBotClient botClient, long chatId, int? threadId, string text, CancellationToken ct)
    {
        try
        {
            await botClient.SendTextMessageAsync(
                chatId: chatId,
                messageThreadId: threadId,
                text: text,
                parseMode: ParseMode.Html,
                cancellationToken: ct);
        }
        catch (ApiRequestException ex) when (ex.Message.Contains("TOPIC_CLOSED"))
        {
            // ПЛАН "Б": Якщо гілка закрита, шлемо в корінь чату (без threadId)
            await botClient.SendTextMessageAsync(
                chatId: chatId,
                text: $"⚠️ (Гілка закрита) {text}",
                parseMode: ParseMode.Html,
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error] Непередбачена помилка відправки: {ex.Message}");
        }
    }

    public async Task HandleAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, CancellationToken ct)
    {
        if (callbackQuery?.Data == null || callbackQuery.Message == null) return;

        string data = callbackQuery.Data;
        long chatId = callbackQuery.Message.Chat.Id;
        int messageId = callbackQuery.Message.MessageId;
        int? threadId = callbackQuery.Message.MessageThreadId;
        string sessionKey = $"{chatId}_{messageId}";

        // 1. СЕСІЇ RISE UP
        if (_state.ActiveSessions.TryGetValue(sessionKey, out var session))
        {
            if (data == "send")
            {
                await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "🚀 Розпочато!", cancellationToken: ct);

                await botClient.EditMessageTextAsync(
                    chatId: chatId,
                    messageId: messageId,
                    text: "⏳ <b>Йде відправка списків...</b>",
                    parseMode: ParseMode.Html,
                    cancellationToken: ct);

                for (int i = 0; i < session.Users.Count; i++)
                {
                    if (session.Toggles.TryGetValue(i, out bool isSelected) && isSelected)
                    {
                        if (session.Messages.TryGetValue(i, out var userMsgs))
                        {
                            foreach (var msg in userMsgs)
                            {
                                await SafeSendAsync(botClient, chatId, threadId, msg, ct);
                            }
                        }
                    }
                }

                _state.ActiveSessions.TryRemove(sessionKey, out _);
                await SafeSendAsync(botClient, chatId, threadId, "✅ <b>Розсилку завершено!</b>", ct);
                return;
            }

            if (data.StartsWith("t_"))
            {
                if (int.TryParse(data.Replace("t_", ""), out int idx))
                {
                    if (session.Toggles.ContainsKey(idx))
                    {
                        session.Toggles[idx] = !session.Toggles[idx];
                        await botClient.EditMessageReplyMarkupAsync(
                            chatId: chatId,
                            messageId: messageId,
                            replyMarkup: KeyboardBuilder.Build(session),
                            cancellationToken: ct);
                    }
                }
                await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, cancellationToken: ct);
                return;
            }
        }

        // 2. НАЛАШТУВАННЯ
        if (data == "set_toggle_select")
        {
            var settings = _state.GetSettings(chatId);
            if (settings != null)
            {
                settings.SelectAllByDefault = !settings.SelectAllByDefault;
                _state.SaveSettings();
            }
            await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "Налаштування оновлено", cancellationToken: ct);
            return;
        }

        // 3. TITLES RISE UP
        if (data.StartsWith("tru|") || data == "tru_all")
        {
            if (_sheetsService != null) await ProcessTitlesRiseUp(botClient, callbackQuery, ct);
        }
    }

    private async Task ProcessTitlesRiseUp(ITelegramBotClient botClient, CallbackQuery callbackQuery, CancellationToken ct)
    {
        string data = callbackQuery.Data;
        long chatId = callbackQuery.Message.Chat.Id;
        int? threadId = callbackQuery.Message.MessageThreadId;

        await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "Формую...", cancellationToken: ct);
        
        await botClient.EditMessageTextAsync(
            chatId: chatId,
            messageId: callbackQuery.Message.MessageId,
            text: "⏳ <b>Обробка запиту...</b>",
            parseMode: ParseMode.Html,
            cancellationToken: ct);

        string targetTitle = data == "tru_all" ? "ALL" : data.Split('|')[1];
        var allTasks = await _sheetsService.GetTitlesTasksAsync();
        
        // Отримуємо словник з гугл таблиць (де Key - Псевдонім, Value - Тег)
        var teamTags = await _sheetsService.GetTeamTagsAsync();

        // Створюємо ЗВОРОТНИЙ словник: шукаємо псевдонім за тегом
        var tagToPseudonym = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in teamTags)
        {
            string cleanTag = kvp.Value.Replace("@", "").Trim();
            if (!string.IsNullOrEmpty(cleanTag) && cleanTag != "-")
            {
                tagToPseudonym[cleanTag] = kvp.Key; // Наприклад: [rabbitemperor] = "Імператор кроликів"
            }
        }

        var filteredTasks = targetTitle == "ALL" 
            ? allTasks 
            : allTasks.Where(t => t.TitleName.Contains(targetTitle)).ToList();

        var groupedTitles = filteredTasks.GroupBy(t => t.TitleName).OrderBy(g => g.Key);

        foreach (var titleGroup in groupedTitles)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"🎬 <b>{titleGroup.Key}</b>\n");

            foreach (var epGroup in titleGroup.GroupBy(t => t.Episode).OrderBy(g => g.Key))
            {
                sb.AppendLine($"📺 {epGroup.Key}:");
                foreach (var task in epGroup)
                {
                    string original = task.Username.Trim();
                    string rawName = original;

                    // 1. Жорстко відрізаємо все, що йде після пробілу
                    int spaceIndex = rawName.IndexOf(' ');
                    if (spaceIndex > 0) rawName = rawName.Substring(0, spaceIndex);

                    // 2. Жорстко відрізаємо все, що йде після дужки (якщо пробілу не було)
                    int bracketIndex = rawName.IndexOf('(');
                    if (bracketIndex > 0) rawName = rawName.Substring(0, bracketIndex);

                    // 3. Чистимо від @ і зайвих пробілів
                    rawName = rawName.Replace("@", "").Trim();
                    
                    // За замовчуванням ставимо тег (якщо не знайдемо в базі)
                    string displayName = string.IsNullOrEmpty(rawName) ? original : "@" + rawName;

                    // 4. Шукаємо в словнику Команди
                    if (!string.IsNullOrEmpty(rawName) && teamTags != null)
                    {
                        foreach (var kvp in teamTags)
                        {
                            string cleanKey = kvp.Key?.Replace("@", "").Trim() ?? "";
                            string cleanValue = kvp.Value?.Replace("@", "").Trim() ?? "";

                            // Якщо знайшли тег у колонці значень -> беремо псевдонім
                            if (cleanValue.Equals(rawName, StringComparison.OrdinalIgnoreCase))
                            {
                                displayName = kvp.Key; 
                                break;
                            }
                            // Якщо словник раптом перевернутий -> беремо іншу колонку
                            else if (cleanKey.Equals(rawName, StringComparison.OrdinalIgnoreCase))
                            {
                                displayName = kvp.Value;
                                break;
                            }
                        }
                    }

                    sb.AppendLine($" ├ 👤 {displayName} — <i>{task.Role}</i>");
                }
                sb.AppendLine();
            }

            await SafeSendAsync(botClient, chatId, threadId, sb.ToString(), ct);
        }

        await SafeSendAsync(botClient, chatId, threadId, "✅ <b>Розсилку по тайтлах завершено!</b>", ct);
    }
}