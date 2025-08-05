﻿using System;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using NotificationService.Application.Interfaces;
using NotificationService.Domain.Entities;
using NotificationService.Domain.Repositories;

namespace NotificationService.Application.Services
{
    public class NotificationSender:INotificationSender
    {
        private readonly IHubContext<NotificationHub> _hubContext;
        private readonly INotificationRepository _repository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IUserService _userService;
        private readonly ILogger<NotificationSender> _logger;

        public NotificationSender(
            IHubContext<NotificationHub> hubContext,
            INotificationRepository repository,
            IUnitOfWork unitOfWork,
            IUserService userService,
            ILogger<NotificationSender> logger)
        {
            _hubContext = hubContext;
            _repository = repository;
            _unitOfWork = unitOfWork;
            _userService = userService;
            _logger = logger;
        }

        public async Task SendToUserAsync(int userId,string type,string title,string message,object? data = null)
        {
            //Save to the database
            var notification = new Notification(
                userId,
                type,
                title,
                message,
                data != null ? System.Text.Json.JsonSerializer.Serialize(data) : null);

            await _repository.AddAsync(notification);
            await _unitOfWork.SaveChangesAsync();

            //Send via SignalR
            await _hubContext.Clients.Group($"user-{userId}").SendAsync("ReceiveNotification", new
            {
                notification.Id,
                notification.Type,
                notification.Title,
                notification.Message,
                notification.CreatedAt,
                Data = data
            });
        }
        public async Task SendToRoleAsync(string role, string type, string title, string message, object? data = null)
        {
            // Get users in role from identity service
            var users = await _userService.GetUsersAsync(role);

            _logger.LogInformation($"Sending notification to {users.Count} users in role {role}");

            // Save notification for each user
            foreach (var user in users)
            {
                var notification = new Notification(
                    user.Id,
                    type,
                    title,
                    message,
                    data != null ? System.Text.Json.JsonSerializer.Serialize(data) : null);

                await _repository.AddAsync(notification);
                await _unitOfWork.SaveChangesAsync();
            }

            // Send to role group via SignalR
            await _hubContext.Clients.Group($"role-{role}").SendAsync("ReceiveNotification", new
            {
                Id = 0, // Temporary ID for broadcast
                Type = type,
                Title = title,
                Message = message,
                CreatedAt = DateTime.UtcNow.AddHours(4),
                Data = data
            });
        }
    }
}