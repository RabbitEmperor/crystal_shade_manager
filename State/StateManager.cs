using System.Collections.Concurrent;
using System.Collections.Generic;
using crystal_shade_manager.Models;
using crystal_shade_manager.Helpers;
using crystal_shade_manager.Interfaces;
using crystal_shade_manager.Services;

namespace crystal_shade_manager.State;

public class StateManager : IStateManager
{
    public Dictionary<string, List<string>> CachedUserMessages { get; set; } = new();
    public ConcurrentDictionary<string, RiseUpSession> ActiveSessions { get; } = new();
    
    private readonly ConcurrentDictionary<long, BotSettings> _chatSettings;

    public StateManager()
    {
        _chatSettings = SettingsManager.Load();
    }

    public BotSettings GetSettings(long chatId)
    {
        return _chatSettings.GetOrAdd(chatId, _ => 
        {
            var newSettings = new BotSettings();
            SettingsManager.Save(_chatSettings); 
            return newSettings;
        });
    }

    public void SaveSettings()
    {
        SettingsManager.Save(_chatSettings);
    }
}