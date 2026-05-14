# ЕТАП 1: Збірка (Build)
# Використовуємо офіційний образ .NET SDK для компіляції коду
# (Якщо в тебе .NET 7 або 9, зміни 8.0 на свою версію)
FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

# Копіюємо файл проєкту і відновлюємо залежності (NuGet пакети)
COPY ["crystal_shade_manager.csproj", "./"]
RUN dotnet restore "crystal_shade_manager.csproj"

# Копіюємо весь інший вихідний код
COPY . .
WORKDIR "/src/"

# Збираємо проєкт у режимі Release
RUN dotnet build "crystal_shade_manager.csproj" -c Release -o /app/build

# Публікуємо готовий додаток
RUN dotnet publish "crystal_shade_manager.csproj" -c Release -o /app/publish /p:UseAppHost=false

# ЕТАП 2: Запуск (Runtime)
# Використовуємо легкий образ тільки з Runtime (без інструментів розробки)
FROM mcr.microsoft.com/dotnet/runtime:10.0-preview
WORKDIR /app

# Копіюємо скомпільовані файли з першого етапу
COPY --from=build /app/publish .

# ВАЖЛИВО: Встановлюємо часовий пояс (щоб логи і час в таблиці гугла були правильними)
# За замовчуванням ставимо Київський час
ENV TZ=Europe/Kyiv
RUN ln -snf /usr/share/zoneinfo/$TZ /etc/localtime && echo $TZ > /etc/timezone

# Запускаємо бота
ENTRYPOINT ["dotnet", "crystal_shade_manager.dll"]