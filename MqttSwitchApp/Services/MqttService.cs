using Microsoft.AspNetCore.SignalR;
using MQTTnet;
using System.Text;

namespace MqttSwitchApp.Services
{
    public class MqttService : IHostedService
    {
        private readonly IHubContext<MqttHub> _hubContext;
        private IMqttClient _mqttClient;
        private readonly ILogger<MqttService> _logger;

        public MqttService(IHubContext<MqttHub> hubContext, ILogger<MqttService> logger)
        {
            _hubContext = hubContext;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var factory = new MqttClientFactory();
            _mqttClient = factory.CreateMqttClient();

            var options = new MqttClientOptionsBuilder()
                .WithTcpServer("localhost", 1883) // Пример брокера
                .Build();

            _mqttClient.ConnectedAsync += async e =>
            {
                _logger.LogInformation("Connected to MQTT broker");
                await _mqttClient.SubscribeAsync(new MqttTopicFilterBuilder()
                    .WithTopic("test/topic")
                    .Build());
            };

            _mqttClient.DisconnectedAsync += async e =>
            {
                _logger.LogInformation("Disconnected from MQTT broker");
                await Task.CompletedTask;
            };

            _mqttClient.ApplicationMessageReceivedAsync += async e =>
            {
                var message = System.Text.Encoding.UTF8.GetString(e.ApplicationMessage.Payload);
                await _hubContext.Clients.All.SendAsync("ReceiveMqttData", message);
            };

            await _mqttClient.ConnectAsync(options, cancellationToken);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await _mqttClient.DisconnectAsync();
        }
    }
}
