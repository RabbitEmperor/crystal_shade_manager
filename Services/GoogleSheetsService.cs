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
    private readonly SheetsService _service;

    public GoogleSheetsService()
    {
        string credentialsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Config.CredentialsFile);

        if (!File.Exists(credentialsPath))
            throw new FileNotFoundException($"Файл {credentialsPath} не знайдено!");

        GoogleCredential credential;
        using (var stream = new FileStream(credentialsPath, FileMode.Open, FileAccess.Read))
        {
            credential = GoogleCredential.FromStream(stream).CreateScoped(SheetsService.Scope.Spreadsheets);
        }

        _service = new SheetsService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "Crystal Manager",
            HttpClientFactory = new Google.Apis.Http.HttpClientFactory()
        });
    }

    private async Task<Dictionary<string, string>> GetTeamDictionaryAsync(bool tagToNick)
    {
        var dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        
        try
        {
            // ВІДНОВЛЕНО: Назва аркуша "Команда"
            var teamData = await _service.Spreadsheets.Values.Get(Config.SheetId, "'Команда'!A2:C").ExecuteAsync();
            if (teamData.Values == null) return dictionary;

            foreach (var row in teamData.Values)
            {
                if (row.Count == 0 || row[0] == null) continue;

                var nick = row[0].ToString()?.Trim();
                var tag = row.Count > 2 ? row[2]?.ToString()?.Trim() : "";
                
                if (string.IsNullOrEmpty(nick)) continue;

                string cleanTag = tag;
                if (cleanTag?.StartsWith("@") == true) cleanTag = cleanTag.Substring(1);

                if (tagToNick)
                {
                    if (!string.IsNullOrEmpty(cleanTag) && cleanTag != "-") 
                        dictionary[cleanTag] = nick;
                }
                else
                {
                    dictionary[nick] = (!string.IsNullOrEmpty(cleanTag) && cleanTag != "-") ? "@" + cleanTag : nick;
                }
            }
        }
        catch { /* Ігноруємо помилки */ }

        return dictionary;
    }

   public async Task<Dictionary<string, List<string>>> GetUserTaskMessagesAsync()
    {
        var userTags = await GetTeamDictionaryAsync(tagToNick: false);
        var userTasksGrouped = new Dictionary<string, Dictionary<string, HashSet<string>>>();
        
        var spreadsheet = await _service.Spreadsheets.Get(Config.SheetId).ExecuteAsync();
        var validSheets = spreadsheet.Sheets
            .Select(s => s.Properties.Title)
            // ВІДНОВЛЕНО: Назви аркушів
            .Where(t => t != "Команда" && !t.StartsWith("Статистика") && t != "Логи")
            .ToList();

        if (validSheets.Count == 0) return null;

        var ranges = validSheets.Select(t => $"'{t}'!A2:G").ToList();
        var batchRequest = _service.Spreadsheets.Values.BatchGet(Config.SheetId);
        batchRequest.Ranges = ranges;
        var batchResponse = await batchRequest.ExecuteAsync();

        for (int i = 0; i < validSheets.Count; i++)
        {
            var title = validSheets[i];
            var sheetData = batchResponse.ValueRanges[i].Values;

            if (sheetData == null) continue;

            foreach (var row in sheetData)
            {
                if (row.Count <= 5) continue; 

                var status = row[5].ToString()?.Trim().ToLower(); 
                var participant = row[3].ToString()?.Trim();     

                // ВІДНОВЛЕНО: Статуси
                bool isActiveStatus = status == "виконується" || status == "готово" || status == "true";

                if (string.IsNullOrEmpty(participant) || !isActiveStatus || !userTags.ContainsKey(participant)) 
                    continue; 

                var taskName = row.Count > 1 ? row[1].ToString()?.Trim() : "Завдання"; 
                var episode = row.Count > 0 ? row[0].ToString()?.Trim() : "";          

                if (taskName?.ToUpper() == "TRUE") taskName = "Основна роль";

                string safeTaskName = taskName.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
                string statusIcon = status == "готово" ? "✅" : "⏳";
                string projectKey = $"{statusIcon} <b>{title}</b> (Еп. {episode})";

                if (!userTasksGrouped.ContainsKey(participant))
                    userTasksGrouped[participant] = new Dictionary<string, HashSet<string>>();
                
                if (!userTasksGrouped[participant].ContainsKey(projectKey))
                    userTasksGrouped[participant][projectKey] = new HashSet<string>();

                userTasksGrouped[participant][projectKey].Add(safeTaskName);
            }
        }

        if (userTasksGrouped.Count == 0) return null;

        return FormatUserMessages(userTasksGrouped, userTags);
    }

    private Dictionary<string, List<string>> FormatUserMessages(
        Dictionary<string, Dictionary<string, HashSet<string>>> userTasksGrouped, 
        Dictionary<string, string> userTags)
    {
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
            sb.AppendLine($"👤 {safeTag}, ваші поточні завдання:");

            foreach (var projKvp in projects)
            {
                string taskLine = $"{projKvp.Key}: {string.Join(", ", projKvp.Value)}";

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
        var values = new List<object> { DateTime.Now.ToString("dd.MM.yyyy HH:mm"), userName, action };
        var valueRange = new ValueRange { Values = new List<IList<object>> { values } };

        // ВІДНОВЛЕНО: Назва аркуша "Логи"
        var appendRequest = _service.Spreadsheets.Values.Append(valueRange, Config.SheetId, "'Логи'!A:C");
        appendRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;

        await appendRequest.ExecuteAsync();
    }

    public async Task AddCastListAsync(string sheetName, string episode, List<(string Character, string Actor)> cast, string deadline)
    {
        var tagToNick = await GetTeamDictionaryAsync(tagToNick: true);
        var rows = new List<IList<object>>();

        rows.Add(new List<object> { episode });

        foreach (var member in cast)
        {
            // 1. Жорстко відрізаємо все, що йде після пробілу або відкритої дужки.
            // Тобто з "@oBogeMiy (1 репліка)" ми робимо просто "oBogeMiy"
            string cleanTag = member.Actor.Split(new[] { ' ', '(', '\u00A0' }, StringSplitOptions.RemoveEmptyEntries)[0].Replace("@", "").Trim();

            // 2. Шукаємо псевдонім у нашому словнику. 
            // Якщо знайшли oBogeMiy -> беремо "Чагарник". Якщо не знайшли -> пишемо "@oBogeMiy"
            string finalActor = tagToNick.ContainsKey(cleanTag) ? tagToNick[cleanTag] : "@" + cleanTag;

            // 3. Записуємо в таблицю
            rows.Add(new List<object> 
            { 
                episode,             
                "Дабер",             
                member.Character,    
                finalActor,          // Тут тепер буде красивий псевдонім!
                deadline,            
                "Виконується",       
                ""                   
            });
        }

        var valueRange = new ValueRange { Values = rows };
        var appendRequest = _service.Spreadsheets.Values.Append(valueRange, Config.SheetId, $"'{sheetName}'!A:G");
        appendRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;

        var appendResponse = await appendRequest.ExecuteAsync();

        if (appendResponse.Updates != null && !string.IsNullOrEmpty(appendResponse.Updates.UpdatedRange))
        {
            string updatedRange = appendResponse.Updates.UpdatedRange; 

            var match = System.Text.RegularExpressions.Regex.Match(updatedRange, @"![A-Z]+(\d+)");
            if (match.Success)
            {
                int startRow = int.Parse(match.Groups[1].Value);

                var spreadsheet = await _service.Spreadsheets.Get(Config.SheetId).ExecuteAsync();
                var targetSheet = spreadsheet.Sheets.FirstOrDefault(s => s.Properties.Title == sheetName);

                if (targetSheet != null)
                {
                    int tabId = targetSheet.Properties.SheetId.Value;

                    var gridRange = new GridRange
                    {
                        SheetId = tabId,
                        StartRowIndex = startRow - 1, 
                        EndRowIndex = startRow,       
                        StartColumnIndex = 0,         
                        EndColumnIndex = 7            
                    };

                    var batchRequests = new List<Request>
                    {
                        new Request { MergeCells = new MergeCellsRequest { Range = gridRange, MergeType = "MERGE_ALL" } },
                        
                        new Request
                        {
                            RepeatCell = new RepeatCellRequest
                            {
                                Range = gridRange,
                                Cell = new CellData
                                {
                                    UserEnteredFormat = new CellFormat
                                    {
                                        BackgroundColor = new Color { Red = 0, Green = 0, Blue = 0 }, 
                                        HorizontalAlignment = "CENTER", 
                                        TextFormat = new TextFormat
                                        {
                                            ForegroundColor = new Color { Red = 1, Green = 1, Blue = 1 }, 
                                            Bold = true 
                                        }
                                    }
                                },
                                Fields = "userEnteredFormat(backgroundColor,horizontalAlignment,textFormat)"
                            }
                        }
                    };

                    var batchUpdateRequest = new BatchUpdateSpreadsheetRequest { Requests = batchRequests };
                    await _service.Spreadsheets.BatchUpdate(batchUpdateRequest, Config.SheetId).ExecuteAsync();
                }
            }
        }
    }

    public async Task UpdateCorrectionsAsync(string sheetName, string episode, Dictionary<string, List<string>> corrections)
    {
        var tagToNick = await GetTeamDictionaryAsync(tagToNick: true);
        var correctionsByNick = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in corrections)
        {
            string tag = kvp.Key.Replace("@", "").Trim();
            string nick = tagToNick.ContainsKey(tag) ? tagToNick[tag] : tag;
            
            correctionsByNick[nick] = string.Join(", ", kvp.Value); 
        }

        var readRequest = _service.Spreadsheets.Values.Get(Config.SheetId, $"'{sheetName}'!A:G");
        var response = await readRequest.ExecuteAsync();
        var rows = response.Values;

        if (rows == null || rows.Count == 0) 
            throw new Exception("Дані таблиці пусті або не зчитані.");

        var dataToUpdate = new List<ValueRange>();

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Count < 4) continue; 

            var rowEp = row[0]?.ToString()?.Trim();
            var rowActorNick = row[3]?.ToString()?.Trim();

            if (rowEp != episode || string.IsNullOrEmpty(rowActorNick) || !correctionsByNick.ContainsKey(rowActorNick)) 
                continue; 

            int sheetRowIndex = i + 1; 
            string currentNotes = row.Count > 6 ? row[6]?.ToString()?.Trim() : "";
            string newNotes = correctionsByNick[rowActorNick];

            if (!string.IsNullOrEmpty(currentNotes))
            {
                newNotes = currentNotes + "\n---\n" + newNotes;
            }

            dataToUpdate.Add(new ValueRange
            {
                Range = $"'{sheetName}'!F{sheetRowIndex}:G{sheetRowIndex}",
                Values = new List<IList<object>> { new List<object> { "Правки", newNotes } }
            });
        }

        if (dataToUpdate.Count == 0)
            throw new Exception($"Не знайдено акторів у списку правок в епізоді {episode}.");

        var batchUpdateRequest = new BatchUpdateValuesRequest
        {
            ValueInputOption = "USER_ENTERED", 
            Data = dataToUpdate
        };

        var request = _service.Spreadsheets.Values.BatchUpdate(batchUpdateRequest, Config.SheetId);
        await request.ExecuteAsync();
    }

    public async Task<List<crystal_shade_manager.Models.TitleTask>> GetTitlesTasksAsync()
    {
        var tasks = new List<crystal_shade_manager.Models.TitleTask>();
        try
        {
            // ВІДНОВЛЕНО: Назва аркуша "Тайтли"
            var request = _service.Spreadsheets.Values.Get(Config.SheetId, "'Тайтли'!A2:C");
            var response = await request.ExecuteAsync();
            var values = response.Values;

            if (values == null || values.Count == 0) return tasks;

            foreach (var row in values)
            {
                if (row.Count < 3) continue;
                var titleName = row[0]?.ToString()?.Trim();
                var detailsText = row[2]?.ToString()?.Trim();
                if (string.IsNullOrEmpty(titleName) || string.IsNullOrEmpty(detailsText)) continue;

                var lines = detailsText.Split(new[] { '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
                string currentEpisode = "Невідомий епізод";

                foreach (var line in lines)
                {
                    var cleanLine = line.Trim();
                    if (string.IsNullOrEmpty(cleanLine)) continue;
                    // ВІДНОВЛЕНО: Пошук по слову "Серія:"
                    if (cleanLine.EndsWith("Серія:"))
                    {
                        currentEpisode = cleanLine.Replace(":", "").Trim();
                        continue;
                    }

                    var match = System.Text.RegularExpressions.Regex.Match(cleanLine, @"^(.+?)\s+<(.+?)>$");
                    if (match.Success)
                    {
                        tasks.Add(new crystal_shade_manager.Models.TitleTask
                        {
                            TitleName = titleName,
                            Episode = currentEpisode,
                            Username = match.Groups[1].Value.Trim(),
                            Role = match.Groups[2].Value.Trim()
                        });
                    }
                }
            }
        }
        catch (System.Exception ex) { System.Console.WriteLine($"Помилка 'Тайтли': {ex.Message}"); }
        return tasks;
    }

    public async Task<Dictionary<string, string>> GetTeamTagsAsync()
    {
        var tagsDict = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
        try
        {
            // ВІДНОВЛЕНО: Назва аркуша "Команда"
            var request = _service.Spreadsheets.Values.Get(Config.SheetId, "'Команда'!A2:C");
            var response = await request.ExecuteAsync();
            var values = response.Values;

            if (values != null)
            {
                foreach (var row in values)
                {
                    if (row.Count >= 3)
                    {
                        var pseudonym = row[0]?.ToString()?.Trim();
                        var tgTag = row[2]?.ToString()?.Trim();

                        if (!string.IsNullOrEmpty(pseudonym) && !string.IsNullOrEmpty(tgTag))
                        {
                            if (tgTag == "-")
                            {
                                tagsDict[pseudonym] = "-";
                                continue;
                            }

                            string properTag = tgTag.StartsWith("@") ? tgTag : "@" + tgTag;
                            string tagWithoutAt = properTag.Substring(1);

                            tagsDict[pseudonym] = properTag;
                            tagsDict[tagWithoutAt] = properTag;
                            tagsDict[properTag] = properTag;
                        }
                    }
                }
            }
        }
        catch (System.Exception ex) { System.Console.WriteLine($"Помилка 'Команда': {ex.Message}"); }
        return tagsDict;
    }
}