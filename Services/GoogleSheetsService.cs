using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;

namespace crystal_shade_manager.Services;

public class GoogleSheetsService : IGoogleSheetsService
{
    public async Task<Dictionary<string, List<string>>> GetUserTaskMessagesAsync()
    {
        if (!System.IO.File.Exists(Config.CredentialsFile))
            throw new System.IO.FileNotFoundException($"Файл {Config.CredentialsFile} не знайдено!");

        GoogleCredential credential;
        using (var stream = new System.IO.FileStream(Config.CredentialsFile, System.IO.FileMode.Open, System.IO.FileAccess.Read))
        {
            credential = GoogleCredential.FromStream(stream).CreateScoped(SheetsService.Scope.SpreadsheetsReadonly);
        }

        var service = new SheetsService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "Crystal Manager"
        });

        // 1. Збираємо офіційний склад команди
        var userTags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try 
        {
            var teamData = await service.Spreadsheets.Values.Get(Config.SheetId, "'Команда'!A2:C").ExecuteAsync();
            if (teamData.Values != null)
            {
                foreach (var row in teamData.Values)
                {
                    // Перевіряємо, чи є хоча б Нік (колонка A)
                    if (row.Count > 0 && row[0] != null)
                    {
                        var nick = row[0].ToString()?.Trim();
                        // Якщо є тег (колонка C), беремо його, якщо ні - пустий рядок
                        var tag = row.Count > 2 ? row[2].ToString()?.Trim() : "";
                        
                        if (!string.IsNullOrEmpty(nick))
                        {
                            // Зберігаємо всіх, хто є у вкладці "Команда"
                            // Якщо тегу немає або стоїть прочерк, використовуємо нік замість тегу
                            if (string.IsNullOrEmpty(tag) || tag == "-")
                                userTags[nick] = nick;
                            else
                                userTags[nick] = tag;
                        }
                    }
                }
            }
        }
        catch { }

        var userTasksGrouped = new Dictionary<string, Dictionary<string, HashSet<string>>>();
        var spreadsheet = await service.Spreadsheets.Get(Config.SheetId).ExecuteAsync();

        var validSheets = spreadsheet.Sheets
            .Select(s => s.Properties.Title)
            .Where(t => t != "Команда" && !t.StartsWith("ЗАМОРОЖЕНО") && t != "Архів")
            .ToList();

        if (validSheets.Count > 0)
        {
            var ranges = validSheets.Select(t => $"'{t}'!A2:G").ToList();
            var batchRequest = service.Spreadsheets.Values.BatchGet(Config.SheetId);
            batchRequest.Ranges = ranges;
            var batchResponse = await batchRequest.ExecuteAsync();

            for (int i = 0; i < validSheets.Count; i++)
            {
                var title = validSheets[i];
                var sheetData = batchResponse.ValueRanges[i].Values;

                if (sheetData == null) continue;

                foreach (var row in sheetData)
                {
                    if (row.Count > 5) 
                    {
                        var status = row[5].ToString()?.Trim().ToLower(); 
                        var participant = row[3].ToString()?.Trim(); 
                        
                        // ГОЛОВНЕ ПРАВИЛО ФІЛЬТРАЦІЇ: userTags.ContainsKey(participant)
                        // Бот перевіряє, чи є цей учасник у нашому списку Команди. Якщо ні - ігнорує.
                        if (!string.IsNullOrEmpty(participant) && userTags.ContainsKey(participant) && (status == "виконується" || status == "правки"))
                        {
                            var taskName = row.Count > 1 ? row[1].ToString() : "Завдання";
                            var episode = row.Count > 0 ? row[0].ToString() : "";

                            string safeTaskName = taskName.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
                            string statusIcon = status == "правки" ? "✏️" : "🔨";
                            string projectKey = $"{statusIcon} <b>{title}</b> (Еп. {episode})";

                            if (!userTasksGrouped.ContainsKey(participant))
                                userTasksGrouped[participant] = new Dictionary<string, HashSet<string>>();
                            
                            if (!userTasksGrouped[participant].ContainsKey(projectKey))
                                userTasksGrouped[participant][projectKey] = new HashSet<string>();

                            userTasksGrouped[participant][projectKey].Add(safeTaskName);
                        }
                    }
                }
            }
        }

        if (userTasksGrouped.Count == 0) return null;

        var resultMessages = new Dictionary<string, List<string>>();

        foreach (var userKvp in userTasksGrouped)
        {
            var rawNick = userKvp.Key; 
            var projects = userKvp.Value;

            string safeNick = rawNick.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
            
            // Оскільки ми вже відфільтрували чужинців, ми точно знаємо, що цей нік є в userTags
            string tag = userTags[rawNick];
            string safeTag = tag.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

            var userMsgList = new List<string>();
            var sb = new StringBuilder();
            sb.AppendLine($"👤 {safeTag}, ваш список рабства:");

            foreach (var projKvp in projects)
            {
                var projectInfo = projKvp.Key;
                var roles = string.Join(", ", projKvp.Value);
                string taskLine = $"{projectInfo}: {roles}";

                if (sb.Length + taskLine.Length > 4000)
                {
                    userMsgList.Add(sb.ToString());
                    sb.Clear();
                    sb.AppendLine($"👤 {safeTag} (продовження списку):");
                }
                sb.AppendLine(taskLine);
            }
            
            userMsgList.Add(sb.ToString());
            resultMessages[safeNick] = userMsgList; 
        }

        return resultMessages;
    }
}