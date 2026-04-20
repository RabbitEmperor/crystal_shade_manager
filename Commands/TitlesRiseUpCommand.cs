using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using crystal_shade_manager.Services;
using crystal_shade_manager.Interfaces;
using System.Collections.Generic;

namespace crystal_shade_manager.Commands;

public class TitlesRiseUpCommand : ITelegramCommand 
{
    private readonly IGoogleSheetsService _sheetsService;

    public string Trigger => "/titles_riseup"; 

    public TitlesRiseUpCommand(IGoogleSheetsService sheetsService)
    {
        _sheetsService = sheetsService;
    }

    public async Task ExecuteAsync(ITelegramBotClient botClient, Message message, CancellationToken ct)
    {
        var processingMessage = await botClient.SendTextMessageAsync(
            message.Chat.Id, 
            "🔄 Отримую список поточних тайтлів...", 
            cancellationToken: ct);

        var allTasks = await _sheetsService.GetTitlesTasksAsync();

        if (allTasks.Count == 0)
        {
            await botClient.EditMessageTextAsync(message.Chat.Id, processingMessage.MessageId, "✅ Наразі немає активних завдань.", cancellationToken: ct);
            return;
        }

        var uniqueTitles = allTasks.Select(t => t.TitleName).Distinct().ToList();
        var buttons = new List<InlineKeyboardButton[]>();
        
        foreach (var title in uniqueTitles)
        {
            string safeTitle = title.Length > 40 ? title.Substring(0, 40) : title;
            buttons.Add(new[] { InlineKeyboardButton.WithCallbackData($"🎬 {title}", $"tru|{safeTitle}") });
        }

        buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("📢 Надіслати по всім", "tru_all") });

        var keyboard = new InlineKeyboardMarkup(buttons);

        await botClient.EditMessageTextAsync(
            message.Chat.Id,
            processingMessage.MessageId,
            "👇 Оберіть тайтл для формування Rise Up розсилки:",
            replyMarkup: keyboard,
            cancellationToken: ct);
    }
}