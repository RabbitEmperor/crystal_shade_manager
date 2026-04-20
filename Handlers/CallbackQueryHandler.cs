using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using crystal_shade_manager.Interfaces;
using crystal_shade_manager.Services;

namespace crystal_shade_manager.Handlers;

public class CallbackQueryHandler
{
    private readonly IStateManager _stateManager;
    private readonly IGoogleSheetsService _sheetsService; 

    public CallbackQueryHandler(IStateManager stateManager, IGoogleSheetsService sheetsService = null)
    {
        _stateManager = stateManager;
        _sheetsService = sheetsService;
    }

    public async Task HandleAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, CancellationToken ct)
    {
        if (callbackQuery?.Data == null) return;

        if (callbackQuery.Data == "set_toggle_select")
        {
            var settings = _stateManager.GetSettings(callbackQuery.Message.Chat.Id);
            if (settings != null)
            {
                settings.SelectAllByDefault = !settings.SelectAllByDefault;
                _stateManager.SaveSettings();
            }
            return;
        }

        if (callbackQuery.Data.StartsWith("tru|") || callbackQuery.Data == "tru_all")
        {
            await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "Формую звіт...", cancellationToken: ct);
            await botClient.EditMessageTextAsync(callbackQuery.Message.Chat.Id, callbackQuery.Message.MessageId, "⏳ Збираю дані з таблиці...", cancellationToken: ct);

            string targetTitle = callbackQuery.Data == "tru_all" ? "ALL" : callbackQuery.Data.Split('|')[1];

            var allTasks = await _sheetsService.GetTitlesTasksAsync();
            var teamTags = await _sheetsService.GetTeamTagsAsync();

            var filteredTasks = targetTitle == "ALL" 
                ? allTasks 
                : allTasks.Where(t => t.TitleName.StartsWith(targetTitle)).ToList();

            var groupedTitles = filteredTasks.GroupBy(t => t.TitleName).OrderBy(g => g.Key);
            bool isFirstMessage = true;

            foreach (var titleGroup in groupedTitles)
            {
                var sb = new System.Text.StringBuilder();
                
                // Рятуємося від помилки Ambiguous invocation
                string titleHeader = $"🎬 **{titleGroup.Key}**\n";
                sb.AppendLine(titleHeader);

                var groupedEpisodes = titleGroup.GroupBy(t => t.Episode).OrderBy(g => g.Key);

                foreach (var epGroup in groupedEpisodes)
                {
                    string epHeader = $"📺 {epGroup.Key}:";
                    sb.AppendLine(epHeader);
                    
                    foreach (var task in epGroup)
                    {
                        string userPing;
                        string rawName = task.Username.Trim();

                        if (teamTags.TryGetValue(rawName, out string actualTgTag) && actualTgTag != "-")
                        {
                            userPing = actualTgTag;
                        }
                        else
                        {
                            userPing = rawName.StartsWith("@") ? rawName : "@" + rawName;
                            if (teamTags.ContainsKey(rawName) && teamTags[rawName] == "-") 
                                userPing = rawName;
                        }
                        
                        // Рятуємося від помилки Ambiguous invocation
                        string taskLine = $" ├ 👤 {userPing} — <i>{task.Role}</i>";
                        sb.AppendLine(taskLine);
                    }
                    sb.AppendLine();
                }

                if (isFirstMessage)
                {
                    await botClient.EditMessageTextAsync(callbackQuery.Message.Chat.Id, callbackQuery.Message.MessageId, sb.ToString(), parseMode: Telegram.Bot.Types.Enums.ParseMode.Html, cancellationToken: ct);
                    isFirstMessage = false;
                }
                else
                {
                    await botClient.SendTextMessageAsync(callbackQuery.Message.Chat.Id, sb.ToString(), parseMode: Telegram.Bot.Types.Enums.ParseMode.Html, cancellationToken: ct);
                }
            }
        }
    }
}