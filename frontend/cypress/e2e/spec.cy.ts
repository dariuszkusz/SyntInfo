describe('News Feed', () => {
  it('Visits the initial project page and shows InfoSkrót header', () => {
    // Mocking API call to avoid 500 error in CI where backend is not running
    cy.intercept('GET', '/api/news/top', { 
      body: { poland: [], world: [] } 
    }).as('getTopNews');
    
    cy.visit('/');
    
    cy.contains('InfoSkrót');
    cy.wait('@getTopNews');
  });
});
