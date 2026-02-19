using System.Collections.Generic;
using Telegram.Bot.Types.ReplyMarkups;
using crystal_shade_manager.Models;

namespace crystal_shade_manager.Helpers;

public static class KeyboardBuilder
{
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
}