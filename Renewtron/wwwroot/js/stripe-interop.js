window.stripeInterop = {
    stripe: null,
    elements: null,
    cardElement: null,

    initialize: function (publishableKey) {
        this.stripe = Stripe(publishableKey);
        this.elements = this.stripe.elements();

        const style = {
            base: {
                fontSize: '16px',
                color: '#1f2937',
                fontFamily: 'Inter, system-ui, sans-serif',
                '::placeholder': {
                    color: '#9ca3af',
                },
                padding: '12px',
            },
            invalid: {
                color: '#dc2626',
                iconColor: '#dc2626',
            },
        };

        this.cardElement = this.elements.create('card', {
            style: style,
            hidePostalCode: true
        });
        this.cardElement.mount('#card-element');

        this.cardElement.on('change', function (event) {
            const displayError = document.getElementById('card-errors');
            if (event.error) {
                displayError.textContent = event.error.message;
            } else {
                displayError.textContent = '';
            }
        });

        return true;
    },

    createPaymentMethod: async function (cardholderName) {
        if (!this.stripe || !this.cardElement) {
            return { success: false, error: 'Stripe not initialized' };
        }

        const { paymentMethod, error } = await this.stripe.createPaymentMethod({
            type: 'card',
            card: this.cardElement,
            billing_details: {
                name: cardholderName,
            },
        });

        if (error) {
            return { success: false, error: error.message };
        }

        return {
            success: true,
            paymentMethodId: paymentMethod.id,
            cardBrand: paymentMethod.card.brand,
            cardLast4: paymentMethod.card.last4,
            cardExpMonth: paymentMethod.card.exp_month.toString().padStart(2, '0'),
            cardExpYear: paymentMethod.card.exp_year.toString()
        };
    },

    destroy: function () {
        if (this.cardElement) {
            this.cardElement.destroy();
            this.cardElement = null;
        }
        this.elements = null;
        this.stripe = null;
    }
};
