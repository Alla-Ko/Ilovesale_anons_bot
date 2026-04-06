# Announcement

Монолітний ASP.NET Core 8 (Razor Pages), PostgreSQL, Identity з ролями. Анонси з колажами, завантаження медіа (ImgBB / tmpfile.link), інтеграції Telegraph та Telegram.

## Вимоги

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [PostgreSQL](https://www.postgresql.org/download/) (локально або хмара, наприклад Railway)

## Локальний запуск

### 1. Клонування / відкриття проєкту

Каталог з проєктом: папка `Announcement` (файл `Announcement.csproj`).

### 2. Секрети та підключення до БД

1. Скопіюйте `.env.example` у `.env` у тій самій папці, що й `Announcement.csproj`.
2. Заповніть мінімум:
   - `ConnectionStrings__DefaultConnection` — рядок Npgsql, наприклад:
     - `Host=localhost;Port=5432;Database=announcement;Username=postgres;Password=...;SSL Mode=Require;Trust Server Certificate=true`
   - `ADMIN_USERNAME` та `ADMIN_PASSWORD` — логін і пароль першого адміністратора (без плейсхолдерів на кшталт `YOUR_...`).

Файл `.env` завантажується на старті застосунку (див. `Program.cs`). Не комітьте `.env` у git.

### 3. Міграції бази даних

У каталозі проєкту (`Announcement`):

```bash
dotnet ef database update
```

Потрібен інструмент EF (якщо ще не встановлено):

```bash
dotnet tool install --global dotnet-ef
```

### 4. Запуск

```bash
cd Announcement
dotnet run
```

Відкрийте в браузері URL з консолі (за замовчуванням `http://localhost:5xxx`). Сторінка входу: `/Account/Login`.

### 5. Тимчасовий повний пересоздання БД (тільки для розробки)

У `Program.cs` є виклик `DevDatabaseRecreateSeeder.RunAsync`: при кожному старті **видаляється база**, заново застосовуються міграції та створюються ролі й перший адмін з `.env`.

Після першого успішного локального запуску **закоментуйте або видаліть** цей виклик, щоб не стирати дані при кожному `dotnet run`.

Далі достатньо звичайного `DbSeeder.SeedAsync`, який лише застосовує міграції та при потребі додає відсутні ролі / адміна.

## Корисні команди

| Дія | Команда |
|-----|--------|
| Збірка | `dotnet build` |
| Нова міграція | `dotnet ef migrations add ІмяМіграції` |
| Оновлення БД | `dotnet ef database update` |

## Структура конфігурації

- `appsettings.json` — нечутливі налаштування; секрети краще в `.env` або змінних оточення.
- Змінні з подвійним підкресленням у `.env` відповідають вкладеним ключам конфігурації (наприклад `ConnectionStrings__DefaultConnection`, `ImgBB__ApiKey`).

Детальні плейсхолдери — у `.env.example`.
