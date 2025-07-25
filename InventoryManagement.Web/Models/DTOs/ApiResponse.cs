﻿namespace InventoryManagement.Web.Models.DTOs
{
    public record ApiResponse<T>
    {
        public bool IsSuccess { get; set; }
        public bool IsApprovalRequest { get; set; }
        public int? ApprovalRequestId { get; set; }
        public string Message { get; set; } = string.Empty;
        public T? Data { get; set; }
    }
}