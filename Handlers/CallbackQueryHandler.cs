using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
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

    private async Task SafeSendAsync(ITelegramBotClient botClient, long chatId, int? threadId, string text, CancellationToken ct)
    {
        try
        {
            await botClient.SendTextMessageAsync(chatId: chatId, messageThreadId: threadId, text: text, parseMode: ParseMode.Html, cancellationToken: ct);
        }
        catch (ApiRequestException ex) when (ex.Message.Contains("TOPIC_CLOSED"))
        {
            await botClient.SendTextMessageAsync(chatId: chatId, text: $"⚠️ (Гілка закрита) {text}", parseMode: ParseMode.Html, cancellationToken: ct);
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
                await botClient.EditMessageTextAsync(chatId: chatId, messageId: messageId, text: "⏳ <b>Йде відправка списків...</b>", parseMode: ParseMode.Html, cancellationToken: ct);
                for (int i = 0; i < session.Users.Count; i++) {
                    if (session.Toggles.TryGetValue(i, out bool isSelected) && isSelected) {
                        if (session.Messages.TryGetValue(i, out var userMsgs)) {
                            foreach (var msg in userMsgs) {
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
                if (int.TryParse(data.Replace("t_", ""), out int idx)) {
                    if (session.Toggles.ContainsKey(idx)) {
                        session.Toggles[idx] = !session.Toggles[idx];
                        await botClient.EditMessageReplyMarkupAsync(chatId: chatId, messageId: messageId, replyMarkup: KeyboardBuilder.Build(session), cancellationToken: ct);
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
            if (settings != null) {
                settings.SelectAllByDefault = !settings.SelectAllByDefault;
                _state.SaveSettings();
            }
            await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "Налаштування оновлено", cancellationToken: ct);
            return;
        }

        // 3. TITLES RISE UP - вибір тайтлу
        if (data.StartsWith("tru|") || data == "tru_all")
        {
            if (_sheetsService != null) await ProcessTitlesRiseUp(botClient, callbackQuery, ct);
            return;
        }

        // 4. TITLES RISE UP - вибір епізоду (НОВЕ)
        if (data.StartsWith("tep|"))
        {
            if (_sheetsService != null) await ProcessEpisodeRiseUp(botClient, callbackQuery, ct);
            return;
        }
    }

    // МЕНЮ ВИБОРУ ЕПІЗОДУ
    private async Task ProcessTitlesRiseUp(ITelegramBotClient botClient, CallbackQuery callbackQuery, CancellationToken ct)
    {
        string data = callbackQuery.Data;
        long chatId = callbackQuery.Message.Chat.Id;

        string targetTitle = data == "tru_all" ? "ALL" : data.Split('|')[1];

        if (targetTitle == "ALL")
        {
            await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "Формую всі...", cancellationToken: ct);
            await SendRiseUpData(botClient, chatId, callbackQuery.Message.MessageThreadId, "ALL", "ALL", callbackQuery.Message.MessageId, ct);
            return;
        }

        await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "Завантажую епізоди...", cancellationToken: ct);
        
        var allTasks = await _sheetsService.GetTitlesTasksAsync();
        var titleTasks = allTasks.Where(t => t.TitleName == targetTitle).ToList();
        
        if (!titleTasks.Any())
        {
            await botClient.EditMessageTextAsync(chatId, callbackQuery.Message.MessageId, "Епізодів не знайдено.", cancellationToken: ct);
            return;
        }

        var episodes = titleTasks.Select(t => t.Episode).Distinct().OrderBy(e => e).ToList();
        var inlineKeyboard = new List<IEnumerable<InlineKeyboardButton>>();
        
        // Захист від ліміту Telegram в 64 байти для CallbackData
        string safeTitle = targetTitle.Length > 15 ? targetTitle.Substring(0, 15) : targetTitle;

        foreach (var ep in episodes)
        {
            string safeEp = ep.Length > 15 ? ep.Substring(0, 15) : ep;
            inlineKeyboard.Add(new[] { InlineKeyboardButton.WithCallbackData($"📺 {ep}", $"tep|{safeTitle}|{safeEp}") });
        }
        
        inlineKeyboard.Add(new[] { InlineKeyboardButton.WithCallbackData("🎬 Всі епізоди", $"tep|{safeTitle}|ALL") });

        await botClient.EditMessageTextAsync(
            chatId: chatId,
            messageId: callbackQuery.Message.MessageId,
            text: $"🎬 <b>{targetTitle}</b>\nОберіть епізод для виводу:",
            parseMode: ParseMode.Html,
            replyMarkup: new InlineKeyboardMarkup(inlineKeyboard),
            cancellationToken: ct);
    }

    // ОБРОБКА НАТИСКАННЯ НА ЕПІЗОД
    private async Task ProcessEpisodeRiseUp(ITelegramBotClient botClient, CallbackQuery callbackQuery, CancellationToken ct)
    {
        string data = callbackQuery.Data;
        long chatId = callbackQuery.Message.Chat.Id;
        int? threadId = callbackQuery.Message.MessageThreadId;
        
        var parts = data.Split('|');
        string targetTitleSafe = parts[1];
        string targetEpSafe = parts[2];

        await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "Обробка...", cancellationToken: ct);
        await SendRiseUpData(botClient, chatId, threadId, targetTitleSafe, targetEpSafe, callbackQuery.Message.MessageId, ct);
    }

    // ФІНАЛЬНА ВІДПРАВКА
    private async Task SendRiseUpData(ITelegramBotClient botClient, long chatId, int? threadId, string targetTitleSafe, string targetEpSafe, int messageId, CancellationToken ct)
    {
        await botClient.EditMessageTextAsync(
            chatId: chatId,
            messageId: messageId,
            text: "⏳ <b>Формування списку...</b>",
            parseMode: ParseMode.Html,
            cancellationToken: ct);

        var allTasks = await _sheetsService.GetTitlesTasksAsync();
        var teamTags = await _sheetsService.GetTeamTagsAsync();

        var filteredTasks = allTasks;
        
        if (targetTitleSafe != "ALL")
            filteredTasks = filteredTasks.Where(t => t.TitleName.StartsWith(targetTitleSafe)).ToList();
            
        if (targetEpSafe != "ALL")
            filteredTasks = filteredTasks.Where(t => t.Episode.StartsWith(targetEpSafe)).ToList();

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
                    string rawName = task.Username.Replace("@", "").Trim();
                    string displayName = "";

                    if (!string.IsNullOrEmpty(rawName) && teamTags != null)
                    {
                        foreach (var kvp in teamTags)
                        {
                            string cleanKey = kvp.Key?.Replace("@", "").Trim() ?? "";
                            string cleanValue = kvp.Value?.Replace("@", "").Trim() ?? "";

                            if (cleanValue.Equals(rawName, StringComparison.OrdinalIgnoreCase))
                            {
                                // Якщо замість тегу стоїть мінус, виводимо псевдонім. Інакше - тег із @
                                displayName = cleanValue == "-" ? cleanKey : "@" + cleanValue; 
                                break;
                            }
                            else if (cleanKey.Equals(rawName, StringComparison.OrdinalIgnoreCase))
                            {
                                // Випадок з Lumen: знайдено псевдонім. Якщо тег "-", виводимо псевдонім.
                                displayName = cleanValue == "-" ? cleanKey : kvp.Value; 
                                break;
                            }
                        }
                    }

                    // Запасний варіант, якщо людини немає в базі
                    if (string.IsNullOrEmpty(displayName)) 
                    {
                        string safeTag = rawName.Replace(" ", "_");
                        displayName = safeTag == "-" ? rawName : "@" + safeTag;
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