describe('News Feed', () => {
  beforeEach(() => {
    // We might need to mock the API for testing the UI
    cy.intercept('GET', '/api/news/top', {
      body: {
        poland: [
          {
            id: '1',
            title: 'Wiadomość z Polski',
            summaryText: 'To jest streszczenie wiadomości z Polski wygenerowane przez AI.',
            publishedAt: new Date().toISOString(),
            sourceUrls: ['https://tvn24.pl'],
            categoryName: 'Wiadomości',
            region: 0
          }
        ],
        world: [
          {
            id: '2',
            title: 'News from the World',
            summaryText: 'This is an AI generated summary of a world news article.',
            publishedAt: new Date().toISOString(),
            sourceUrls: ['https://bbc.com'],
            categoryName: 'World',
            region: 1
          }
        ]
      }
    }).as('getTopNews');
    
    cy.visit('/');
  });

  it('should display the app title', () => {
    cy.contains('SyntInfo').should('be.visible');
  });

  it('should display Poland and World sections', () => {
    cy.wait('@getTopNews');
    cy.contains('Polska').should('be.visible');
    cy.contains('Świat').should('be.visible');
  });

  it('should display articles in both sections', () => {
    cy.wait('@getTopNews');
    cy.contains('Wiadomość z Polski').should('be.visible');
    cy.contains('News from the World').should('be.visible');
  });


  it('should show refresh button (⚡)', () => {
    cy.get('button').contains('⚡').should('be.visible');
  });
});
