﻿using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NotificationService.Application.DTOs;
using NotificationService.Application.Interfaces;
using NotificationService.Application.Services;
using NotificationService.Domain.Entities;
using NotificationService.Domain.Events;
using NotificationService.Domain.Repositories;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace NotificationService.Infrastructure.Services
{
    public class RabbitMQConsumer : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<RabbitMQConsumer> _logger;
        private readonly IConnection _connection;
        private readonly IModel _channel;
        private readonly string _queueName = "notification-queue";

        public RabbitMQConsumer(IServiceProvider serviceProvider, IConfiguration configuration, ILogger<RabbitMQConsumer> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;

            var factory = new ConnectionFactory
            {
                HostName = configuration["RabbitMQ:HostName"] ?? "localhost",
                UserName = configuration["RabbitMQ:UserName"] ?? "guest",
                Password = configuration["RabbitMQ:Password"] ?? "guest",
                DispatchConsumersAsync = true,
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
            };

            _connection = factory.CreateConnection("NotificationService");
            _channel = _connection.CreateModel();

            // Set QoS
            _channel.BasicQos(prefetchSize:0, prefetchCount: 1, global: false);


            // Declare exchange and queue
            _channel.ExchangeDeclare("inventory-events", ExchangeType.Topic, durable: true);
            _channel.QueueDeclare(_queueName, durable: true, exclusive: false, autoDelete: false);


            // Bind all event types we want to listen to
            _channel.QueueBind(_queueName, "inventory-events", "approval.request.created");
            _channel.QueueBind(_queueName, "inventory-events", "approval.request.processed");
            _channel.QueueBind(_queueName, "inventory-events", "product.created");
            _channel.QueueBind(_queueName, "inventory-events", "product.deleted");
            _channel.QueueBind(_queueName, "inventory-events", "product.updated");
            _channel.QueueBind(_queueName, "inventory-events", "route.created");
            _channel.QueueBind(_queueName, "inventory-events", "route.completed");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("RabbitMQ Consumer starting...");

            var consumer = new AsyncEventingBasicConsumer(_channel);

            consumer.Received += async (sender, ea) =>
            {
                try
                {
                    var body = ea.Body.ToArray();
                    var message = Encoding.UTF8.GetString(body);
                    var routingKey = ea.RoutingKey;

                    _logger.LogInformation($"=== RECEIVED MESSAGE ===");
                    _logger.LogInformation($"RoutingKey: {routingKey}");
                    _logger.LogInformation($"Message: {message}");
                    _logger.LogInformation($"======================");

                    // Handle different event types based on routing key
                    await ProcessMessage(routingKey, message);

                    _channel.BasicAck(ea.DeliveryTag, false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing message");
                    _channel.BasicNack(ea.DeliveryTag, false, true);
                }
            };

            _channel.BasicConsume(_queueName, false, consumer);

            // Keep the service running
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken);
            }
        }

        private async Task ProcessMessage(string routingKey, string message)
        {
            switch (routingKey)
            {
                case "approval.request.created":
                    await HandleApprovalRequestCreated(message);
                    break;
                case "approval.request.processed":
                    await HandleApprovalRequestProcessed(message);
                    break;
                case "product.created":
                    await HandleProductCreated(message);
                    break;
                case "product.deleted":
                    await HandleProductDeleted(message);
                    break;
                case "route.created":
                    await HandleRouteCreated(message);
                    break;
                case "route.completed":
                    await HandleRouteCompleted(message);
                    break;
                default:
                    _logger.LogWarning($"Unknown routing key: {routingKey}");
                    break;
            }
        }

        private async Task HandleApprovalRequestCreated(string message)
        {
            try
            {
                var approvalEvent = JsonSerializer.Deserialize<ApprovalRequestCreatedEvent>(message);
                if (approvalEvent == null) return;

                _logger.LogInformation($"Processing approval request from {approvalEvent.RequestedByName}");

                // Get all admin users
                using var scope = _serviceProvider.CreateScope();
                var userService = scope.ServiceProvider.GetRequiredService<IUserService>();
                var adminUsers = await userService.GetUsersAsync("Admin");
                _logger.LogInformation($"Found {adminUsers.Count} admin users to notify");

                var readableType = GetReadableRequestType(approvalEvent.RequestType);
                var actionDescription = GetActionDescription(approvalEvent.RequestType);

                foreach (var admin in adminUsers)
                {
                    var notification = new Notification(
                        admin.Id,
                        "ApprovalRequest",
                        $"New {readableType} Request",
                        $"{approvalEvent.RequestedByName} has requested to {actionDescription}. Request #{approvalEvent.RequestId} needs your approval.",
                        JsonSerializer.Serialize(new
                        {
                            approvalRequestId = approvalEvent.RequestId,
                            requestType = approvalEvent.RequestType,
                            requestedBy = approvalEvent.RequestedByName
                        })
                    );

                    await SaveAndSendNotification(notification);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling approval requested event");
            }
        }

        private async Task HandleApprovalRequestProcessed(string message)
        {
            try
            {
                var approvalEvent = JsonSerializer.Deserialize<ApprovalRequestProcessedEvent>(message);
                if (approvalEvent == null) return;

                _logger.LogInformation($"Processing approval response for user {approvalEvent.RequestedById}");

                var readableType = GetReadableRequestType(approvalEvent.RequestType);
                var actionDescription = GetActionDescription(approvalEvent.RequestType);

                string title;
                string messageText;

                if (approvalEvent.Status == "Approved" || approvalEvent.Status == "Executed")
                {
                    title = "Request Approved ✓";
                    messageText = $"Your request to {actionDescription} (Request #{approvalEvent.RequestId}) has been approved by {approvalEvent.ProcessedByName}.";
                }
                else if (approvalEvent.Status == "Rejected")
                {
                    title = "Request Rejected ✗";
                    messageText = $"Your request to {actionDescription} (Request #{approvalEvent.RequestId}) has been rejected by {approvalEvent.ProcessedByName}.";
                    if (!string.IsNullOrEmpty(approvalEvent.RejectionReason))
                    {
                        messageText += $" Reason: {approvalEvent.RejectionReason}";
                    }
                }
                else if (approvalEvent.Status == "Failed")
                {
                    title = "Request Failed ⚠";
                    messageText = $"Your request to {actionDescription} (Request #{approvalEvent.RequestId}) was approved but failed to execute.";
                    if (!string.IsNullOrEmpty(approvalEvent.RejectionReason))
                    {
                        messageText += $" Error: {approvalEvent.RejectionReason}";
                    }
                }
                else
                {
                    title = "Request Updated";
                    messageText = $"Your {readableType} request (#{approvalEvent.RequestId}) status has been updated to: {approvalEvent.Status}";
                }

                var notification = new Notification(
                    approvalEvent.RequestedById,
                    "ApprovalResponse",
                    title,
                    messageText,
                    JsonSerializer.Serialize(new
                    {
                        approvalRequestId = approvalEvent.RequestId,
                        status = approvalEvent.Status,
                        processedBy = approvalEvent.ProcessedByName
                    })
                );

                await SaveAndSendNotification(notification);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling approval processed event");
            }
        }

        private async Task HandleProductCreated(string message)
        {
            try
            {
                var productEvent = JsonSerializer.Deserialize<ProductCreatedEvent>(message);
                if (productEvent == null) return;

                _logger.LogInformation($"Product created: {productEvent.Model} ({productEvent.InventoryCode})");

                var allUsers = await GetAllUsersId();

                foreach (var userId in allUsers)
                {
                    var notification = new Notification(
                        userId,
                        "ProductUpdate",
                        "New Product Added",
                        $"Product {productEvent.Model} by {productEvent.Vendor} (Code: {productEvent.InventoryCode}) has been added to {productEvent.DepartmentName}",
                        JsonSerializer.Serialize(new
                        {
                            productId = productEvent.ProductId,
                            inventoryCode = productEvent.InventoryCode,
                            model = productEvent.Model
                        })
                    );

                    await SaveAndSendNotification(notification);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling product created event");
            }
        }

        private async Task HandleProductDeleted(string message)
        {
            try
            {
                var productEvent = JsonSerializer.Deserialize<ProductDeletedEvent>(message);
                if (productEvent == null) return;

                _logger.LogInformation($"Product deleted: {productEvent.Model} ({productEvent.InventoryCode})");

                // Notify relevant users
                var allUsers = await GetAllUsersId();

                foreach (var userId in allUsers)
                {
                    var notification = new Notification(
                        userId,
                        "ProductUpdate",
                        "Product Deleted",
                        $"Product {productEvent.Model} (Code: {productEvent.InventoryCode}) has been deleted from {productEvent.DepartmentName}",
                        JsonSerializer.Serialize(new
                        {
                            productId = productEvent.ProductId,
                            inventoryCode = productEvent.InventoryCode,
                            departmentName=productEvent.DepartmentName
                        })
                    );

                    await SaveAndSendNotification(notification);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling product deleted event");
            }
        }

        private async Task HandleRouteCreated(string message)
        {
            try
            {
                var routeEvent = JsonSerializer.Deserialize<RouteCreatedEvent>(message);
                if (routeEvent == null) return;

                _logger.LogInformation($"Route created for product {routeEvent.Model} to {routeEvent.ToDepartmentName}");

                // Notify destination department
                var allUsers = await GetAllUsersId();

                foreach (var userId in allUsers)
                {
                    var notification = new Notification(
                        userId,
                        "RouteUpdate",
                        "Incoming Product Transfer",
                        $"Product {routeEvent.Model} (Code: {routeEvent.InventoryCode}) is being transferred to {routeEvent.ToDepartmentName}",
                        JsonSerializer.Serialize(new
                        {
                            routeId = routeEvent.RouteId,
                            productId = routeEvent.ProductId
                        })
                    );

                    await SaveAndSendNotification(notification);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling route created event");
            }
        }

        private async Task HandleRouteCompleted(string message)
        {
            try
            {
                var routeEvent = JsonSerializer.Deserialize<RouteCompletedEvent>(message);
                if (routeEvent == null) return;

                _logger.LogInformation($"Route completed: {routeEvent.RouteId}");

                // Notify relevant users
                var allUsers = await GetAllUsersId();

                foreach (var userId in allUsers)
                {
                    var notification = new Notification(
                        userId,
                        "RouteUpdate",
                        "Transfer Completed",
                        $"Product {routeEvent.Model} (Code: {routeEvent.InventoryCode}) transfer to {routeEvent.ToDepartmentName} has been completed",
                        JsonSerializer.Serialize(new
                        {
                            routeId = routeEvent.RouteId,
                            productId = routeEvent.ProductId
                        })
                    );

                    await SaveAndSendNotification(notification);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling route completed event");
            }
        }

        private async Task SaveAndSendNotification(Notification notification)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<INotificationRepository>();
                var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<NotificationHub>>();

                await repository.AddAsync(notification);
                await unitOfWork.SaveChangesAsync();

                _logger.LogInformation($"Notification saved to database for user {notification.UserId}");

                // Send via SignalR
                var groupName = $"user-{notification.UserId}";
                _logger.LogInformation($"Sending notification to SignalR group: {groupName}");

                await hubContext.Clients.Group(groupName).SendAsync("ReceiveNotification", new NotificationDto
                {
                    Id = notification.Id,
                    Type = notification.Type,
                    Title = notification.Title,
                    Message = notification.Message,
                    CreatedAt = notification.CreatedAt,
                    IsRead = notification.IsRead,
                    Data = notification.Data
                });

                _logger.LogInformation($"Notification sent via SignalR to user {notification.UserId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error saving/sending notification for user {notification.UserId}");
                throw;
            }
        }

        private async Task<List<int>> GetAllUsersId()
        {
            using var scope = _serviceProvider.CreateScope();
            var userService = scope.ServiceProvider.GetRequiredService<IUserService>();

            var operators = await userService.GetUsersAsync("Operator");
            var admins = await userService.GetUsersAsync("Admin");

            return operators.Select(u => u.Id)
                .Union(admins.Select(u => u.Id))
                .Distinct()
                .ToList();
        }

        private string GetReadableRequestType(string requestType)
        {
            return requestType switch
            {
                "product.create" => "Product Creation",
                "product.update" => "Product Update",
                "product.delete" => "Product Deletion",
                "product.transfer" => "Product Transfer",
                "route.update" => "Route Update",
                "route.delete" => "Route Deletion",
                _ => requestType.Replace(".", " ").ToTitleCase()
            };
        }

        private string GetActionDescription(string requestType)
        {
            return requestType switch
            {
                "product.create" => "create a new product",
                "product.update" => "update product information",
                "product.delete" => "delete a product",
                "product.transfer" => "transfer a product to another department",
                "route.update" => "update route information",
                "route.delete" => "delete a route",
                _ => requestType.Replace(".", " ")
            };
        }

        public override void Dispose()
        {
            _channel?.Close();
            _connection?.Close();
            base.Dispose();
        }
    }

    // Extension method for title case
    public static class StringExtensions
    {
        public static string ToTitleCase(this string str)
        {
            if (string.IsNullOrEmpty(str))
                return str;

            var words = str.Split(' ');
            for (int i = 0; i < words.Length; i++)
            {
                if (words[i].Length > 0)
                {
                    words[i] = char.ToUpper(words[i][0]) + words[i].Substring(1).ToLower();
                }
            }
            return string.Join(" ", words);
        }
    }
}