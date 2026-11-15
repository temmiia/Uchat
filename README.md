dotnet new install Avalonia.Templates
dotnet new sln -n uchat
dotnet new avalonia.mvvm -o .\uchat\UChat.App
dotnet sln .\uchat.sln add .\uchat\UChat.App\UChat.App.csprojdotnet run --project .\uchat\UChat.App\UChat.App.csproj# UChat

**UChat** is a C#/.NET desktop client-server messenger featuring private and group chats, real-time messaging, and server-side message history.

> **Status:** Technical concept / MVP development. The features below describe requirements and plans, not necessarily completed functionality.

## Project Goal

Build a simple, reliable messenger where users can register, find others by `username`, exchange messages, and recover their conversation history after a restart or connection loss. The server handles authentication, authorization, and data storage.

## Technology Stack

| Component | Technology |
| --- | --- |
| Language and platform | C#, .NET 10 LTS (`net10.0`) |
| Desktop UI | Avalonia UI |
| MVVM | CommunityToolkit.Mvvm |
| HTTP API | ASP.NET Core Web API |
| Real-time communication | ASP.NET Core SignalR |
| Database | SQLite + Entity Framework Core |
| Authentication | ASP.NET Core Identity or another proven implementation |
| Testing | xUnit, TestServer / WebApplicationFactory |
| CI | GitHub Actions |
| Logging | Microsoft.Extensions.Logging |

SDK and dependency versions should be pinned in `global.json`, `Directory.Packages.props`, and/or `*.csproj` files. Use compatible stable package versions.

## MVP Features (Version 0.1.0)

- Registration, login, and logout; unique `username`, display name, and profile.
- User lookup by `username` and private conversations.
- Real-time messaging via SignalR, including sender and timestamp.
- Persistent server-side message history.
- Editing and deleting **only your own** messages.
- Reconnection and retrieval of missed messages without duplicates.
- Group creation, adding members, and group messaging.
- Authorization checks for HTTP API requests and SignalR commands.
- Support for two simultaneous clients and history persistence across server restarts.

Planned post-MVP features include advanced session management, backups, one-time invitation codes, UI improvements, reactions, attachments, and more.

## Architecture

UChat follows a **modular monolith** architecture: one server application with separate API, application logic, domain, and infrastructure layers. The desktop client manages the UI, local settings, and network communication.

```text
/
├── uchat/
│   ├── UChat.App/                 # Avalonia UI, Views, ViewModels
│   ├── UChat.Core/                # DTOs, client models, interfaces
│   ├── UChat.Infrastructure/      # HTTP/SignalR client, settings
│   └── tests/                     # Client tests
├── uchat_server/
│   ├── UChat.Server/              # ASP.NET Core API, SignalR Hub, CLI
│   ├── UChat.Application/         # Application use cases
│   ├── UChat.Domain/              # Entities and business rules
│   ├── UChat.Infrastructure/      # EF Core, SQLite, migrations
│   └── tests/                     # Server tests
├── uchat.sln
├── global.json
├── Directory.Packages.props       # If central package management is used
├── .editorconfig
├── .gitignore
└── README.md
```

> This is the **proposed repository structure** from the technical concept. Confirm actual project names, test assemblies, and commands against the source code once created.

### Message Delivery Flow

1. The client sends a command containing `ConversationId`, message text, and a unique `ClientMessageId` (idempotency key).
2. The server validates the session, conversation membership, permissions, and input data.
3. The server saves the message in SQLite and assigns `CreatedAt` in UTC.
4. **Only after successful persistence**, the server broadcasts a SignalR event to conversation participants.
5. After reconnecting, the client fetches missed messages and merges them by `MessageId` to avoid duplicates.

## Development Prerequisites

- **.NET 10 SDK** compatible with `global.json`.
- Git.
- A C# IDE or editor (for example, Rider or Visual Studio).
- NuGet access to restore dependencies.

## Build and Run

The following commands assume the proposed project structure. They will work once the corresponding `.csproj` files, configuration, and application entry points are implemented.

```bash
# From the repository root
dotnet restore uchat.sln

# Verify the Release build
dotnet build uchat.sln -c Release -m

# Start the server in a separate terminal
dotnet run --project uchat_server/UChat.Server

# Start the desktop client
dotnet run --project uchat/UChat.App
```

To test communication between two clients, launch the desktop client twice if supported by the implementation. The server address, ports, SQLite database path, and secret configuration must be documented once implemented; the technical concept does not specify exact values.

## Testing and Code Quality

```bash
dotnet format uchat.sln --verify-no-changes
dotnet build uchat.sln -c Release -m
dotnet test uchat.sln -c Release --no-build
```

Important test scenarios include registration and login, incorrect passwords, message exchange between two clients, persistence after server restart, reconnect, rejection of edits/deletions by non-authors, and denial of group history access to non-members.

Planned GitHub Actions CI pipeline:

`checkout → setup-dotnet → restore → format check → Release build → tests → dependency/security scan`

The technical concept also sets a target of **under 30 seconds** for a build in the evaluation environment; this must be verified through measurement.

## Security and Privacy

- Use HTTPS/TLS for API and SignalR in network deployments.
- Store passwords using proven password hashing; do not invent custom cryptography.
- Enforce authorization on every HTTP request and SignalR command.
- Allow only conversation members to access group history and events.
- Allow only the author to edit or delete a message.
- Never log passwords, tokens, keys, or message contents.
- Keep secrets in environment variables or a secret store, not in Git.
- Manage database schema changes through EF Core migrations and plan for backups.

**Important:** TLS protects data in transit but **does not provide end-to-end encryption (E2EE)**. UChat must not claim E2EE support unless it has been implemented and independently reviewed.

## Git Workflow and Conventions

The `main` branch is protected: direct commits are not allowed. Changes should be developed in feature branches, reviewed in pull requests, and merged only after CI passes.

Branch naming format: `type/issue-short-description`. Examples:

```text
feat/42-private-chats
fix/63-empty-message-validation
test/84-chat-permissions
chore/12-editorconfig
```

Use **Conventional Commits**: `type(scope): short description`. Commit subjects must be in English, no longer than 72 characters, and must not end with a period.

```text
feat(chat): add private messaging
fix(auth): validate expired tokens
test(groups): cover unauthorized access
docs(readme): describe local setup
ci(build): add release checks
```

Supported types: `feat`, `fix`, `refactor`, `test`, `docs`, `style`, `perf`, `chore`, `build`, `ci`. Before opening a pull request, verify formatting, build, tests, and absence of committed secrets.

## Release Roadmap

| Version | Planned scope |
| --- | --- |
| `0.1.0` MVP | Accounts, private/group chats, history, real-time messaging, reconnect, edit/delete, authorization |
| `0.2.0` Hardening | Stronger security, session management, audit logging, backup/restore |
| `0.3.0` Groups+ | Advanced roles, membership management, invitations |
| `0.4.0` UX/UI | History search, presence, typing indicators, themes, keyboard shortcuts |
| Later | Attachments, reactions, replies; E2EE only after a dedicated cryptographic review |

## Team Collaboration

According to the technical concept, the six-person team consists of a Team Lead, a Tech Lead, two Backend Developers, a Client/UI Developer, and a QA/Automation/DevOps Engineer. Discord is the primary communication channel; an issue tracker manages tasks; GitHub/GitLab hosts code and reviews. Short sync meetings are held every two days, and API/architecture decisions are documented.

## Documentation Status

This README is based on the UChat technical concept. Update the setup instructions, configuration details, actual test project names, and feature completion status as implementation progresses.
