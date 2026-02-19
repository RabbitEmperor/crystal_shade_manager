using System.Collections.Generic;

namespace crystal_shade_manager.Models;

public class RiseUpSession
{
    public List<string> Users { get; set; } = new();
    public Dictionary<int, bool> Toggles { get; set; } = new();
    public Dictionary<int, List<string>> Messages { get; set; } = new();
}