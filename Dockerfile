# Етап 1: Збірка (Build)
FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

# Копіюємо проект та відновлюємо залежності
COPY ["crystal_shade_manager.csproj", "./"]
RUN dotnet restore "crystal_shade_manager.csproj"

# Копіюємо все інше та публікуємо
COPY . .
RUN dotnet publish "crystal_shade_manager.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Етап 2: Запуск (Runtime)
# ВАЖЛИВО: Використовуємо aspnet замість runtime, щоб підтримувати веб-частину
FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview
WORKDIR /app

# Копіюємо зібрані файли
COPY --from=build /app/publish .

# Налаштування часового поясу (Київ)
ENV TZ=Europe/Kyiv
# В образі aspnet (на базі Debian/Alpine) команди налаштування можуть відрізнятися, 
# але ENV TZ зазвичай достатньо для .NET
ENV ASPNETCORE_URLS=http://+:10000

# Точка входу
ENTRYPOINT ["dotnet", "crystal_shade_manager.dll"]