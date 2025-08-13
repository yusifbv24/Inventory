﻿using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NotificationService.Application.DTOs;
using NotificationService.Application.Interfaces;

namespace NotificationService.Infrastructure.Services
{
    public class WhatsAppService:IWhatsAppService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<WhatsAppService> _logger;
        private readonly WhatsAppSettings _settings;

        public WhatsAppService(
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<WhatsAppService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;

            _settings = configuration.GetSection("Whatsapp").Get<WhatsAppSettings>()
                ?? throw new InvalidOperationException("Whatsapp settings not found in configuration");

            _httpClient.BaseAddress = new Uri(_settings.ApiUrl);
        }

        public async Task<bool> SendGroupMessageAsync(string groupId,string message)
        {
            try
            {
                // Ensure group Id is in the correct format
                if (!groupId.EndsWith("@g.us"))
                    groupId = $"{groupId}@g.us";

                // Create the request payload according to Green API documentation
                var payload = new
                {
                    chatId = groupId,
                    message
                };

                // Construct the API endpoint URL
                var endpoint = $"/waInstance{_settings.IdInstance}/sendMessage/{_settings.ApiTokenInstance}";

                // Send the HTTP request
                var response = await SendRequestAsync(endpoint, payload);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation($"Whatsapp message sent succesfully to group {groupId}");
                    return true;
                }
                var errorContent=await response.Content.ReadAsStringAsync();
                _logger.LogError($"Failed to send WhatsApp message. Status: {response.StatusCode}, Error: {errorContent}");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error sending WhatsApp message to group {groupId}");
                return false;
            }
        }

        public async Task<bool> SendGroupMessageWithImageDataAsync(string groupId, string message, byte[] imageData, string fileName)
        {
            try
            {
                // Ensure group Id is in the correct format
                if (!groupId.EndsWith("@g.us"))
                    groupId = $"{groupId}@g.us";

                // Truncate caption if too long
                if (message.Length > 2048)
                {
                    _logger.LogWarning("Caption exceeds 2048 characters. Truncating...");
                    message = message.Substring(0, 2045) + "...";
                }

                // Check image size before attempting to send
                var imageSizeInMB = imageData.Length / (1024.0 * 1024.0);
                _logger.LogInformation($"Image size: {imageSizeInMB:F2} MB for file: {fileName}");

                if (imageSizeInMB > 10) // Green API typically has a 10MB limit
                {
                    _logger.LogWarning($"Image size {imageSizeInMB:F2}MB exceeds limit. Sending text only.");
                    return await SendGroupMessageAsync(groupId, message);
                }

                // Convert image data to base64 for sending
                var mimeType = GetMimeType(imageData,fileName);
                var base64Image = Convert.ToBase64String(imageData);

                string finalFileName = fileName;

                if (string.IsNullOrEmpty(fileName))
                {
                    var ext = mimeType.Split('/')[1] switch
                    {
                        "jpeg" => "jpg",
                        "png" => "png",
                        "gif" => "gif",
                        "bmp" => "bmp",
                        "webp" => "webp",
                        _ => "jpg"
                    };
                    finalFileName = $"image.{ext}";
                }

                var payload = new
                {
                    chatId = groupId,
                    caption = message,
                    file = $"data:{mimeType};base64,{base64Image}",
                    fileName = finalFileName
                };

                var endpoint = $"/waInstance{_settings.IdInstance}/sendFileByUpload/{_settings.ApiTokenInstance}";

                var response = await SendRequestAsync(endpoint, payload);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation($"WhatsApp message with embedded image sent successfully to group {groupId}");
                    return true;
                }

                _logger.LogError($"Failed to send WhatsApp message with embedded image. Status: {response.StatusCode}, Error: {responseContent}");
                return await SendGroupMessageAsync(groupId, message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error sending WhatsApp message with image to group {groupId}");

                // Fallback to text-only message
                try
                {
                    return await SendGroupMessageAsync(groupId, message);
                }
                catch
                {
                    return false;
                }
            }
        }

        private string GetMimeType(byte[] imageData,string fileName)
        {
            if (!string.IsNullOrEmpty(fileName))
            {
                var extension = Path.GetExtension(fileName)?.ToLowerInvariant() ?? "";
                return extension switch
                {
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".png" => "image/png",
                    ".gif" => "image/gif",
                    ".bmp" => "image/bmp",
                    ".webp" => "image/webp",
                    _ => DetectMimeTypeFromData(imageData) // Fallback to data detection
                };
            }
            return DetectMimeTypeFromData(imageData);
        }

        private string DetectMimeTypeFromData(byte[] imageData)
        {
            if (imageData.Length < 4) return "image/jpeg";

            // PNG detection
            if (imageData[0] == 0x89 && imageData[1] == 0x50 && imageData[2] == 0x4E && imageData[3] == 0x47)
                return "image/png";

            // JPEG detection
            if (imageData[0] == 0xFF && imageData[1] == 0xD8 && imageData[2] == 0xFF)
                return "image/jpeg";

            // GIF detection
            if (imageData[0] == 0x47 && imageData[1] == 0x49 && imageData[2] == 0x46)
                return "image/gif";

            // BMP detection
            if (imageData[0] == 0x42 && imageData[1] == 0x4D)
                return "image/bmp";

            // WEBP detection
            if (imageData.Length > 12 &&
                imageData[0] == 0x52 && imageData[1] == 0x49 &&
                imageData[2] == 0x46 && imageData[3] == 0x46 &&
                imageData[8] == 0x57 && imageData[9] == 0x45 &&
                imageData[10] == 0x42 && imageData[11] == 0x50)
                return "image/webp";

            return "image/jpeg"; // Default
        }

        public string FormatProductNotification(WhatsAppProductNotification notification)
        {
            var message = new StringBuilder();

            // Add header with emoji based on notification type
            var emoji = notification.NotificationType switch
            {
                "created" => "✅",
                "transferred" => "🔄",
                _ => "📌"
            };

            message.AppendLine($"{emoji} *Product {notification.NotificationType.ToUpper()}*");
            message.AppendLine();

            // Add product details
            message.AppendLine($"📦 *Product Details:*");
            message.AppendLine($"• *Inventory Code:* {notification.InventoryCode}");
            message.AppendLine($"• *Vendor:* {notification.Vendor}");
            message.AppendLine($"• *Model:* {notification.Model}");
            if (notification.NotificationType == "created")
            {
                message.AppendLine($"• *Department:* {notification.ToDepartmentName}");

                if (!string.IsNullOrEmpty(notification.ToWorker))
                {
                    message.AppendLine($"• *Assigned Worker:* {notification.ToWorker}");
                }

                if (notification.IsNewItem)
                {
                    message.AppendLine($"• *Status:* 🆕 New Item");
                }
                if(!notification.IsWorking)
                {
                    message.AppendLine($"• *Status:* ❌ Not Working");
                }
            }
            else if(notification.NotificationType =="transferred")
            {
                message.AppendLine($"• *From Department:* {notification.FromDepartmentName}");

                if (!string.IsNullOrEmpty(notification.FromWorker))
                {
                    message.AppendLine($"• *From Worker:* {notification.FromWorker}");
                }

                message.AppendLine($"• *To Department:* {notification.ToDepartmentName}");

                if (!string.IsNullOrEmpty(notification.ToWorker))
                {
                    message.AppendLine($"• *Assigned Worker:* {notification.ToWorker}");
                }
            }

            if (!string.IsNullOrEmpty(notification.Notes))
            {
                message.AppendLine($"• *Notes:* {notification.Notes}");
            }

            message.AppendLine();
            message.AppendLine($"⏰ *Time:* {notification.CreatedAt:dd/MM/yyyy HH:mm}");

            // Add footer
            message.AppendLine();
            message.AppendLine("_This is an automated notification from 166 Logistics Inventory System_");

            return message.ToString();
        }


        private async Task<HttpResponseMessage> SendRequestAsync(string endpoint, object payload)
        {
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _logger.LogDebug($"Sending request to: {_httpClient.BaseAddress}{endpoint}");

            // Log first 100 chars of payload for debugging (be careful not to log sensitive data)
            var payloadPreview = json.Length > 100 ? json.Substring(0, 100) + "..." : json;
            _logger.LogDebug($"Payload preview: {payloadPreview}");

            return await _httpClient.PostAsync(endpoint, content);
        }
    }
}