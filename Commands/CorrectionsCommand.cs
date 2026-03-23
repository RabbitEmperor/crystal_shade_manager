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

public class CorrectionsCommand : ITelegramCommand
{
    private readonly IGoogleSheetsService _sheetsService;
    public string Trigger => "/corrections";

    public CorrectionsCommand(IGoogleSheetsService sheetsService)
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
            await botClient.SendTextMessageAsync(chatId, "❌ Команду /corrections треба писати У ВІДПОВІДЬ (Reply) на повідомлення з правками.", messageThreadId: threadId, cancellationToken: token);
            return;
        }

        string replyText = message.ReplyToMessage.Text ?? message.ReplyToMessage.Caption;
        if (string.IsNullOrEmpty(replyText)) return;

        // Витягуємо назву та серію з самої команди (як у /cast)
        var match = Regex.Match(text, @"^/corrections(?:@[^\s]+)?\s+""([^""]+)""\s+(.+)$");
        if (!match.Success)
        {
            await botClient.SendTextMessageAsync(chatId, "❌ Неправильний формат! Приклад: /corrections \"Сайкі 1 сезон\" 5", messageThreadId: threadId, cancellationToken: token);
            return;
        }

        string sheetName = match.Groups[1].Value;
        string episode = match.Groups[2].Value;

        var lines = replyText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var corrections = new Dictionary<string, List<string>>();
        string currentTag = null;

        foreach (var line in lines)
        {
            string cleanLine = line.Trim();
            
            if (cleanLine.StartsWith("@"))
            {
                // Очищаємо тег (якщо після нього є пробіли)
                currentTag = cleanLine.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
                if (!corrections.ContainsKey(currentTag)) 
                    corrections[currentTag] = new List<string>();
            }
            else if (!string.IsNullOrEmpty(currentTag))
            {
                // Розбиваємо рядок по першому пробілу. 
                // Наприклад: "04:33 наголос, повинно бути..." -> беремо тільки "04:33"
                var words = cleanLine.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                if (words.Length > 0)
                {
                    string timeCode = words[0]; 
                    corrections[currentTag].Add(timeCode);
                }
            }
        }

        if (corrections.Count == 0)
        {
            await botClient.SendTextMessageAsync(chatId, "❌ Не знайдено тегів акторів (@тег) у повідомленні.", messageThreadId: threadId, cancellationToken: token);
            return;
        }

        var waitMsg = await botClient.SendTextMessageAsync(chatId, $"⏳ Записую правки для {corrections.Count} акторів...", messageThreadId: threadId, cancellationToken: token);

        try
        {
            await _sheetsService.UpdateCorrectionsAsync(sheetName, episode, corrections);
            await botClient.EditMessageTextAsync(chatId, waitMsg.MessageId, "✅ Правки додано!", cancellationToken: token);
        }
        catch (Exception ex)
        {
            await botClient.EditMessageTextAsync(chatId, waitMsg.MessageId, $"❌ Помилка: {ex.Message}", cancellationToken: token);
        }
    }
}