window.AppConfig = {
    environment: window.location.hostname === 'localhost' ? 'Development' : 'Production',

    api: {
        baseUrl: '',
        timeout: 30000
    },

    signalR: {
        notificationHub: '/notificationHub',
        options: {
            transport: 1 | 2 | 4, // WebSockets | ServerSentEvents | LongPolling
            withCredentials: true
        }
    },

    getApiBaseUrl: function () {
        if (this.environment === 'Development') {
            return 'http://localhost:5000'; // Use your API gateway URL
        }
        return 'https://inventory166.az';
    },

    buildApiUrl: function (endpoint) {
        endpoint = endpoint.replace(/^\//, '');
        const baseUrl = this.getApiBaseUrl();
        return `${baseUrl}/api/${endpoint}`;
    },

    getApiGatewayUrl: function () {
        return this.getApiBaseUrl(); // Reuse the same logic
    }
};