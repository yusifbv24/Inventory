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

    buildApiUrl: function (endpoint) {
        endpoint = endpoint.replace(/^\//, '');
        return `/api/${endpoint}`;
    },

    getApiGatewayUrl: function () {
        if (this.environment === 'Development') {
            return 'http://localhost:5000';
        }
        return 'https://inventory166.az';
    }
};