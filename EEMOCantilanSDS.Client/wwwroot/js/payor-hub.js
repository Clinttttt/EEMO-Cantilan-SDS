// Payor portal realtime: connects the signed-in payor to their per-payor hub and relays the
// "Official Receipt issued" event to the Blazor toaster. Token-based (the payor JWT from the circuit).
window.eemoPayorHub = {
    connection: null,

    start: async function (hubUrl, token, dotNetRef) {
        if (typeof signalR === "undefined") {
            console.warn("SignalR client library not loaded.");
            return false;
        }
        try {
            if (this.connection) {
                try { await this.connection.stop(); } catch { /* ignore */ }
            }

            this.connection = new signalR.HubConnectionBuilder()
                .withUrl(hubUrl, { accessTokenFactory: () => token })
                .withAutomaticReconnect()
                .build();

            this.connection.on("OnlinePaymentOrIssued", function (n) {
                if (!n) return;
                const orNumber = n.orNumber ?? n.OrNumber ?? "";
                const period = n.period ?? n.Period ?? "";
                const amount = n.amount ?? n.Amount ?? 0;
                const reference = n.reference ?? n.Reference ?? "";
                dotNetRef.invokeMethodAsync("OnOrIssued", orNumber, period, amount, reference);
            });

            await this.connection.start();
            return true;
        } catch (e) {
            console.error("Payor hub failed to start:", e);
            return false;
        }
    },

    stop: async function () {
        if (this.connection) {
            try { await this.connection.stop(); } catch { /* ignore */ }
            this.connection = null;
        }
    }
};
