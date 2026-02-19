using System.Collections.Generic;
using System.Threading.Tasks;

namespace crystal_shade_manager.Services;

public interface IGoogleSheetsService
{
    Task<Dictionary<string, List<string>>> GetUserTaskMessagesAsync();
}