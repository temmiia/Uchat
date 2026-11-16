dotnet new install Avalonia.Templates
dotnet new sln -n uchat
dotnet new avalonia.mvvm -o .\uchat\UChat.App
dotnet sln .\uchat.sln add .\uchat\UChat.App\UChat.App.csprojdotnet run --project .\uchat\UChat.App\UChat.App.csproj# UChat

Цей репозиторій містить спроєктовану схему реляційної бази даних, доменні моделі, конфігурації Entity Framework Core 10 (SQLite) та тестове покриття для клієнт-серверного месенджера **UChat**.

---

## 1. Вимоги та реалізовані сутності

Згідно з технічним завданням спроєктовано 4 ключові таблиці з обов'язковими ідентифікаторами та атрибутами:

| Таблиця | Первинний ключ | Зовнішні ключі / Індекси | Основні поля |
| :--- | :--- | :--- | :--- |
| **`Users`** | **`AccountId`** (string, PK, Immutable) | `UserName` (Unique Index) | `UserName`, `DisplayName`, `PasswordHash`, `AvatarUrl`, `Bio`, `CreatedAtUtc`, `UpdatedAtUtc` |
| **`Conversations`** | **`ConversationId`** (Guid, PK) | — | `Type` (`Direct` / `Group`), `Title`, `CreatedAtUtc`, `UpdatedAtUtc` |
| **`Members`** | **`MemberId`** (Guid, PK) | `FK -> Conversations`, `FK -> Users` | `AccountId`, `ConversationId`, `Role` (`Owner` / `Admin` / `Member`), `JoinedAtUtc` |
| **`Messages`** | **`MessageId`** (Guid, PK) | `FK -> Conversations`, `FK -> Users` | `SenderAccountId`, `ConversationId`, `Content`, `CreatedAtUtc`, `EditedAtUtc`, `IsDeleted`, `ClientMessageId` |

---

## 2. Концепція та правила незмінності `AccountID`

1. **Призначення `AccountId`**:
   - Задається виключно **один раз під час реєстрації** користувача.
   - **Ніколи не змінюється за жодних обставин**.
   - Навіть якщо користувач змінює свій публічний нікнейм (`UserName`) або відображуване ім'я (`DisplayName`), історія листування, авторство повідомлень і членство в групах не порушуються.
2. **Трьохрівневий захист від зміни**:
   - **Domain Model**: властивість `AccountId` має специфікатор `{ get; private init; }`.
   - **EF Core Metadata**: `builder.Property(u => u.AccountId).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);`.
   - **ChangeTracker Interceptor**: перевірка в `AppDbContext.SaveChanges()` та `SaveChangesAsync()`, яка негайно генерує `InvalidOperationException` при спробі підміни значення.

Повна специфікація та діаграма зв'язків: [docs/DATABASE.md](docs/DATABASE.md).

---

## 3. Автономне розгортання в один клік (SQLite + WAL)

Клас `DatabaseInitializer.InitializeAsync(context)` гарантує автоматичну підготовку бази даних при першому запуску:
- Створення таблиць та індексів (`EnsureCreatedAsync`).
- Вмикання режиму високої конкурентності: `PRAGMA journal_mode = WAL;`.
- Вмикання перевірки посилальної цілісності: `PRAGMA foreign_keys = ON;`.
- Ізольована ідемпотентність повідомлень через унікальний індекс `(ConversationId, SenderAccountId, ClientMessageId)`.
- Нормалізація дати й часу в UTC (`UtcDateTimeConverter`).

---

## 4. Структура проєкту

```text
/
├── docs/
│   └── DATABASE.md                         # Детальна специфікація БД, ERD та правила AccountID
├── uchat_server/
│   ├── UChat.Domain/                       # Сутності: User, Conversation, ConversationMember, Message
│   │   ├── Entities/                       # User.cs, Conversation.cs, ConversationMember.cs, Message.cs
│   │   └── Enums/                          # ConversationType.cs, MemberRole.cs
│   ├── UChat.Server.Infrastructure/        # EF Core AppDbContext, Configurations, DatabaseInitializer
│   │   ├── Configurations/                 # Fluent API конфігурації для кожної сутності
│   │   └── Data/                           # AppDbContext.cs, DatabaseInitializer.cs
│   └── tests/
│       └── UChat.Database.Tests/           # Модульні та інтеграційні тести бази даних
├── uchat.sln                               # Рішення .NET 10
├── global.json                             # Фіксація .NET 10 SDK
├── .editorconfig                           # Стиль коду C#
├── .gitattributes                          # Нормалізація кінців рядків (LF)
└── .gitignore                              # Ігнорування артефактів збірки та локальних БД
```

---

## 5. Збірка та тестування бази даних

### Збірка рішення:
```bash
dotnet build uchat.sln -c Release -m
```

### Запуск тестів бази даних:
```bash
dotnet test uchat.sln -c Release --no-build
```

Тести покривають:
- Незмінність `AccountId` та унікальність `UserName`.
- Зв'язки між бесідами (`Conversations`), учасниками (`Members`) та повідомленнями (`Messages`).
- Каскадне видалення бесіди зі збереженням облікових записів авторів (`Restrict`).
- М'яке видалення повідомлень (`Soft Delete`).
- Активацію режимів SQLite `WAL` та `foreign_keys`.
