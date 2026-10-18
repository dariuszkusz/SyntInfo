describe('Newsletter Subscription & Unsubscription', () => {
  beforeEach(() => {
    cy.clearLocalStorage();
    cy.intercept('GET', '/api/news/top', {
      body: {
        poland: [],
        world: []
      }
    }).as('getTopNews');

    cy.visit('/');
    cy.wait('@getTopNews');
  });

  it('powinien wyświetlić sekcję newslettera z polem e-mail i przyciskiem', () => {
    cy.get('[data-cy="newsletter-section"]').should('be.visible');
    cy.get('[data-cy="newsletter-email-input"]')
      .should('be.visible')
      .and('have.attr', 'placeholder', 'twoj@email.pl');
    cy.get('[data-cy="newsletter-submit-btn"]')
      .should('be.visible')
      .and('contain.text', 'Zapisz się');
  });

  it('powinien pomyślnie zapisać użytkownika do newslettera', () => {
    cy.intercept('POST', '/api/newsletter/subscribe', {
      statusCode: 200,
      body: {
        success: true,
        message: 'Dziękujemy! Pomyślnie zapisano do newslettera.'
      }
    }).as('subscribeRequest');

    cy.get('[data-cy="newsletter-email-input"]').type('czytelnik@syntinfo.local');
    cy.get('[data-cy="newsletter-submit-btn"]').click();

    cy.wait('@subscribeRequest').its('request.body').should('deep.include', {
      email: 'czytelnik@syntinfo.local'
    });

    cy.get('[data-cy="newsletter-success-msg"]')
      .should('be.visible')
      .and('contain.text', 'Dziękujemy! Pomyślnie zapisano do newslettera.');
  });

  it('powinien wyświetlić błąd, gdy serwer zwróci błąd zapisu', () => {
    cy.intercept('POST', '/api/newsletter/subscribe', {
      statusCode: 400,
      body: {
        success: false,
        message: 'Podaj poprawny adres e-mail.'
      }
    }).as('subscribeError');

    cy.get('[data-cy="newsletter-email-input"]').type('invalid-email');
    cy.get('[data-cy="newsletter-submit-btn"]').click();

    cy.wait('@subscribeError');
    cy.get('[data-cy="newsletter-error-msg"]')
      .should('be.visible')
      .and('contain.text', 'Podaj poprawny adres e-mail.');
  });

  it('powinien pozwolić na przełączenie w tryb wypisania i pomyślny wypis z newslettera', () => {
    cy.intercept('POST', '/api/newsletter/unsubscribe', {
      statusCode: 200,
      body: {
        success: true,
        message: 'Twój adres e-mail został pomyślnie wypisany z newslettera.'
      }
    }).as('unsubscribeRequest');

    // Kliknięcie przełącznika trybu
    cy.get('[data-cy="newsletter-mode-toggle"]').click();

    cy.get('[data-cy="newsletter-section"]').should('contain.text', 'Wypisz się z newslettera');
    cy.get('[data-cy="newsletter-submit-btn"]').should('contain.text', 'Wypisz się');

    cy.get('[data-cy="newsletter-email-input"]').type('czytelnik@syntinfo.local');
    cy.get('[data-cy="newsletter-submit-btn"]').click();

    cy.wait('@unsubscribeRequest').its('request.body').should('deep.include', {
      email: 'czytelnik@syntinfo.local'
    });

    cy.get('[data-cy="newsletter-success-msg"]')
      .should('be.visible')
      .and('contain.text', 'Twój adres e-mail został pomyślnie wypisany z newslettera.');
  });
});
