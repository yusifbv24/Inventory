window.AppConfig = {
    environment: (window.ASPNETCORE_ENVIRONMENT || "Production"),

    api: {
        baseUrl: window.location.origin,
        timeout: 30000
    },

    signalR: {
        notificationHub: window.location.origin + '/notificationHub',
        options: {
            transport: 1 | 2 | 4, // WebSockets | ServerSentEvents | LongPolling
            withCredentials: true
        }
    },

    buildApiUrl: function (endpoint) {
        const baseUrl = this.api.baseUrl;
        return `${baseUrl}/api/${endpoint}`;
    }
};