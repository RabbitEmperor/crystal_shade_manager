using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using crystal_shade_manager.Interfaces;

namespace crystal_shade_manager.Services;

public class AutoUpdateService
{
    private readonly IGoogleSheetsService _sheetsService;
    private readonly IStateManager _stateManager;

    public AutoUpdateService(IGoogleSheetsService sheetsService, IStateManager stateManager)
    {
        _sheetsService = sheetsService;
        _stateManager = stateManager;
    }

    public void Start(CancellationToken ct)
    {
        // Запускаємо безкінечний цикл у фоновому потоці, щоб він не блокував роботу самого бота
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    // Спочатку чекаємо 5 хвилин (щоб не оновлювати одразу при старті, бо ти це вже робиш у Program.cs)
                    await Task.Delay(TimeSpan.FromMinutes(5), ct);

                    Console.WriteLine("🔄 [AutoUpdate] Починаю автоматичне оновлення даних з Google Таблиць...");

                    // 1. Стягуємо дані (використовую твій існуючий метод для повідомлень/тасків)
                    var newData = await _sheetsService.GetUserTaskMessagesAsync();

                    if (newData != null)
                    {
                        // 2. Оновлюємо оперативну пам'ять бота (це найшвидше і найголовніше!)
                        _stateManager.CachedUserMessages = newData;

                        // 3. Записуємо у JSON файл
                        // WriteIndented = true робить файл красивим (з відступами), щоб його легко читали люди
                        string jsonString = JsonSerializer.Serialize(newData, new JsonSerializerOptions { WriteIndented = true });
                        await File.WriteAllTextAsync("cached_data.json", jsonString, ct);

                        Console.WriteLine($"✅ [AutoUpdate] Дані оновлено! Записано у cached_data.json. Кількість записів: {newData.Count}");
                    }
                }
                catch (TaskCanceledException)
                {
                    // Бот вимикається (наприклад, зупинили в термінал), просто виходимо з циклу
                    break; 
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ [AutoUpdate] Помилка під час оновлення: {ex.Message}");
                }
            }
        }, ct);
    }
}