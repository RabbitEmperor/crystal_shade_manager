using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using crystal_shade_manager.Interfaces;
using crystal_shade_manager.Services;
using File = System.IO.File;

namespace crystal_shade_manager.Commands;

public class DebtsCommand : ITelegramCommand
{
    private readonly IGoogleSheetsService _sheetsService;
    private const string JokesFileName = "user_jokes.json";

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
            messageThreadId: message.MessageThreadId, 
            text: "⏳ <b>Перевіряю всі тайтли...</b>",
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

        // 3. Якщо боргів немає — витягуємо персональний прикол з JSON
        if (debts == null || !debts.Any())
        {
            string customMessage = "🎉 " + userTag + ", <b>у тебе немає боргів! Ти просто котик!</b>";
            
            try
            {
                // Піднімаємося до кореня проєкту
                string projectRoot = AppDomain.CurrentDomain.BaseDirectory;
                while (!File.Exists(Path.Combine(projectRoot, "Program.cs")) && Directory.GetParent(projectRoot) != null)
                {
                    projectRoot = Directory.GetParent(projectRoot).FullName;
                }

                string jokesPath = Path.Combine(projectRoot, JokesFileName);
                if (File.Exists(jokesPath))
                {
                    string jsonString = await File.ReadAllTextAsync(jokesPath, ct);
                    
                    // Десеріалізуємо у тимчасовий словник
                    var rawDict = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonString);
                    
                    if (rawDict != null)
                    {
                        // Переливаємо дані у словник, який ЗАЛІЗОБЕТОННО ігнорує регістр символів
                        var jokesDict = new Dictionary<string, string>(rawDict, StringComparer.OrdinalIgnoreCase);

                        string cleanUsername = username.Trim();
                        
                        // Тепер пошук відпрацює ідеально, незалежно від великих/малих літер
                        if (jokesDict.TryGetValue(cleanUsername, out string jokeText))
                        {
                            if (!string.IsNullOrWhiteSpace(jokeText))
                            {
                                customMessage = jokeText;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Помилка зчитування файлу приколів user_jokes.json: {ex.Message}");
            }

            await botClient.EditMessageTextAsync(message.Chat.Id, loadingMsg.MessageId, customMessage, parseMode: ParseMode.Html, cancellationToken: ct);
            return;
        }

        // 4. Формуємо список з гілочками (якщо борги є)
        var sb = new StringBuilder();
        
        sb.AppendLine("📋 <b>Борги: " + userNickname + "</b>\n");

        var groupedByTitle = debts.GroupBy(d => d.TitleName);
        foreach (var title in groupedByTitle)
        {
            sb.AppendLine("🎬 <b>" + title.Key + "</b>");
            
            var groupedByEp = title.GroupBy(d => d.Episode);
            foreach (var ep in groupedByEp)
            {
                sb.AppendLine(" 📺 Епізод " + ep.Key + ":");
                
                var tasksList = ep.ToList();
                for (int i = 0; i < tasksList.Count; i++)
                {
                    var task = tasksList[i];
                    string deadlineText = string.IsNullOrEmpty(task.Deadline) ? "без дедлайну" : "⏰ " + task.Deadline;
                    
                    string statusAlert = "";
                    if (task.Status != null && task.Status.Equals("правки", StringComparison.OrdinalIgnoreCase))
                    {
                        statusAlert = "❗️<b>[ПРАВКИ]</b> "; 
                    }

                    string charText = string.IsNullOrWhiteSpace(task.Character) ? "" : "<b>" + task.Character + "</b> ";
                    string branch = (i == tasksList.Count - 1) ? "    └ " : "    ├ ";

                    string taskStr = branch + statusAlert + charText + "<i>(" + task.Role + ")</i> — " + deadlineText;
                    sb.AppendLine(taskStr);
                }
            }
            sb.AppendLine(); 
        }

        await botClient.EditMessageTextAsync(message.Chat.Id, loadingMsg.MessageId, sb.ToString(), parseMode: ParseMode.Html, cancellationToken: ct);
    }
}