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
        ]
      }
    }).as('getTopNews');
    
    cy.visit('/');
    cy.wait('@getTopNews');
  });

  it('should display the app title', () => {
    cy.contains('InfoSkrót').should('be.visible');
  });

  it('should display Poland and World options/badges', () => {
    cy.contains('Polska').should('be.visible');
    cy.contains('Świat').should('be.visible');
  });

  it('should display articles from both regions in default view', () => {
    cy.contains('Wiadomość z Polski').should('be.visible');
    cy.contains('News from the World').should('be.visible');
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
    cy.getAllLocalStorage().then((storage) => {
      const originStorage = storage[window.location.origin];
      expect(originStorage['syntinfo_region_filter']).to.equal('poland');
    });

    // Click Świat tab
    cy.get('.region-tab-world').click();
    cy.contains('News from the World').should('be.visible');
    cy.contains('Wiadomość z Polski').should('not.exist');
    cy.getAllLocalStorage().then((storage) => {
      const originStorage = storage[window.location.origin];
      expect(originStorage['syntinfo_region_filter']).to.equal('world');
    });

    // Return to Wszystkie
    cy.get('.region-tab-all').click();
    cy.contains('Wiadomość z Polski').should('be.visible');
    cy.contains('News from the World').should('be.visible');
  });

  it('should filter by category when category badge is clicked and allow clearing', () => {
    cy.contains('.category-btn', 'Gospodarka').click();
    cy.get('.active-category-chip').should('be.visible');
    cy.contains('Wiadomość z Polski').should('be.visible');
    cy.contains('News from the World').should('not.exist');

    // Clear category filter
    cy.get('.clear-category-btn').click();
    cy.get('.active-category-chip').should('not.exist');
    cy.contains('News from the World').should('be.visible');
  });

  it('should display article image', () => {
    cy.get('.article-image').should('have.length.at.least', 1);
    cy.get('.article-image').first().should('be.visible');
  });

  it('should apply ecru background style to world news article', () => {
    cy.get('.world-article').should('exist');
    cy.get('.world-article').should('have.class', 'bg-[#faf7f0]');
  });
});
