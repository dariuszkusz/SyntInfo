namespace SyntInfo.Application.Prompts;

/// <summary>
/// Centralne, pojedyncze źródło prawdy (Single Source of Truth) dla promptów LLM w systemie.
/// Zapewnia spójność promptów pomiędzy różnymi klientami AI (Google AI Studio, OpenRouter, Local LLM)
/// oraz ścisłą zgodność ze strukturami DTO/JSON wymaganymi przez domenę i aplikację.
/// </summary>
public static class AiPrompts
{
    public static class ArticleSelection
    {
        public static string BuildSystemPrompt(int expectedCount, SourceRegion region)
        {
            if (region == SourceRegion.Positive)
            {
                return $"Jesteś asystentem redakcyjnym odpowiedzialnym za sekcję POZYTYWNYCH WIADOMOŚCI. " +
                       $"Twoim zadaniem jest wybranie dokładnie do {expectedCount} indeksów artykułów, które są WYŁĄCZNIE I JEDNOZNACZNIE POZYTYWNE, " +
                       $"optymistyczne, budujące lub opisujące sukcesy, odkrycia naukowe, przełomy medyczne, ochronę przyrody czy pomoc ludziom. " +
                       $"BEZWZGLĘDNIE ODRZUĆ wiadomości o stratach (np. straty w sadach, susza, powódź), problemach gospodarczych, wypadkach, wojnie, kryzysach czy sporach politycznych. " +
                       $"Jeśli żaden artykuł nie jest jednoznacznie pozytywny, zwróć pustą listę []. " +
                       $"Zwróć WYŁĄCZNIE czysty JSON: {{\"selectedIndexes\": [0, 1, ...]}}.";
            }

            string regionCriteria = region == SourceRegion.Poland
                ? "o znaczeniu dla obywatela Polski"
                : "znaczeniem międzynarodowym";

            return $"Jesteś asystentem redakcyjnym. Otrzymujesz w formacie JSON (array) listę dostępnych najnowszych artykułów informacyjnych z ich indeksami, tytułami i opisami. " +
                   $"Twoim zadaniem jest wskazanie dokładnie {expectedCount} indeksów NAJWAŻNIEJSZYCH tekstów z tej listy. " +
                   $"Kieruj się skalą problemu, {regionCriteria} i siłą oddziaływania społecznego lub gospodarczego. " +
                   $"Zwroc WYŁĄCZNIE czysty, poprawny obiekt JSON w formacie: {{\"selectedIndexes\": [0, 1, 3, ...]}}. Nie dodawaj innych tłumaczeń ani komentarzy.";
        }
    }

    public static class FactExtraction
    {
        public const string SystemPrompt =
            "Jesteś ekspertem analizy danych i analitykiem informacji. Twoim zadaniem jest wyciągnięcie kluczowych faktów z dostarczonych materiałów (RSS i wyniki wyszukiwania).\n\n" +
            "Zasady:\n" +
            "1. Zidentyfikuj co najmniej 5-8 najważniejszych faktów.\n" +
            "2. Wyeliminuj duplikaty i sprzeczne informacje.\n" +
            "3. Zwroc dane WYŁĄCZNIE w formacie JSON jako tablicę obiektów: [{\"fact\": \"treść faktu\", \"source\": \"krótki opis źródła\"}].\n" +
            "4. Nie dodawaj wstępu ani zakończenia.";

        public static string BuildUserPrompt(string articleOrRssContent, string searchContent)
        {
            return $"[DANE Z ARTYKUŁU/RSS]:\n{articleOrRssContent}\n\n[DANE Z WYSZUKIWARKI]:\n{searchContent}";
        }
    }

    public static class Summary
    {
        public const string FromFactsSystemPrompt =
            "Jesteś doświadczonym redaktorem i architektem informacji. Twoim zadaniem jest stworzenie minimalistycznego, treściwego podsumowania w języku polskim (tzw. Infopiguła) na podstawie listy faktów.\n\n" +
            "Zasady:\n" +
            "1. TYTUŁ: Stwórz faktyczny, neutralny nagłówek w języku polskim.\n" +
            "2. PODSUMOWANIE: Stwórz 3-4 konkretne akapity (łącznie 400-800 znaków).\n" +
            "3. KATEGORIA: Przypisz jedną kategorię (np. BIZNES, POLITYKA, TECH, ŚWIAT, PL).\n" +
            "4. FORMAT: Zwróć WYŁĄCZNIE czysty, poprawny obiekt JSON: {\"title\": \"...\", \"essence\": \"...\", \"category\": \"...\"}.\n" +
            "5. JĘZYK: Odpowiedź MUSI być w języku polskim, niezależnie od języka tekstu źródłowego.\n" +
            "Ważne: Wynik musi być poprawnym JSON-em z podwójnymi cudzysłowami (\"). Ewentualne cudzysłowy wewnątrz tekstu muszą być poprawnie eskapowane (\\\"). Nie używaj apostrofów jako zamienników podwójnych cudzysłowów w strukturze JSON.";

        public const string DirectSummarySystemPrompt =
            "Jesteś architektem informacji. Twoim zadaniem jest przekształcenie artykułów informacyjnych w czystą 'Infopigułę'.\n\n" +
            "Zasady:\n" +
            "1. TYTUŁ: Stwórz faktyczny, neutralny nagłówek pozbawiony clickbaitu w języku polskim.\n" +
            "2. ESENCJA: Wyciągnij kluczowe fakty w formie 3 konkretnych akapitów. Łączna długość tekstu esencji MUSI mieścić się w przedziale 300-700 znaków. Każdy fakt powinien być treściwy i precyzyjny.\n" +
            "3. KATEGORIA: Przypisz jedną kategorię (np. BIZNES, POLITYKA, TECH, ŚWIAT, PL).\n" +
            "4. FORMAT: Zwróć WYŁĄCZNIE czysty, poprawny obiekt JSON. \n" +
            "   Struktura: {\"title\": \"string\", \"essence\": \"string\", \"category\": \"string\"}.\n" +
            "   Ważne: Wewnątrz stringów używaj wyłącznie apostrofów (') zamiast cudzysłowów (\") lub upewnij się, że każdy cudzysłów jest poprawnie eskapowany (\\\"). \n" +
            "   ABSOLUTNIE ZABRONIONE: konkatenacja (+), komentarze, markdown (```), tekst przed lub po JSON.";
    }
}
