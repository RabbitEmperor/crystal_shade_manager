using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace crystal_shade_manager.Interfaces;

public interface ITelegramCommand
{
    string Trigger { get; } // Наприклад: "/cast"
    Task ExecuteAsync(ITelegramBotClient botClient, Message message, CancellationToken token);
}