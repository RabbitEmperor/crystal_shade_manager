using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;

namespace crystal_shade_manager.Services;

public class GoogleSheetsService : IGoogleSheetsService
{
    public async Task<Dictionary<string, List<string>>> GetUserTaskMessagesAsync()
    {
        string credentialsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Config.CredentialsFile);

        if (!File.Exists(credentialsPath))
            throw new FileNotFoundException($"Файл {credentialsPath} не знайдено!");  

        GoogleCredential credential;
        using (var stream = new FileStream(credentialsPath, FileMode.Open, FileAccess.Read))
        {
            credential = GoogleCredential.FromStream(stream).CreateScoped(SheetsService.Scope.SpreadsheetsReadonly);
        }

        var service = new SheetsService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "Crystal Manager",
            HttpClientFactory = new Google.Apis.Http.HttpClientFactory()
        });

        var userTags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try 
        {
            var teamData = await service.Spreadsheets.Values.Get(Config.SheetId, "'Команда'!A2:C").ExecuteAsync();
            if (teamData.Values != null)
            {
                foreach (var row in teamData.Values)
                {
                    if (row.Count > 0 && row[0] != null)
                    {
                        var nick = row[0].ToString()?.Trim();
                        var tag = row.Count > 2 ? row[2].ToString()?.Trim() : "";
                        
                        if (!string.IsNullOrEmpty(nick))
                        {
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
                        
                        bool isActiveStatus = status == "виконується" || status == "правки" || status == "true";

                        if (!string.IsNullOrEmpty(participant) && isActiveStatus)
                        {
                            var taskName = row.Count > 1 ? row[1].ToString()?.Trim() : "Завдання"; 
                            var episode = row.Count > 0 ? row[0].ToString()?.Trim() : "";          

                            if (taskName?.ToUpper() == "TRUE") taskName = "Виконати завдання";

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
            
            string tag = userTags.ContainsKey(rawNick) ? userTags[rawNick] : rawNick;
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

    public async Task AddLogEntryAsync(string userName, string action)
    {
        string basePath = AppDomain.CurrentDomain.BaseDirectory;
        string fullPath = Path.Combine(basePath, Config.CredentialsFile);

        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Файл {fullPath} не знайдено!");

        GoogleCredential credential;
        using (var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read))
        {
            credential = GoogleCredential.FromStream(stream).CreateScoped(SheetsService.Scope.Spreadsheets);
        }

        var service = new SheetsService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "Crystal Manager"
        });

        var values = new List<object> { DateTime.Now.ToString("dd.MM.yyyy HH:mm"), userName, action };
        var valueRange = new ValueRange { Values = new List<IList<object>> { values } };

        string range = "'Логи'!A:C"; 
        var appendRequest = service.Spreadsheets.Values.Append(valueRange, Config.SheetId, range);
        appendRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;

        await appendRequest.ExecuteAsync();
    }

    // ТУТ МЕТОД ДЛЯ ЗАПИСУ КАСТУ
public async Task AddCastListAsync(string sheetName, string episode, List<(string Character, string Actor)> cast, string deadline)
    {
        string credentialsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Config.CredentialsFile);
        GoogleCredential credential;
        using (var stream = new FileStream(credentialsPath, FileMode.Open, FileAccess.Read))
        {
            credential = GoogleCredential.FromStream(stream).CreateScoped(SheetsService.Scope.Spreadsheets);
        }

        var service = new SheetsService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "Crystal Manager"
        });

        // 1. Отримуємо список команди, щоб створити "перекладач" з тегів на псевдоніми
        var tagToNick = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try 
        {
            var teamData = await service.Spreadsheets.Values.Get(Config.SheetId, "'Команда'!A2:C").ExecuteAsync();
            if (teamData.Values != null)
            {
                foreach (var r in teamData.Values)
                {
                    if (r.Count > 0 && r[0] != null)
                    {
                        var nick = r[0].ToString().Trim();
                        var tag = r.Count > 2 && r[2] != null ? r[2].ToString().Trim() : "";
                        
                        // Відкидаємо @ для зручності пошуку
                        if (tag.StartsWith("@")) tag = tag.Substring(1);

                        // Якщо є і нік, і тег - зберігаємо у словник
                        if (!string.IsNullOrEmpty(nick) && !string.IsNullOrEmpty(tag) && tag != "-")
                        {
                            tagToNick[tag] = nick; // Наприклад: tagToNick["krunuca"] = "Щирий"
                        }
                    }
                }
            }
        }
        catch { }

        // 2. Формуємо рядки для запису
        var rows = new List<IList<object>>();

        // Рядок-розділювач (серія по центру)
        rows.Add(new List<object> { "", "", "", episode, "", "", "" });

        foreach (var member in cast)
        {
            // Очищаємо тег актора від @, якщо він там випадково є
            string cleanTag = member.Actor.Trim();
            if (cleanTag.StartsWith("@")) cleanTag = cleanTag.Substring(1);

            // МАГІЯ ТУТ: Шукаємо тег у словнику. Якщо знайшли - беремо Псевдонім. Якщо ні - залишаємо тег як є.
            string finalActor = tagToNick.ContainsKey(cleanTag) ? tagToNick[cleanTag] : member.Actor;

            rows.Add(new List<object> 
            { 
                episode,             
                "Дабер",             
                member.Character,    
                finalActor,          // Записуємо Псевдонім (наприклад, "Щирий")
                deadline,            
                "Виконується",       
                ""                   
            });
        }

        var valueRange = new ValueRange { Values = rows };
        var appendRequest = service.Spreadsheets.Values.Append(valueRange, Config.SheetId, $"'{sheetName}'!A:G");
        appendRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;

        await appendRequest.ExecuteAsync();
    }
public async Task UpdateCorrectionsAsync(string sheetName, string episode, Dictionary<string, List<string>> corrections)
{
    string credentialsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Config.CredentialsFile);
    GoogleCredential credential;
    using (var stream = new FileStream(credentialsPath, FileMode.Open, FileAccess.Read))
    {
        credential = GoogleCredential.FromStream(stream).CreateScoped(SheetsService.Scope.Spreadsheets);
    }

    var service = new SheetsService(new BaseClientService.Initializer
    {
        HttpClientInitializer = credential,
        ApplicationName = "Crystal Manager"
    });

    // 1. Створюємо "перекладач" з тегів на псевдоніми (так само, як для касту)
    var tagToNick = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    try 
    {
        var teamData = await service.Spreadsheets.Values.Get(Config.SheetId, "'Команда'!A2:C").ExecuteAsync();
        if (teamData.Values != null)
        {
            foreach (var r in teamData.Values)
            {
                if (r.Count > 0 && r[0] != null)
                {
                    var nick = r[0].ToString().Trim();
                    var tag = r.Count > 2 && r[2] != null ? r[2].ToString().Trim() : "";
                    if (tag.StartsWith("@")) tag = tag.Substring(1);

                    if (!string.IsNullOrEmpty(nick) && !string.IsNullOrEmpty(tag) && tag != "-")
                        tagToNick[tag] = nick; 
                }
            }
        }
    }
    catch { }

    // Переводимо передані теги у Псевдоніми, об'єднуючи таймкоди в один текст
    var correctionsByNick = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var kvp in corrections)
    {
        string tag = kvp.Key.Replace("@", "").Trim();
        string nick = tagToNick.ContainsKey(tag) ? tagToNick[tag] : tag; // Якщо не знайшли, лишаємо як є
        correctionsByNick[nick] = string.Join("\n", kvp.Value);
    }

    // 2. Читаємо аркуш, щоб знайти номери рядків потрібних акторів
    var readRequest = service.Spreadsheets.Values.Get(Config.SheetId, $"'{sheetName}'!A:G");
    var response = await readRequest.ExecuteAsync();
    var rows = response.Values;

    if (rows == null || rows.Count == 0) throw new Exception("Аркуш порожній або не існує.");

    var dataToUpdate = new List<ValueRange>();

    for (int i = 0; i < rows.Count; i++)
    {
        var row = rows[i];
        if (row.Count < 4) continue; // Пропускаємо пусті рядки

        var rowEp = row[0]?.ToString()?.Trim();
        var rowActorNick = row[3]?.ToString()?.Trim();

        // Якщо це потрібна серія і актор є у списку правок
        if (rowEp == episode && !string.IsNullOrEmpty(rowActorNick) && correctionsByNick.ContainsKey(rowActorNick))
        {
            int sheetRowIndex = i + 1; // Google Таблиці починаються з 1

            string currentNotes = row.Count > 6 ? row[6]?.ToString()?.Trim() : "";
            string newNotes = correctionsByNick[rowActorNick];

            // Якщо примітки вже були, додаємо нові з нового рядка
            if (!string.IsNullOrEmpty(currentNotes))
            {
                newNotes = currentNotes + "\n---\n" + newNotes;
            }

            // Готуємо оновлення для колонок F (Статус) та G (Примітки)
            var valueRange = new ValueRange
            {
                Range = $"'{sheetName}'!F{sheetRowIndex}:G{sheetRowIndex}",
                Values = new List<IList<object>> { new List<object> { "Правки", newNotes } }
            };
            dataToUpdate.Add(valueRange);
        }
    }


// 3. Відправляємо пакетне оновлення
    if (dataToUpdate.Count > 0)
    {
        var batchUpdateRequest = new BatchUpdateValuesRequest
        {
            ValueInputOption = "USER_ENTERED", // ТЕПЕР ПРАВИЛЬНО
            Data = dataToUpdate
        };

        var request = service.Spreadsheets.Values.BatchUpdate(batchUpdateRequest, Config.SheetId);
        await request.ExecuteAsync();
    }
    else
    {
        throw new Exception($"Не знайдено акторів з такими тегами у серії {episode}.");
    }
}
}