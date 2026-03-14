window.airwallexInterop = {
    card: null,
    intentId: null,
    clientSecret: null,

    initialize: async function (env, intentId, clientSecret) {
        await window.AirwallexComponentsSDK.init({
            env: env,
            enabledElements: ['payments']
        });

        this.card = await window.AirwallexComponentsSDK.createElement('card');
        this.card.mount('card-element');

        this.intentId = intentId;
        this.clientSecret = clientSecret;

        this.card.on('change', function (event) {
            var displayError = document.getElementById('card-errors');
            if (displayError) {
                if (event.type === 'error') {
                    displayError.textContent = event.error?.message || '';
                } else {
                    displayError.textContent = '';
                }
            }
        });

        return true;
    },

    confirmPayment: async function (cardholderName) {
        if (!this.card || !this.intentId || !this.clientSecret) {
            return { success: false, error: 'Airwallex not initialized' };
        }

        try {
            var response = await this.card.confirm({
                id: this.intentId,
                client_secret: this.clientSecret
            });

            if (response.status === 'SUCCEEDED') {
                var paymentMethod = response.latest_payment_attempt?.payment_method;
                var cardInfo = paymentMethod?.card;

                return {
                    success: true,
                    paymentIntentId: response.id,
                    cardBrand: cardInfo?.brand || '',
                    cardLast4: cardInfo?.last4 || '',
                    cardExpMonth: cardInfo?.expiry_month?.toString().padStart(2, '0') || '',
                    cardExpYear: cardInfo?.expiry_year?.toString() || ''
                };
            }

            return { success: false, error: 'Payment was not successful. Status: ' + response.status };
        } catch (error) {
            return { success: false, error: error.message || 'Payment confirmation failed' };
        }
    },

    destroy: function () {
        if (this.card) {
            this.card.destroy();
            this.card = null;
        }
        this.intentId = null;
        this.clientSecret = null;
    }
};
