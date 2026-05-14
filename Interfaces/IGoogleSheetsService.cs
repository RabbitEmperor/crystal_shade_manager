using System.Collections.Generic;
using System.Threading.Tasks;

namespace crystal_shade_manager.Services;

public interface IGoogleSheetsService
{
    Task<Dictionary<string, List<string>>> GetUserTaskMessagesAsync();
    // Новий метод для запису
    Task AddLogEntryAsync(string userName, string action);
    // НОВИЙ МЕТОД:
    Task AddCastListAsync(string sheetName, string episode, List<(string Character, string Actor)> cast, string deadline);
    Task UpdateCorrectionsAsync(string sheetName, string episode, Dictionary<string, List<string>> corrections);
    Task<List<crystal_shade_manager.Models.TitleTask>> GetTitlesTasksAsync();
    // Додай цей рядок туди, де в тебе інші методи:
    Task<Dictionary<string, string>> GetTeamTagsAsync();
    Task<List<crystal_shade_manager.Models.TitleTask>> GetUserDebtsAsync(string nickname, string tag);
}