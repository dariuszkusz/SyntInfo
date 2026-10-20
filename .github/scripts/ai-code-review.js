const fs = require('fs');
const { execSync } = require('child_process');

async function run() {
  try {
    const apiKey = process.env.GEMINI_API_KEY;
    const githubToken = process.env.GITHUB_TOKEN;
    const eventPath = process.env.GITHUB_EVENT_PATH;
    const repo = process.env.GITHUB_REPOSITORY;

    if (!apiKey) {
      console.warn('⚠️ Warning: GEMINI_API_KEY is not defined in repository secrets. Skipping AI Code Review.');
      process.exit(0);
    }

    if (!githubToken || !eventPath || !repo) {
      console.error('❌ Error: Missing required GitHub Actions environment variables (GITHUB_TOKEN, GITHUB_EVENT_PATH, GITHUB_REPOSITORY).');
      process.exit(1);
    }

    const eventData = JSON.parse(fs.readFileSync(eventPath, 'utf8'));
    const pullRequest = eventData.pull_request;

    if (!pullRequest) {
      console.log('ℹ️ No pull_request object found in event payload. Exiting.');
      return;
    }

    const prNumber = pullRequest.number;
    const baseRef = pullRequest.base?.ref || 'develop';
    console.log(`🔍 Analyzing PR #${prNumber} comparing with base branch '${baseRef}'...`);

    // Fetch base branch to compare diff correctly
    try {
      execSync(`git fetch origin ${baseRef} --depth=100`, { stdio: 'inherit' });
    } catch (e) {
      console.log('Fetch base branch warning, proceeding...');
    }

    // Get git diff
    let diff = '';
    try {
      diff = execSync(`git diff origin/${baseRef}...HEAD`, { encoding: 'utf8', maxBuffer: 10 * 1024 * 1024 });
    } catch (e) {
      console.log('Fallback to git diff HEAD~1...');
      diff = execSync(`git diff HEAD~1`, { encoding: 'utf8', maxBuffer: 10 * 1024 * 1024 });
    }

    if (!diff || diff.trim() === '') {
      console.log('ℹ️ No git diff detected in PR. Exiting.');
      return;
    }

    // Filter out unwanted lockfiles and binaries from diff
    const filteredDiff = filterDiff(diff);
    if (!filteredDiff || filteredDiff.trim() === '') {
      console.log('ℹ️ No relevant code diff after filtering lockfiles and binary assets.');
      return;
    }

    // Truncate if diff is too long
    const MAX_DIFF_LENGTH = 70000;
    let finalDiff = filteredDiff;
    if (filteredDiff.length > MAX_DIFF_LENGTH) {
      finalDiff = filteredDiff.substring(0, MAX_DIFF_LENGTH) + '\n\n...[Diff truncated due to size limit]...';
    }

    console.log(`📤 Sending diff of length ${finalDiff.length} to Google Gemini API...`);

    const reviewMarkdown = await callGeminiApi(apiKey, finalDiff, pullRequest);

    console.log('💬 Posting review comment to GitHub PR...');
    await postOrUpdateComment(repo, prNumber, githubToken, reviewMarkdown);

    console.log('✅ AI Code Review completed successfully!');
  } catch (error) {
    console.error('❌ AI Code Review failed:', error);
    process.exit(1);
  }
}

function filterDiff(fullDiff) {
  const fileDiffs = fullDiff.split(/^diff --git /m);
  const ignoredPatterns = [
    'package-lock.json',
    'pnpm-lock.yaml',
    'yarn.lock',
    'dist/',
    'bin/',
    'obj/',
    '.min.js',
    '.min.css',
    '.png',
    '.jpg',
    '.jpeg',
    '.svg',
    '.ico',
    '.webp',
    '.woff',
    '.woff2',
    '.eot',
    '.ttf',
    '.map'
  ];

  const validDiffs = fileDiffs.filter(block => {
    if (!block.trim()) return false;
    const firstLine = block.split('\n')[0];
    return !ignoredPatterns.some(pattern => firstLine.includes(pattern));
  });

  return validDiffs.join('diff --git ');
}

async function callGeminiApi(apiKey, diffContent, pr) {
  const models = ['gemini-3.8-flash', 'gemini-3.8-flash-lite', 'gemini-3.8-pro', 'gemini-2.5-flash'];
  const systemInstruction = `Jesteś doświadczonym Senior Architektem i ekspertem ds. Code Review w zespole programistycznym SyntInfo (.NET, C#, Angular, TypeScript).
Twoim zadaniem jest przeprowadzenie rzetelnego, konstruktywnego i profesjonalnego przeglądu kodu Pull Requesta (PR).

Zasady i standardy projektu SyntInfo, których musisz bezwzględnie pilnować:
.NET & Backend:
1. Wersja C#: C# 10 / .NET 10.
2. Architektura: Clean Architecture & standardy S.O.L.I.D.
3. Wzorce projektowe: CQRS (separacja Commands/Queries) oraz Unit of Work Pattern.
4. Zadania w tle: Quartz.NET.
5. Baza danych: Entity Framework Core Code First (migracje, konfiguracje Fluent API).
6. Podział: .NET (Backend) i Angular (Frontend) muszą pozostać oddzielnymi projektami.

Angular & Frontend:
1. Zarządzanie stanem: NgRx (Store, Actions, Reducers, Effects / Signals).
2. Testy: Tworzenie testów w Cypress do każdej nowej funkcjonalności i komponentu.
3. Standard PWA: Aplikacja musi zachowywać cechy Progressive Web App.
4. Stylistyka i UI: Tailwind CSS, styl ściśle minimalistyczny, czysty i nowoczesny.

Jakość ogólna:
- Bezpieczeństwo (brak wycieków sekretów/kluczy, walidacja wejścia, brak podatności SQLi/XSS).
- Wydajność i optymalizacja zapytań (async/await, brak problemów N+1, unikanie niepotrzebnych re-renderów).
- Obsługa błędów, logowanie i czystość kodu (czytelne nazewnictwo, brak zbędnego/martwego kodu).

Format odpowiedzi (użyj czytelnego formatu Markdown w języku polskim):
## 🤖 AI Code Review (SyntInfo) - Podsumowanie
- **Ocena ogólna:** 🟢 Zatwierdzono / 🟡 Wymaga drobnych poprawek / 🔴 Wymaga istotnych zmian
- **Krótkie podsumowanie zmian:** (1-2 zwięzłe zdania)

### 📊 Weryfikacja Standardów Projektowych
| Kryterium | Status | Uwagi |
| --- | --- | --- |
| .NET / Clean Architecture & S.O.L.I.D | 🟢 / 🟡 / 🔴 / ⚪ N/A | ... |
| CQRS & Unit of Work | 🟢 / 🟡 / 🔴 / ⚪ N/A | ... |
| Baza danych Code First | 🟢 / 🟡 / 🔴 / ⚪ N/A | ... |
| Background Jobs (Quartz.NET) | 🟢 / 🟡 / 🔴 / ⚪ N/A | ... |
| Angular & NgRx | 🟢 / 🟡 / 🔴 / ⚪ N/A | ... |
| Testy Cypress | 🟢 / 🟡 / 🔴 / ⚪ N/A | ... |
| PWA & Styl Minimalistyczny (Tailwind) | 🟢 / 🟡 / 🔴 / ⚪ N/A | ... |
| Bezpieczeństwo i Wydajność | 🟢 / 🟡 / 🔴 | ... |

### 🔍 Szczegółowe Uwagi i Rekomendacje
(Wskaż konkretne fragmenty kodu, pliki lub linie z propozycją ulepszeń lub pochwałami. Używaj bloków kodu \`\`\`csharp / \`\`\`typescript / \`\`\`html gdy sugerujesz zamienniki).

### 💡 Dobre praktyki i Refaktoring
(Krótko wskaż co zostało zrobione dobrze oraz ewentualne sugestie dalszego rozwoju).
`;

  const userPrompt = `Tytuł PR: ${pr.title}\nOpis PR: ${pr.body || 'Brak opisu'}\n\nOto Git Diff zmian do przeglądu:\n\`\`\`diff\n${diffContent}\n\`\`\``;

  for (const model of models) {
    try {
      console.log(`Attempting Gemini model: ${model}...`);
      const url = `https://generativelanguage.googleapis.com/v1beta/models/${model}:generateContent?key=${apiKey}`;
      const res = await fetch(url, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'x-goog-api-key': apiKey
        },
        body: JSON.stringify({
          contents: [{ role: 'user', parts: [{ text: userPrompt }] }],
          system_instruction: { parts: [{ text: systemInstruction }] },
          generationConfig: {
            temperature: 0.2,
            maxOutputTokens: 3500
          }
        })
      });

      if (!res.ok) {
        const errText = await res.text();
        console.warn(`Model ${model} returned HTTP ${res.status}: ${errText}`);
        continue;
      }

      const data = await res.json();
      const candidate = data.candidates?.[0];
      const parts = candidate?.content?.parts;
      if (Array.isArray(parts)) {
        // Filter out thought parts if thinking is returned by Gemini 3.8
        const textParts = parts.filter(p => !p.thought && typeof p.text === 'string').map(p => p.text);
        const text = textParts.length > 0 ? textParts.join('\n') : parts[0]?.text;
        if (text) {
          return text;
        }
      }
    } catch (e) {
      console.warn(`Error invoking model ${model}:`, e.message);
    }
  }

  throw new Error('All Gemini API model calls failed.');
}

async function postOrUpdateComment(repo, prNumber, token, markdownContent) {
  const commentHeader = '<!-- AI_CODE_REVIEW_COMMENT -->';
  const fullCommentBody = `${commentHeader}\n${markdownContent}\n\n---\n*Wygenerowano automatycznie przez Google Gemini AI Code Review.*`;

  const baseUrl = `https://api.github.com/repos/${repo}`;
  const headers = {
    'Authorization': `Bearer ${token}`,
    'Accept': 'application/vnd.github.v3+json',
    'User-Agent': 'Gemini-AI-Code-Review-Action'
  };

  // Check for existing review comment
  const commentsRes = await fetch(`${baseUrl}/issues/${prNumber}/comments`, { headers });
  if (!commentsRes.ok) {
    throw new Error(`Failed to fetch PR comments: ${commentsRes.statusText}`);
  }

  const comments = await commentsRes.json();
  const existingComment = comments.find(c => c.body && c.body.includes(commentHeader));

  if (existingComment) {
    console.log(`Updating existing AI review comment ID #${existingComment.id}...`);
    const updateRes = await fetch(`${baseUrl}/issues/comments/${existingComment.id}`, {
      method: 'PATCH',
      headers: { ...headers, 'Content-Type': 'application/json' },
      body: JSON.stringify({ body: fullCommentBody })
    });
    if (!updateRes.ok) {
      throw new Error(`Failed to update comment: ${updateRes.statusText}`);
    }
  } else {
    console.log('Creating new AI review comment...');
    const createRes = await fetch(`${baseUrl}/issues/${prNumber}/comments`, {
      method: 'POST',
      headers: { ...headers, 'Content-Type': 'application/json' },
      body: JSON.stringify({ body: fullCommentBody })
    });
    if (!createRes.ok) {
      throw new Error(`Failed to create comment: ${createRes.statusText}`);
    }
  }
}

run();
