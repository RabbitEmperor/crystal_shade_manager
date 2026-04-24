using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using crystal_shade_manager.Interfaces;
using crystal_shade_manager.Services;
using crystal_shade_manager.Helpers;

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

    public async Task HandleAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, CancellationToken ct)
    {
        // ВАЖЛИВО: Отримуємо дані безпосередньо з повідомлення, де була натиснута кнопка
        if (callbackQuery?.Data == null || callbackQuery.Message == null) return;

        string data = callbackQuery.Data;
        long chatId = callbackQuery.Message.Chat.Id;
        int messageId = callbackQuery.Message.MessageId;
        int? threadId = callbackQuery.Message.MessageThreadId; 
        string sessionKey = $"{chatId}_{messageId}";

        // 1. СЕСІЇ RISE UP (Класична розсилка)
        if (_state.ActiveSessions.TryGetValue(sessionKey, out var session))
        {
            if (data == "send")
            {
                await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "🚀 Розсилку розпочато!", cancellationToken: ct);

                // Оновлюємо статус (додано threadId для надійності в топіках)
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
                                try 
                                { 
                                    // Відправляємо саме в цей чат і саме в цю гілку
                                    await botClient.SendTextMessageAsync(
                                        chatId: chatId, 
                                        text: msg, 
                                        messageThreadId: threadId, 
                                        parseMode: ParseMode.Html, 
                                        cancellationToken: ct); 
                                }
                                catch { }
                            }
                        }
                    }
                }

                _state.ActiveSessions.TryRemove(sessionKey, out _);
                
                // Фінальне повідомлення в ту саму гілку
                await botClient.SendTextMessageAsync(
                    chatId: chatId, 
                    text: "✅ <b>Розсилку завершено!</b>", 
                    messageThreadId: threadId, 
                    parseMode: ParseMode.Html, 
                    cancellationToken: ct);
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

        // 3. TITLES RISE UP (Розсилка по тайтлах)
        if (data.StartsWith("tru|") || data == "tru_all")
        {
            if (_sheetsService != null)
            {
                await ProcessTitlesRiseUp(botClient, callbackQuery, ct);
            }
        }
    }

    private async Task ProcessTitlesRiseUp(ITelegramBotClient botClient, CallbackQuery callbackQuery, CancellationToken ct)
    {
        string data = callbackQuery.Data;
        long chatId = callbackQuery.Message.Chat.Id;
        int? threadId = callbackQuery.Message.MessageThreadId;

        await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "Формую звіт...", cancellationToken: ct);
        
        await botClient.EditMessageTextAsync(
            chatId: chatId, 
            messageId: callbackQuery.Message.MessageId, 
            text: "⏳ <b>Обробка запиту по тайтлах...</b>", 
            parseMode: ParseMode.Html, 
            cancellationToken: ct);

        string targetTitle = data == "tru_all" ? "ALL" : data.Split('|')[1];
        var allTasks = await _sheetsService.GetTitlesTasksAsync();
        var teamTags = await _sheetsService.GetTeamTagsAsync();

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
                    string rawName = task.Username.Trim();
                    string userPing = (teamTags.TryGetValue(rawName, out string tag) && tag != "-") 
                        ? tag : (rawName.StartsWith("@") ? rawName : "@" + rawName);

                    sb.AppendLine($" ├ 👤 {userPing} — <i>{task.Role}</i>");
                }
                sb.AppendLine();
            }

            await botClient.SendTextMessageAsync(
                chatId: chatId,
                messageThreadId: threadId, 
                text: sb.ToString(),
                parseMode: ParseMode.Html,
                cancellationToken: ct);
        }

        await botClient.SendTextMessageAsync(
            chatId: chatId,
            messageThreadId: threadId,
            text: "✅ <b>Розсилку по тайтлах завершено!</b>",
            parseMode: ParseMode.Html,
            cancellationToken: ct);
    }
}