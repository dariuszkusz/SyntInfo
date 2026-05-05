# SyntInfo - AI News Aggregator

SyntInfo is a modern, automated news aggregator that leverages artificial intelligence to provide concise, factual, and minimalist news summaries. The project focuses on cost-efficiency and performance, utilizing background jobs to fetch RSS feeds and local Large Language Models (LLMs) to generate clustered summaries.

## 🌟 Features

- **Automated RSS Fetching**: Real-time news ingestion from various national (Poland) and international sources.
- **AI-Powered Summarization**: Uses local LLM integration to generate brief, neutral summaries of clustered news articles.
- **Minimalist UI**: A clean, distraction-free interface designed for quick consumption of information.
- **Progressive Web App (PWA)**: Fully mobile-friendly and installable as a native-like app.
- **Background Automation**: Quartz.NET based jobs for continuous data processing.

## 🛠 Technology Stack

### Backend (.NET 10)
- **Clean Architecture**: Decoupled layers (Api, Application, Domain, Infrastructure).
- **CQRS**: Command Query Responsibility Segregation using MediatR.
- **Persistence**: EF Core with PostgreSQL (Code First approach).
- **Automation**: Quartz.NET for scheduled background tasks.
- **Unit of Work**: Consistent data access pattern for transaction integrity.

### Frontend (Angular)
- **State Management**: NgRx for predictable application state.
- **Styling**: Tailwind CSS with a sleek, minimalist design system.
- **Testing**: Cypress for comprehensive End-to-End verification.

## 🏗 Architecture

The system follows Clean Architecture principles, ensuring scalability and maintainability:
- **Domain**: Core business entities and fundamental logic.
- **Application**: CQRS Handlers and business rule implementations.
- **Infrastructure**: Database persistence, AI client communication, and background job logic.
- **API**: ASP.NET Core endpoints and global configuration.

---

# SyntInfo - Agregator Newsów AI

SyntInfo to nowoczesny, zautomatyzowany agregator informacji, który wykorzystuje sztuczną inteligencję do dostarczania zwięzłych, merytorycznych i minimalistycznych streszczeń wiadomości. Projekt stawia na wydajność i niskie koszty eksploatacji, wykorzystując zadania w tle do pobierania kanałów RSS oraz lokalne modele językowe (LLM) do generowania zgrupowanych podsumowań.

## 🌟 Funkcje

- **Automatyczne Pobieranie RSS**: Ingestia wiadomości w czasie rzeczywistym z wielu polskich i zagranicznych źródeł.
- **Streszczenia Napędzane przez AI**: Integracja z lokalnymi modelami LLM w celu generowania krótkich, neutralnych podsumowań powiązanych artykułów.
- **Minimalistyczny UI**: Przejrzysty interfejs pozbawiony szumu informacyjnego, zoptymalizowany pod kątem szybkiej lektury.
- **Progressive Web App (PWA)**: Aplikacja w pełni responsywna, z możliwością instalacji na urządzeniach mobilnych.
- **Automatyzacja procesów**: Wykorzystanie Quartz.NET do ciągłego przetwarzania danych w tle.

## 🛠 Stos Technologiczny

### Backend (.NET 10)
- **Clean Architecture**: Podział na warstwy API, Application, Domain oraz Infrastructure.
- **CQRS**: Wykorzystanie wzorca MediatR do separacji komend i zapytań.
- **Baza Danych**: EF Core z PostgreSQL (podejście Code First).
- **Automatyzacja**: Quartz.NET do obsługi harmonogramu zadań.
- **Unit of Work**: Zaimplementowany wzorzec Unit of Work dla spójności i integralności transakcji.

### Frontend (Angular)
- **State Management**: Zarządzanie stanem przy użyciu NgRx.
- **Styl**: Tailwind CSS w nowoczesnym, minimalistycznym wydaniu.
- **Testy**: Cypress do weryfikacji funkcjonalnej E2E.

## 🏗 Architektura

System został zbudowany w oparciu o zasady Clean Architecture, co gwarantuje łatwą skalowalność i utrzymanie:
- **Domain**: Główne encje biznesowe i logika rdzenna.
- **Application**: Obsługa wzorca CQRS i reguł biznesowych.
- **Infrastructure**: Dostęp do bazy danych, integracja z klientem AI oraz zadania w tle.
- **API**: Kontrolery ASP.NET Core i konfiguracja globalna.
