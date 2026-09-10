describe('Buy Me a Coffee Button', () => {
  beforeEach(() => {
    cy.intercept('GET', '/api/news/top', {
      body: {
        poland: [],
        world: []
      }
    }).as('getTopNews');

    cy.visit('/');
  });

  it('powinien wyświetlić przycisk Buy Me a Coffee w nagłówku i stopce z poprawnym odnośnikiem', () => {
    cy.get('[data-cy="buy-me-coffee-btn"]')
      .should('have.length.at.least', 1)
      .each(($btn) => {
        cy.wrap($btn)
          .should('be.visible')
          .should('have.attr', 'target', '_blank')
          .should('have.attr', 'rel', 'noopener noreferrer')
          .should('have.attr', 'href', 'https://buymeacoffee.com/dariuszkusz');
      });
  });
});
