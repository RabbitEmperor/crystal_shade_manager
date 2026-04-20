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
        if (callbackQuery?.Data == null || callbackQuery.Message == null) return;

        string data = callbackQuery.Data;
        long chatId = callbackQuery.Message.Chat.Id;
        int messageId = callbackQuery.Message.MessageId;
        string sessionKey = $"{chatId}_{messageId}";

        // 1. ОБРОБКА СЕСІЇ RISE UP (Класична розсилка)
        if (_state.ActiveSessions.TryGetValue(sessionKey, out var session))
        {
            if (data == "send")
            {
                await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "🚀 Розсилку розпочато!", cancellationToken: ct);

                // Редагуємо повідомлення з кнопками, щоб воно просто висіло як статус
                await botClient.EditMessageTextAsync(chatId, messageId, "⏳ <b>Йде відправка списків...</b>", parseMode: ParseMode.Html, cancellationToken: ct);

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
                                    // Відправляємо кожній людині список ОКРЕМИМ повідомленням
                                    await botClient.SendTextMessageAsync(chatId, msg, parseMode: ParseMode.Html, cancellationToken: ct); 
                                }
                                catch { }
                            }
                        }
                    }
                }

                _state.ActiveSessions.TryRemove(sessionKey, out _);
                
                // ВІДПРАВЛЯЄМО ПОВІДОМЛЕННЯ ПРО ЗАВЕРШЕННЯ В КІНЕЦЬ ЧАТУ
                await botClient.SendTextMessageAsync(chatId, "✅ <b>Розсилку завершено!</b>", parseMode: ParseMode.Html, cancellationToken: ct);
                return;
            }

            if (data.StartsWith("t_"))
            {
                if (int.TryParse(data.Replace("t_", ""), out int idx))
                {
                    if (session.Toggles.ContainsKey(idx))
                    {
                        session.Toggles[idx] = !session.Toggles[idx];
                        await botClient.EditMessageReplyMarkupAsync(chatId, messageId, replyMarkup: KeyboardBuilder.Build(session), cancellationToken: ct);
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

    await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "Формую звіт...", cancellationToken: ct);
    
    await botClient.EditMessageTextAsync(
        chatId: chatId, 
        messageId: callbackQuery.Message.MessageId, 
        text: "⏳ <b>Обробка запиту по тайтлах...</b>", 
        parseMode: ParseMode.Html, 
        cancellationToken: ct);

    string targetTitle = data == "tru_all" ? "ALL" : data.Split('|')[1];
    var allTasks = await _sheetsService.GetTitlesTasksAsync();
    
    // Отримуємо словник тегів (він уже має бути Case-Insensitive завдяки нашому GetTeamTagsAsync)
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
                string userPing;

                // 1. Спробуємо знайти в словнику (ігноруючи регістр)
                if (teamTags.TryGetValue(rawName, out string tag) && tag != "-")
                {
                    userPing = tag;
                }
                else
                {
                    // 2. Якщо в словнику немає, але в "Команді" для цього юзера прочерк — лишаємо текст
                    if (teamTags.ContainsKey(rawName) && teamTags[rawName] == "-")
                    {
                        userPing = rawName;
                    }
                    else
                    {
                        // 3. Якщо взагалі не знайшли — додаємо @ примусово, щоб спробувати тегнути
                        userPing = rawName.StartsWith("@") ? rawName : "@" + rawName;
                    }
                }

                sb.AppendLine($" ├ 👤 {userPing} — <i>{task.Role}</i>");
            }
            sb.AppendLine();
        }

        await botClient.SendTextMessageAsync(
            chatId: chatId,
            text: sb.ToString(),
            parseMode: ParseMode.Html,
            cancellationToken: ct);
    }

    await botClient.SendTextMessageAsync(
        chatId: chatId,
        text: "✅ <b>Розсилку по тайтлах завершено!</b>",
        parseMode: ParseMode.Html,
        cancellationToken: ct);
}
}