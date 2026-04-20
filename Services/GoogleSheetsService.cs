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

    // 1. Конструктор: Авторизація відбувається лише один раз при створенні сервісу
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

    // 2. Приватний метод: Витягує команду з таблиці, щоб не дублювати цей код 3 рази
    private async Task<Dictionary<string, string>> GetTeamDictionaryAsync(bool tagToNick)
    {
        var dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        
        try
        {
            var teamData = await _service.Spreadsheets.Values.Get(Config.SheetId, "'Команда'!A2:C").ExecuteAsync();
            if (teamData.Values == null) return dictionary;

            foreach (var row in teamData.Values)
            {
                if (row.Count == 0 || row[0] == null) continue; // Guard Clause

                var nick = row[0].ToString()?.Trim();
                var tag = row.Count > 2 ? row[2]?.ToString()?.Trim() : "";
                
                if (string.IsNullOrEmpty(nick)) continue; // Guard Clause

                // Очищаємо тег від @ для зручності обробки
                string cleanTag = tag;
                if (cleanTag?.StartsWith("@") == true) cleanTag = cleanTag.Substring(1);

                if (tagToNick)
                {
                    // Це для команди /cast та /corrections
                    if (!string.IsNullOrEmpty(cleanTag) && cleanTag != "-") 
                        dictionary[cleanTag] = nick;
                }
                else
                {
                    // ОСЬ ТУТ ЗМІНА: Це для розсилки /rise_up. 
                    // Якщо тег є і це не прочерк "-", обов'язково додаємо спереду "@"
                    dictionary[nick] = (!string.IsNullOrEmpty(cleanTag) && cleanTag != "-") ? "@" + cleanTag : nick;
                }
            }
        }
        catch { /* Можна додати логування помилки читання команди */ }

        return dictionary;
    }

   public async Task<Dictionary<string, List<string>>> GetUserTaskMessagesAsync()
    {
        // 1. Отримуємо список АКТУАЛЬНОЇ команди
        var userTags = await GetTeamDictionaryAsync(tagToNick: false);
        var userTasksGrouped = new Dictionary<string, Dictionary<string, HashSet<string>>>();
        
        var spreadsheet = await _service.Spreadsheets.Get(Config.SheetId).ExecuteAsync();
        var validSheets = spreadsheet.Sheets
            .Select(s => s.Properties.Title)
            .Where(t => t != "Команда" && !t.StartsWith("ЗАМОРОЖЕНО") && t != "Архів")
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

                bool isActiveStatus = status == "виконується" || status == "правки" || status == "true";

                // НОВИЙ ФІЛЬТР: Якщо учасника немає в userTags (на вкладці Команда) - пропускаємо його!
                if (string.IsNullOrEmpty(participant) || !isActiveStatus || !userTags.ContainsKey(participant)) 
                    continue; 

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

        if (userTasksGrouped.Count == 0) return null;

        return FormatUserMessages(userTasksGrouped, userTags);
    }

    // Винесено логіку форматування повідомлень з методу отримання даних
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
            sb.AppendLine($"👤 {safeTag}, ваш список рабства:");

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

        var appendRequest = _service.Spreadsheets.Values.Append(valueRange, Config.SheetId, "'Логи'!A:C");
        appendRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;

        await appendRequest.ExecuteAsync();
    }

   public async Task AddCastListAsync(string sheetName, string episode, List<(string Character, string Actor)> cast, string deadline)
    {
        var tagToNick = await GetTeamDictionaryAsync(tagToNick: true);
        var rows = new List<IList<object>>();

        // Рядок-розділювач (поки що просто пишемо туди номер серії)
        rows.Add(new List<object> { episode });

        foreach (var member in cast)
        {
            string cleanTag = member.Actor.Trim();
            if (cleanTag.StartsWith("@")) cleanTag = cleanTag.Substring(1);

            string finalActor = tagToNick.ContainsKey(cleanTag) ? tagToNick[cleanTag] : member.Actor;

            rows.Add(new List<object> 
            { 
                episode,             
                "Дабер",             
                member.Character,    
                finalActor,          
                deadline,            
                "Виконується",       
                ""                   
            });
        }

        var valueRange = new ValueRange { Values = rows };
        var appendRequest = _service.Spreadsheets.Values.Append(valueRange, Config.SheetId, $"'{sheetName}'!A:G");
        appendRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;

        // КРОК 1: Записуємо текст у таблицю і запам'ятовуємо, куди він потрапив
        var appendResponse = await appendRequest.ExecuteAsync();

        if (appendResponse.Updates != null && !string.IsNullOrEmpty(appendResponse.Updates.UpdatedRange))
        {
            string updatedRange = appendResponse.Updates.UpdatedRange; // Виглядає як: "'Аркуш'!A25:G30"

            // Витягуємо номер першого рядка (нашого розділювача) за допомогою Regex
            var match = System.Text.RegularExpressions.Regex.Match(updatedRange, @"![A-Z]+(\d+)");
            if (match.Success)
            {
                int startRow = int.Parse(match.Groups[1].Value);

                // Щоб форматувати, нам потрібен цифровий ID конкретної вкладки
                var spreadsheet = await _service.Spreadsheets.Get(Config.SheetId).ExecuteAsync();
                var targetSheet = spreadsheet.Sheets.FirstOrDefault(s => s.Properties.Title == sheetName);

                if (targetSheet != null)
                {
                    int tabId = targetSheet.Properties.SheetId.Value;

                    // КРОК 2: Готуємо координати для пензлика маляра
                    var gridRange = new GridRange
                    {
                        SheetId = tabId,
                        StartRowIndex = startRow - 1, // В Google API рядки рахуються з нуля (тому -1)
                        EndRowIndex = startRow,       // Захоплюємо тільки один рядок
                        StartColumnIndex = 0,         // Від стовпця A
                        EndColumnIndex = 7            // До стовпця G
                    };

                    var batchRequests = new List<Request>
                    {
                        // А) Об'єднуємо клітинки
                        new Request { MergeCells = new MergeCellsRequest { Range = gridRange, MergeType = "MERGE_ALL" } },
                        
                        // Б) Фарбуємо
                        new Request
                        {
                            RepeatCell = new RepeatCellRequest
                            {
                                Range = gridRange,
                                Cell = new CellData
                                {
                                    UserEnteredFormat = new CellFormat
                                    {
                                        BackgroundColor = new Color { Red = 0, Green = 0, Blue = 0 }, // Чорний фон
                                        HorizontalAlignment = "CENTER", // Текст по центру
                                        TextFormat = new TextFormat
                                        {
                                            ForegroundColor = new Color { Red = 1, Green = 1, Blue = 1 }, // Білий текст
                                            Bold = true // Жирний
                                        }
                                    }
                                },
                                Fields = "userEnteredFormat(backgroundColor,horizontalAlignment,textFormat)"
                            }
                        }
                    };

                    // Відправляємо наказ на форматування
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
            
            // З'єднуємо таймкоди через кому!
            correctionsByNick[nick] = string.Join(", ", kvp.Value); 
        }

        var readRequest = _service.Spreadsheets.Values.Get(Config.SheetId, $"'{sheetName}'!A:G");
        var response = await readRequest.ExecuteAsync();
        var rows = response.Values;

        if (rows == null || rows.Count == 0) 
            throw new Exception("Аркуш порожній або не існує.");

        var dataToUpdate = new List<ValueRange>();

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Count < 4) continue; // Guard Clause

            var rowEp = row[0]?.ToString()?.Trim();
            var rowActorNick = row[3]?.ToString()?.Trim();

            if (rowEp != episode || string.IsNullOrEmpty(rowActorNick) || !correctionsByNick.ContainsKey(rowActorNick)) 
                continue; // Guard Clause

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
            throw new Exception($"Не знайдено акторів з такими тегами у серії {episode}.");

        var batchUpdateRequest = new BatchUpdateValuesRequest
        {
            ValueInputOption = "USER_ENTERED", 
            Data = dataToUpdate
        };

        var request = _service.Spreadsheets.Values.BatchUpdate(batchUpdateRequest, Config.SheetId);
        await request.ExecuteAsync();
    }
   // ==========================================
    // МЕТОД 1: Збір завдань по тайтлах (який зараз "загубився")
    // ==========================================
public async Task<List<crystal_shade_manager.Models.TitleTask>> GetTitlesTasksAsync()
    {
        var tasks = new List<crystal_shade_manager.Models.TitleTask>();
        try
        {
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
                    if (cleanLine.EndsWith("епізод:"))
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
} // <--- Це остання фігурна дужка всього класу GoogleSheetsService