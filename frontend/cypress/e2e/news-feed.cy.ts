describe('News Feed', () => {
  beforeEach(() => {
    cy.clearLocalStorage();
    cy.intercept('GET', '/api/news/top', {
      body: {
        poland: [
          {
            id: '1',
            title: 'Wiadomość z Polski',
            summaryText: 'To jest streszczenie wiadomości z Polski wygenerowane przez AI.',
            publishedAt: new Date(Date.now() - 1000 * 60 * 30).toISOString(),
            sourceUrls: ['https://tvn24.pl'],
            categoryName: 'Gospodarka',
            region: 0,
            imageUrl: 'https://images.unsplash.com/photo-1611974789855-9c2a0a7236a3?w=800'
          }
        ],
        world: [
          {
            id: '2',
            title: 'News from the World',
            summaryText: 'This is an AI generated summary of a world news article.',
            publishedAt: new Date(Date.now() - 1000 * 60 * 10).toISOString(),
            sourceUrls: ['https://bbc.com'],
            categoryName: 'Świat',
            region: 1,
            imageUrl: 'https://images.unsplash.com/photo-1526778548025-fa2f459cd5c1?w=800'
          }
        ],
        positive: [
          {
            id: '3',
            title: 'Pozytywna wiadomość ze świata',
            summaryText: 'Naukowcy opracowali nową ekologiczną metodę oczyszczania oceanów.',
            publishedAt: new Date(Date.now() - 1000 * 60 * 5).toISOString(),
            sourceUrls: ['https://dobrewiadomosci.net.pl'],
            categoryName: 'Ekologia',
            region: 2,
            imageUrl: 'https://images.unsplash.com/photo-1518709268805-4e9042af9f23?w=800'
          }
        ]
      }
    }).as('getTopNews');
    
    cy.visit('/');
    cy.wait('@getTopNews');
  });

  it('should display the app title', () => {
    cy.contains('InfoSkrót').should('be.visible');
  });

  it('should display Poland, World and Positive options/badges', () => {
    cy.contains('Polska').should('be.visible');
    cy.contains('Świat').should('be.visible');
    cy.contains('Pozytywne').should('be.visible');
  });

  it('should display articles from all regions in default view', () => {
    cy.contains('Wiadomość z Polski').should('be.visible');
    cy.contains('News from the World').should('be.visible');
    cy.contains('Pozytywna wiadomość ze świata').should('be.visible');
  });

  it('should not have leading whitespace in summary text', () => {
    cy.get('.article-summary').first().invoke('text').then((text) => {
      expect(text).to.match(/^\S/); // Starts with non-whitespace character
    });
  });

  it('should filter news when clicking region tabs and persist in localStorage', () => {
    // Click Krajowe tab
    cy.get('.region-tab-poland').click();
    cy.contains('Wiadomość z Polski').should('be.visible');
    cy.contains('News from the World').should('not.exist');
    cy.contains('Pozytywna wiadomość ze świata').should('not.exist');
    cy.getAllLocalStorage().then((storage) => {
      const originStorage = storage[window.location.origin];
      expect(originStorage['syntinfo_region_filter']).to.equal('poland');
    });

    // Click Świat tab
    cy.get('.region-tab-world').click();
    cy.contains('News from the World').should('be.visible');
    cy.contains('Wiadomość z Polski').should('not.exist');
    cy.contains('Pozytywna wiadomość ze świata').should('not.exist');
    cy.getAllLocalStorage().then((storage) => {
      const originStorage = storage[window.location.origin];
      expect(originStorage['syntinfo_region_filter']).to.equal('world');
    });

    // Click Pozytywne tab
    cy.get('.region-tab-positive').click();
    cy.contains('Pozytywna wiadomość ze świata').should('be.visible');
    cy.contains('Wiadomość z Polski').should('not.exist');
    cy.contains('News from the World').should('not.exist');
    cy.getAllLocalStorage().then((storage) => {
      const originStorage = storage[window.location.origin];
      expect(originStorage['syntinfo_region_filter']).to.equal('positive');
    });

    // Return to Wszystkie
    cy.get('.region-tab-all').click();
    cy.contains('Wiadomość z Polski').should('be.visible');
    cy.contains('News from the World').should('be.visible');
    cy.contains('Pozytywna wiadomość ze świata').should('be.visible');
  });

  it('should retain user settings after page reload', () => {
    // 1. Select 'Pozytywne' filter
    cy.get('.region-tab-positive').click();
    cy.contains('Pozytywna wiadomość ze świata').should('be.visible');
    cy.contains('Wiadomość z Polski').should('not.exist');
    cy.get('.region-tab-positive').should('have.attr', 'aria-pressed', 'true');

    // 2. Reload the page
    cy.reload();
    cy.wait('@getTopNews');

    // 3. Verify 'Pozytywne' setting remains active after reload
    cy.get('.region-tab-positive').should('have.attr', 'aria-pressed', 'true');
    cy.contains('Pozytywna wiadomość ze świata').should('be.visible');
    cy.contains('Wiadomość z Polski').should('not.exist');

    // 4. Change setting to 'Świat'
    cy.get('.region-tab-world').click();
    cy.contains('News from the World').should('be.visible');
    cy.contains('Pozytywna wiadomość ze świata').should('not.exist');
    cy.get('.region-tab-world').should('have.attr', 'aria-pressed', 'true');

    // 5. Reload again
    cy.reload();
    cy.wait('@getTopNews');

    // 6. Verify 'Świat' setting remains active after second reload
    cy.get('.region-tab-world').should('have.attr', 'aria-pressed', 'true');
    cy.contains('News from the World').should('be.visible');
    cy.contains('Pozytywna wiadomość ze świata').should('not.exist');
  });

  it('should filter by category when category badge is clicked and allow clearing', () => {
    cy.contains('.category-btn', 'Gospodarka').click();
    cy.get('.active-category-chip').should('be.visible');
    cy.contains('Wiadomość z Polski').should('be.visible');
    cy.contains('News from the World').should('not.exist');
    cy.contains('Pozytywna wiadomość ze świata').should('not.exist');

    // Clear category filter
    cy.get('.clear-category-btn').click();
    cy.get('.active-category-chip').should('not.exist');
    cy.contains('News from the World').should('be.visible');
    cy.contains('Pozytywna wiadomość ze świata').should('be.visible');
  });

  it('should render article in a 2/3 text and 1/3 image layout', () => {
    cy.get('.article-content-col').first().should('have.class', 'md:w-2/3');
    cy.get('.article-image-col').first().should('have.class', 'md:w-1/3');
    cy.get('.article-image').first().should('be.visible');
  });

  it('should apply ecru background style to world news article', () => {
    cy.get('.world-article').should('exist');
    cy.get('.world-article').should('have.class', 'bg-[#faf7f0]');
  });

  it('should apply positive styling and badge to positive news article', () => {
    cy.get('.positive-article').should('exist');
    cy.get('.positive-article').should('have.class', 'bg-[#f4fbf7]');
    cy.get('.region-badge-positive').should('contain', 'Pozytywne');
  });

  describe('Mobile Viewport & Positive News', () => {
    beforeEach(() => {
      // Symulacja smartfona (iPhone X: 375x812)
      cy.viewport(375, 812);
    });

    it('should display filter tabs in a 2x2 grid on mobile screens with left alignment', () => {
      // Kontener powinien mieć klasy siatki 2x2 na mobile
      cy.get('.region-tab-all')
        .parent()
        .should('have.class', 'grid')
        .and('have.class', 'grid-cols-2')
        .and('have.class', 'w-full');

      // Wszystkie 4 przyciski powinny być widoczne i mieć wyrównanie do lewej (justify-start)
      const tabs = [
        '.region-tab-all',
        '.region-tab-poland',
        '.region-tab-world',
        '.region-tab-positive'
      ];

      tabs.forEach((selector) => {
        cy.get(selector)
          .should('be.visible')
          .and('have.class', 'justify-start');
      });

      // Sprawdzenie, że kontener zakładek mieści się w szerokości ekranu i nie powoduje overflow
      cy.get('.region-tab-all')
        .parent()
        .then(($container) => {
          expect($container[0].scrollWidth).to.be.lte($container[0].clientWidth + 1);
        });

      // Sprawdzenie, że cała strona nie posiada poziomego scrolla na mobile
      cy.window().then((win) => {
        expect(win.document.documentElement.scrollWidth).to.be.lte(win.innerWidth + 1);
      });
    });

    it('should filter only positive news when tapped on mobile', () => {
      // Kliknięcie w zakładkę "Pozytywne" w widoku mobilnym
      cy.get('.region-tab-positive').click();

      // Weryfikacja stanu aktywnego przycisku
      cy.get('.region-tab-positive')
        .should('have.attr', 'aria-pressed', 'true')
        .and('have.class', 'bg-aux3')
        .and('have.class', 'text-white');

      // Powinna być widoczna tylko pozytywna wiadomość
      cy.contains('Pozytywna wiadomość ze świata').should('be.visible');
      cy.contains('Wiadomość z Polski').should('not.exist');
      cy.contains('News from the World').should('not.exist');

      // Weryfikacja zapisania filtru w localStorage
      cy.getAllLocalStorage().then((storage) => {
        const originStorage = storage[window.location.origin];
        expect(originStorage['syntinfo_region_filter']).to.equal('positive');
      });

      // Powrót do "Wszystkie" na mobile
      cy.get('.region-tab-all').click();
      cy.contains('Wiadomość z Polski').should('be.visible');
      cy.contains('News from the World').should('be.visible');
      cy.contains('Pozytywna wiadomość ze świata').should('be.visible');
    });
  });

  describe('Source Attribution & AI Transparency', () => {
    it('should display visible source links with domain names and correct attributes', () => {
      // Sekcja Źródło / Źródła
      cy.contains('Źródło:').should('be.visible');

      // Linki do źródeł
      cy.get('.source-link').should('have.length', 3);
      
      // Sprawdzenie linków do serwisów
      cy.contains('.source-link', 'tvn24.pl')
        .should('be.visible')
        .and('have.attr', 'href', 'https://tvn24.pl')
        .and('have.attr', 'target', '_blank')
        .and('have.attr', 'rel', 'noopener noreferrer')
        .and('have.attr', 'itemprop', 'isBasedOn');

      cy.contains('.source-link', 'bbc.com')
        .should('be.visible')
        .and('have.attr', 'href', 'https://bbc.com');

      cy.contains('.source-link', 'dobrewiadomosci.net.pl')
        .should('be.visible')
        .and('have.attr', 'href', 'https://dobrewiadomosci.net.pl');
    });

    it('should include Schema.org NewsArticle semantic microdata on article cards', () => {
      cy.get('article.article-card').first()
        .should('have.attr', 'itemtype', 'https://schema.org/NewsArticle');

      cy.get('article.article-card').first().within(() => {
        cy.get('[itemprop="headline"]').should('be.visible');
        cy.get('[itemprop="description"]').should('be.visible');
        cy.get('[itemprop="datePublished"]').should('exist');
      });
    });

    it('should display AI summary essence badge and transparency disclaimer', () => {
      cy.get('.ai-essence-badge').should('have.length', 3);
      cy.contains('Streszczenie AI').should('be.visible');
      
      // Transparentność w stopce
      cy.contains('Dbamy o transparentność: każde podsumowanie powstaje na bazie oficjalnych kanałów RSS').should('be.visible');
    });
  });
});
