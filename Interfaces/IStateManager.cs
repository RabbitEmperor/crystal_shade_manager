using System.Collections.Concurrent;
using System.Collections.Generic;
using crystal_shade_manager.Models;

namespace crystal_shade_manager.Interfaces;

public interface IStateManager
{
    Dictionary<string, List<string>> CachedUserMessages { get; set; }
    ConcurrentDictionary<string, RiseUpSession> ActiveSessions { get; }
    BotSettings GetSettings(long chatId);
    void SaveSettings();
}