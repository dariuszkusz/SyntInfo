# SyntInfo (InfoSkrót) - AI News Aggregator & Omnichannel Publisher

SyntInfo (**InfoSkrót**) is a modern, automated AI news aggregator and omnichannel publishing platform. It ingests news from Polish and international RSS feeds as well as web search discovery, semantically clusters articles covering the same event using vector embeddings (`pgvector`), verifies facts, and generates concise, neutral summaries. Selected stories are presented in a minimalist Progressive Web App (PWA) and distributed across social platforms and an email newsletter with human-in-the-loop Telegram curation.

## 🌟 Key Features

- **Automated Multi-Region Ingestion**: Continuous fetching of RSS feeds (`Poland`, `World`, `Positive` news) with **Tavily Search** fallback discovery and metadata (`og:image`) extraction.
- **Semantic Deduplication & Clustering**: Uses vector embeddings (`pgvector` cosine similarity) to group reports about the same event from multiple publishers into a single consolidated story.
- **Two-Stage AI Summarization**: Extracts verified facts first and synthesizes neutral, clickbait-free summaries using **Google AI Studio (Gemini)** with automatic multi-model fallback to **OpenRouter**.
- **Human-in-the-Loop Telegram Curation**: Sends thematic proposal batches (`🇵🇱 Poland`, `🌍 World`, `☀️ Positive`) and an interactive Digest Control Panel to Telegram (`Telegram.Bot`). Includes a scheduled Quartz.NET fallback job (`SocialCurationFallbackJob`) if the admin does not manually trigger publication.
- **Omnichannel Digest & Social Publishing**: Publishes daily digests and staggered individual posts to **Wykop API v3** (OAuth2 + auto token refresh), **Hejto API**, **Facebook**, and **MailerLite** (HTML newsletter rendered 1:1 with the web portal's design).
- **Minimalist PWA Frontend**: Fast, distraction-free Angular Progressive Web App styled with Tailwind CSS.

## 🛠 Technology Stack

### Backend (.NET 10)
- **Clean Architecture & S.O.L.I.D**: Strictly decoupled layers (`SyntInfo.Domain`, `SyntInfo.Application`, `SyntInfo.Infrastructure`, `SyntInfo.Api`).
- **CQRS & Messaging**: **WolverineFx** (`IMessageBus`) for commands, queries, local queues, and scheduled delayed execution.
- **Persistence**: **Entity Framework Core** with **PostgreSQL + pgvector** (Code First migrations) and the **Unit of Work + Repository** pattern.
- **AI & Search Integration**: **Google AI Studio** (`gemini-3.5-flash` / `gemini-3.1-flash-lite`), **OpenRouter** (LLM fallback & embeddings), and **Tavily API**.
- **Background Jobs**: **Quartz.NET** with `Europe/Warsaw` timezone scheduling (`RssFetcherJob`, `SocialCurationFallbackJob`).
- **Security**: Constant-time `X-Admin-Key` / `Bearer` authentication (`ApiKeyAuthenticationHandler`), constant-time Telegram webhook secret validation, and SSRF-safe remote image downloading (`RemoteImageDownloader`).
- **Unit Testing**: **124 unit tests** powered by **xUnit**, **NSubstitute**, **FluentAssertions**, and **MockQueryable** (`SyntInfo.UnitTests`).

### Frontend (Angular)
- **State Management**: **NgRx** for predictable, reactive application state.
- **Styling**: **Tailwind CSS** with a minimalist reading experience.
- **Progressive Web App (PWA)**: Installable on mobile and desktop devices.
- **End-to-End Testing**: **Cypress** for component and E2E verification.

## 🏗 Architecture

The backend follows **Clean Architecture** and **CQRS**:
- **`SyntInfo.Domain`**: Core entities (`NewsArticle`, `NewsSource`, `NewsCategory`, `SocialPostDraft`), domain state transitions, repository contracts (`IUnitOfWork`, `INewsArticleRepository`), and domain helpers (`TagFormattingHelper`).
- **`SyntInfo.Application`**: CQRS Commands, Queries, and Wolverine Handlers, clustering & AI parsing helpers (`RssClusteringHelpers`, `AiSummaryResponseParser`), prompt templates (`AiPrompts`), DTOs, and service interfaces.
- **`SyntInfo.Infrastructure`**: EF Core `AppDbContext` & repositories, `UnitOfWork`, Quartz.NET background jobs, AI & search clients (`GoogleAiStudioClient`, `OpenRouterClient`, `TavilySearchService`), platform clients (`WykopClient`, `HejtoClient`, `MailerLiteClient`, `FacebookClient`, `TelegramNotificationService`), and `NewsletterHtmlRenderer`.
- **`SyntInfo.Api`**: Thin ASP.NET Core controllers dispatching commands/queries via `IMessageBus`, authentication handlers, and DI/Wolverine/Quartz configuration extensions.
- **`SyntInfo.UnitTests`**: Comprehensive unit test suite covering Domain entities, 100% of CQRS handlers, Quartz jobs, and Infrastructure services.

---

# SyntInfo (InfoSkrót) - Agregator Newsów AI i Platforma Publikacji

SyntInfo (**InfoSkrót**) to nowoczesny, zautomatyzowany agregator informacji oraz system wielokanałowej publikacji wykorzystujący sztuczną inteligencję. System pobiera wiadomości z polskich i zagranicznych kanałów RSS oraz wyszukiwarki internetowej, grupuje semantycznie artykuły opisujące to samo wydarzenie za pomocą wektorów (`pgvector`), weryfikuje fakty i generuje zwięzłe, neutralne streszczenia. Wybrane materiały są prezentowane w minimalistycznej aplikacji PWA oraz dystrybuowane na platformy społecznościowe i do newslettera z udziałem kuracji na Telegramie (human-in-the-loop).

## 🌟 Główne Funkcje

- **Automatyczna Ingestia Wieloregionalna**: Cykliczne pobieranie kanałów RSS (`Polska`, `Świat`, `Pozytywne`) wspierane przez wyszukiwanie **Tavily Search** oraz ekstrakcję metadanych (`og:image`).
- **Klastrowanie Semantyczne i Deduplikacja**: Wykorzystanie embeddingów wektorowych (`pgvector` i podobieństwa kosinusowego) do łączenia artykułów z różnych redakcji o tym samym wydarzeniu w jeden skonsolidowany materiał.
- **Dwuetapowa Synteza AI**: Najpierw ekstrakcja twardych faktów, a następnie generowanie neutralnego podsumowania bez clickbaitu przy użyciu **Google AI Studio (Gemini)** z automatycznym fallbackiem do **OpenRouter**.
- **Kuracja Wydania na Telegramie (Human-in-the-Loop)**: Wysyłka kategoryzowanych propozycji (`🇵🇱 Polska`, `🌍 Świat`, `☀️ Pozytywne`) oraz interaktywnego Panelu Sterowania Wydania przez bota Telegram (`Telegram.Bot`). Zawiera również automatyczny wyzwalacz awaryjny (`SocialCurationFallbackJob`), jeśli administrator nie zatwierdzi wydania ręcznie.
- **Wielokanałowa Publikacja (Omnichannel)**: Automatyczna publikacja wydań zbiorczych oraz pojedynczych wpisów (z kolejkowaniem czasowym na Hejto) na **Wykop API v3** (OAuth2 + auto-refresh tokenów), **Hejto API**, **Facebook** oraz **MailerLite** (responsywny newsletter HTML renderowany 1:1 z wyglądem portalu).
- **Minimalistyczny Frontend PWA**: Przejrzysta aplikacja Angular pozbawiona szumu informacyjnego, zoptymalizowana pod kątem szybkiej lektury.

## 🛠 Stos Technologiczny

### Backend (.NET 10)
- **Clean Architecture & S.O.L.I.D**: Ścisły podział na warstwy (`SyntInfo.Domain`, `SyntInfo.Application`, `SyntInfo.Infrastructure`, `SyntInfo.Api`).
- **CQRS & Messaging**: **WolverineFx** (`IMessageBus`) do obsługi komend, zapytań, kolejek lokalnych oraz opóźnionej publikacji zadań (`ScheduleAsync`).
- **Baza Danych**: **EF Core** z **PostgreSQL + pgvector** (podejście Code First) oraz wzorzec **Unit of Work + Repository**.
- **Integracje AI i Wyszukiwania**: **Google AI Studio** (`gemini-3.5-flash` / `gemini-3.1-flash-lite`), **OpenRouter** (fallback LLM oraz embeddingi) i **Tavily API**.
- **Zadania w tle**: **Quartz.NET** z obsługą strefy czasowej `Europe/Warsaw` (`RssFetcherJob`, `SocialCurationFallbackJob`).
- **Bezpieczeństwo**: Autoryzacja administracyjna `X-Admin-Key` / `Bearer` odporna na ataki czasowe (`CryptographicOperations.FixedTimeEquals`), weryfikacja nagłówka sekretu Telegrama w stałym czasie oraz bezpieczne pobieranie obrazów odporne na SSRF/LFI (`RemoteImageDownloader`).
- **Testy Jednostkowe**: **124 testy jednostkowe** oparte o **xUnit**, **NSubstitute**, **FluentAssertions** oraz **MockQueryable** (`SyntInfo.UnitTests`).

### Frontend (Angular)
- **State Management**: Zarządzanie stanem przy użyciu **NgRx**.
- **Styl**: **Tailwind CSS** w nowoczesnym, minimalistycznym wydaniu.
- **Progressive Web App (PWA)**: Pełna responsywność i możliwość instalacji na urządzeniach mobilnych oraz desktopowych.
- **Testy E2E**: **Cypress** do weryfikacji komponentów i ścieżek użytkownika.

## 🏗 Architektura

System został zbudowany zgodnie z zasadami **Clean Architecture** oraz **CQRS**:
- **`SyntInfo.Domain`**: Encje domenowe (`NewsArticle`, `NewsSource`, `NewsCategory`, `SocialPostDraft`), reguły przejść stanów, interfejsy repozytoriów (`IUnitOfWork`, `INewsArticleRepository`) oraz helpery domenowe (`TagFormattingHelper`).
- **`SyntInfo.Application`**: Komendy, zapytania i handlery CQRS (Wolverine), moduły klastrowania i parsowania odpowiedzi AI (`RssClusteringHelpers`, `AiSummaryResponseParser`), prompty (`AiPrompts`), obiekty DTO oraz kontrakty serwisów.
- **`SyntInfo.Infrastructure`**: Kontekst bazy danych `AppDbContext`, repozytoria i `UnitOfWork`, zadania Quartz.NET, klienci AI (`GoogleAiStudioClient`, `OpenRouterClient`, `TavilySearchService`), integracje zewnętrzne (`WykopClient`, `HejtoClient`, `MailerLiteClient`, `FacebookClient`, `TelegramNotificationService`) oraz generator HTML newslettera (`NewsletterHtmlRenderer`).
- **`SyntInfo.Api`**: Odchudzone kontrolery ASP.NET Core komunikujące się wyłącznie przez `IMessageBus`, obsługa uwierzytelniania oraz rozszerzenia konfiguracyjne DI.
- **`SyntInfo.UnitTests`**: Zestaw 124 testów jednostkowych pokrywających encje domenowe, 100% handlerów CQRS, zadania Quartz.NET oraz serwisy infrastrukturalne.

## ⚠️ Zasady testowania na platformach zewnętrznych (Wykop, Hejto, Reddit, FB)

> [!IMPORTANT]
> Platformy społecznościowe (Hejto, Wykop, Reddit itp.) **nie posiadają odrębnego środowiska typu sandbox** – nawet aplikacje ze statusem deweloperskim publikują wpisy na żywej platformie.
> 
> **Zasada bezwzględna:** Wszystkie wpisy, requesty i posty wysyłane w ramach testów integracyjnych na żywe platformy **MUSZĄ** zawierać w tytule lub na początku treści wyraźne oznaczenie:  
> **`[TEST - NIE KLIKAĆ]`** (lub w treści: `Test NIE KLIKAĆ`).
> 
> W przypadku testów lokalnych zaleca się weryfikację z włączonym trybem `UseMock: true` lub publikowanie do społeczności technicznych/testowych.
