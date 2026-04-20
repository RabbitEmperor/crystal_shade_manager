using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using crystal_shade_manager.Interfaces;
using crystal_shade_manager.Services;

namespace crystal_shade_manager.Commands;

public class CastCommand : ITelegramCommand
{
    private readonly IGoogleSheetsService _sheetsService;

    public string Trigger => "/cast";

    public CastCommand(IGoogleSheetsService sheetsService)
    {
        _sheetsService = sheetsService;
    }

    public async Task ExecuteAsync(ITelegramBotClient botClient, Message message, CancellationToken token)
    {
        if (string.IsNullOrEmpty(message.Text))
        {
            return;
        }
        
        var chatId = message.Chat.Id;
        var threadId = message.MessageThreadId;
        var text = message.Text;

        if (message.ReplyToMessage == null)
        {
            await botClient.SendTextMessageAsync(chatId, "❌ Команду /cast треба писати У ВІДПОВІДЬ (Reply).", messageThreadId: threadId, cancellationToken: token);
            return;
        }

        string replyText = message.ReplyToMessage.Text ?? message.ReplyToMessage.Caption;
        if (string.IsNullOrEmpty(replyText)) return;

        var match = Regex.Match(text, @"^/cast(?:@[^\s]+)?\s+""([^""]+)""\s+(.+)$");
        if (!match.Success)
        {
            await botClient.SendTextMessageAsync(chatId, "❌ Формат: /cast \"Назва\" Серія", messageThreadId: threadId, cancellationToken: token);
            return;
        }

        string sheetName = match.Groups[1].Value;
        string episode = match.Groups[2].Value;

        var lines = replyText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
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
            else if (cleanLine.Any(char.IsDigit)) 
            {
                deadline = cleanLine;
            }
        }

        if (castList.Count == 0) return;

        var waitMsg = await botClient.SendTextMessageAsync(chatId, $"⏳ Записую {castList.Count} ролей...", messageThreadId: threadId, cancellationToken: token);

        try
        {
            await _sheetsService.AddCastListAsync(sheetName, episode, castList, deadline);
            await botClient.EditMessageTextAsync(chatId, waitMsg.MessageId, $"✅ Успішно записано!", cancellationToken: token);
        }
        catch (Exception ex)
        {
            await botClient.EditMessageTextAsync(chatId, waitMsg.MessageId, $"❌ Помилка: {ex.Message}", cancellationToken: token);
        }
    }
}