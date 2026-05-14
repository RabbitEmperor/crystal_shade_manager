using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using crystal_shade_manager.Interfaces;
using crystal_shade_manager.Services;

namespace crystal_shade_manager.Commands;

public class DebtsCommand : ITelegramCommand
{
    private readonly IGoogleSheetsService _sheetsService;

    public DebtsCommand(IGoogleSheetsService sheetsService)
    {
        _sheetsService = sheetsService;
    }

    public string Trigger => "/my_task"; 

    public async Task ExecuteAsync(ITelegramBotClient botClient, Message message, CancellationToken ct)
    {
        string username = message.From?.Username;
        if (string.IsNullOrEmpty(username))
        {
            await botClient.SendTextMessageAsync(
                chatId: message.Chat.Id,
                messageThreadId: message.MessageThreadId,
                text: "❌ У тебе не встановлено юзернейм (@) в налаштуваннях Telegram. Я не можу тебе ідентифікувати.",
                cancellationToken: ct
            );
            return;
        }

        string userTag = "@" + username;
        var loadingMsg = await botClient.SendTextMessageAsync(
            chatId: message.Chat.Id,
            messageThreadId: message.MessageThreadId, // <-- Магія для гілок Форуму!
            text: "⏳ <b>Перевіряю всі тайтли на наявність боргів...</b>",
            parseMode: ParseMode.Html,
            cancellationToken: ct
        );

        // 1. Отримуємо псевдонім зі словника команди
        var teamTags = await _sheetsService.GetTeamTagsAsync();
        string userNickname = userTag;
        
        if (teamTags != null)
        {
            var match = teamTags.FirstOrDefault(x => x.Value?.Equals(userTag, StringComparison.OrdinalIgnoreCase) == true);
            if (!string.IsNullOrEmpty(match.Key))
            {
                 userNickname = match.Key; // Отримали псевдонім
            }
        }

        // 2. Шукаємо борги
        var debts = await _sheetsService.GetUserDebtsAsync(userNickname, userTag);

        // 3. Якщо боргів немає
        if (debts == null || !debts.Any())
        {
            string noDebtsMsg = "🎉 " + userTag + ", <b>у тебе немає боргів! Ти просто котик!</b>";
            await botClient.EditMessageTextAsync(message.Chat.Id, loadingMsg.MessageId, noDebtsMsg, parseMode: ParseMode.Html, cancellationToken: ct);
            return;
        }

        // 4. Формуємо красивий список
        var sb = new StringBuilder();
        
        // Використовуємо класичне зшивання рядків (+), щоб компілятор не сходив з розуму
        string header = "📋 <b>Борги для " + userNickname + "</b>:\n";
        sb.AppendLine(header);

        var groupedByTitle = debts.GroupBy(d => d.TitleName);
        foreach (var title in groupedByTitle)
        {
            string titleStr = "🎬 <b>" + title.Key + "</b>";
            sb.AppendLine(titleStr);
            
            var groupedByEp = title.GroupBy(d => d.Episode);
            foreach (var ep in groupedByEp)
            {
                string epStr = " 📺 " + ep.Key + ":";
                sb.AppendLine(epStr);
                
                foreach (var task in ep)
                {
                    string deadlineText = string.IsNullOrEmpty(task.Deadline) ? "без дедлайну" : "⏰ " + task.Deadline;
                    string taskStr = "  ├ 👤 " + task.Character + " <i>(" + task.Role + ")</i> — " + deadlineText;
                    sb.AppendLine(taskStr);
                }
            }
            sb.AppendLine(); // Пустий рядок між тайтлами
        }

        await botClient.EditMessageTextAsync(message.Chat.Id, loadingMsg.MessageId, sb.ToString(), parseMode: ParseMode.Html, cancellationToken: ct);
    }
}