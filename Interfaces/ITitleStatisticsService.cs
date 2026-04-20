using System.Collections.Generic;
using System.Threading.Tasks;
using crystal_shade_manager.Models;

namespace crystal_shade_manager.Services;

public interface ITitleStatisticsService
{
    Task<List<TitleTask>> GetTitlesTasksAsync();
}