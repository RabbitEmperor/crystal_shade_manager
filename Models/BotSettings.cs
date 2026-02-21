namespace crystal_shade_manager.Models;

public class BotSettings
{
    // За замовчуванням при старті у всіх стоять галочки
    public bool SelectAllByDefault { get; set; } = true;
    
    // За замовчуванням увімкнена безпечна затримка (3.1 секунди)
    public bool SafeModeDelay { get; set; } = true; 
}