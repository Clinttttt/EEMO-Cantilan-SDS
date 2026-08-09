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
                // Retry for as long as the page is open. The default schedule gives up after about thirty seconds
                // and never tries again, which on a phone means a payor who walks through a dead spot has a page
                // that looks live and is not.
                .withAutomaticReconnect({
                    nextRetryDelayInMilliseconds: ctx => {
                        switch (ctx.previousRetryCount) {
                            case 0: return 0;
                            case 1: return 2000;
                            case 2: return 5000;
                            case 3: return 10000;
                            default: return 30000;   // then every 30s, indefinitely
                        }
                    }
                })
                .build();

            this.connection.on("OnlinePaymentOrIssued", function (n) {
                if (!n) return;
                const orNumber = n.orNumber ?? n.OrNumber ?? "";
                const period = n.period ?? n.Period ?? "";
                const amount = n.amount ?? n.Amount ?? 0;
                const reference = n.reference ?? n.Reference ?? "";
                dotNetRef.invokeMethodAsync("OnOrIssued", orNumber, period, amount, reference);
            });

            // A receipt encoded while the connection was down was never delivered - SignalR does not queue for an
            // absent client - so the payor would go on seeing a provisional receipt indefinitely. The reconnect is
            // therefore reported as an instruction to re-read rather than treated as nothing having happened.
            this.connection.onreconnected(function () {
                dotNetRef.invokeMethodAsync("OnConnectionRestored");
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
