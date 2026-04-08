describe('News Feed', () => {
  it('Visits the initial project page and shows SyntInfo header', () => {
    // Mocking API call to avoid 500 error in CI where backend is not running
    cy.intercept('GET', '/api/news/top', { 
      body: { poland: [], world: [] } 
    }).as('getTopNews');
    
    cy.visit('/');
    
    cy.contains('SyntInfo');
    cy.wait('@getTopNews');
  });
});
