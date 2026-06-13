// Admin realtime: connects to the API's online-payment hub and relays "payment received" events to
// the Blazor toaster component. Token-based (passed from the server-side circuit), camel/Pascal-safe.
window.eemoPaymentHub = {
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

            this.connection.on("OnlinePaymentReceived", function (n) {
                if (!n) return;
                const amount = n.amount ?? n.Amount ?? 0;
                const period = n.period ?? n.Period ?? "";
                const reference = n.reference ?? n.Reference ?? "";
                dotNetRef.invokeMethodAsync("OnPaymentReceived", amount, period, reference);
            });

            await this.connection.start();
            return true;
        } catch (e) {
            console.error("Online-payment hub failed to start:", e);
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
