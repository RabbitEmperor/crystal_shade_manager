using System.Collections.Generic;
using Telegram.Bot.Types.ReplyMarkups;
using crystal_shade_manager.Models;

namespace crystal_shade_manager.Helpers;

public static class KeyboardBuilder
{
    // Клавіатура для меню розсилки
    public static InlineKeyboardMarkup Build(RiseUpSession session)
    {
        var rows = new List<InlineKeyboardButton[]>();
        var currentRow = new List<InlineKeyboardButton>();

        for (int i = 0; i < session.Users.Count; i++)
        {
            var isChecked = session.Toggles[i];
            var icon = isChecked ? "✅" : "❌";
            
            string btnText = $"{icon} {session.Users[i]}"; 
            var btn = InlineKeyboardButton.WithCallbackData(btnText, $"t_{i}");
            currentRow.Add(btn);

            if (currentRow.Count == 2)
            {
                rows.Add(currentRow.ToArray());
                currentRow.Clear();
            }
        }
        
        if (currentRow.Count > 0)
        {
            rows.Add(currentRow.ToArray());
        }

        rows.Add(new[] { InlineKeyboardButton.WithCallbackData("🚀 Розпочати розсилку", "send") });

        return new InlineKeyboardMarkup(rows);
    }

    // --- НОВА КЛАВІАТУРА ДЛЯ МЕНЮ НАЛАШТУВАНЬ ---
    public static InlineKeyboardMarkup BuildSettings(BotSettings settings)
    {
        var selectAllText = settings.SelectAllByDefault ? "✅ Виділяти всіх рабів при старті" : "❌ Не виділяти нікого при старті";
        var speedText = settings.SafeModeDelay ? "🐢 Швидкість: БЕЗПЕЧНА (3 сек)" : "🚀 Швидкість: ШВИДКА (1.5 сек)";

        return new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData(selectAllText, "set_toggle_select") },
            new[] { InlineKeyboardButton.WithCallbackData(speedText, "set_toggle_speed") }
        });
    }
}