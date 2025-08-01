﻿using InventoryManagement.Web.Models.ViewModels;
using InventoryManagement.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InventoryManagement.Web.Controllers
{
    [Authorize(Roles = "Admin")]
    public class ApprovalsController : BaseController
    {
        private readonly IApprovalService _approvalService;

        public ApprovalsController(IApprovalService approvalService, ILogger<ApprovalsController> logger)
            :base(logger)
        {
            _approvalService = approvalService;
        }

        public async Task<IActionResult> Index()
        {
            try
            {
                var pendingRequests = await _approvalService.GetPendingRequestsAsync();
                var statistics = await _approvalService.GetStatisticsAsync();

                var model = new ApprovalDashboardViewModel
                {
                    PendingRequests = pendingRequests,
                    TotalPending = statistics.TotalPending,
                    TotalApproved = statistics.TotalApprovedToday,
                    TotalRejected = statistics.TotalRejectedToday
                };

                return View(model);
            }
            catch (Exception ex)
            {
                return HandleException(ex, new ApprovalDashboardViewModel());
            }
        }


        public async Task<IActionResult> Details(int id)
        {
            try
            {
                var request = await _approvalService.GetRequestDetailsAsync(id);
                if (request == null)
                {
                    return NotFound();
                }
                return PartialView("_ApprovalDetails", request);
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        }


        [HttpPost]
        public async Task<IActionResult> Approve(int id)
        {
            try
            {
                var approvalRequest = await _approvalService.GetRequestDetailsAsync(id);
                if (approvalRequest == null)
                    return NotFound();

                await _approvalService.ApproveRequestAsync(id);
                return Json(new { success = true, message = "Request approved successfully" });
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        }


        [HttpPost]
        public async Task<IActionResult> Reject(int id, string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return HandleError("Rejection reason is required", null,
                    new Dictionary<string, string> { ["reason"] = "Please provide a reason for rejection" });
            }

            try
            {
                var approvalRequest = await _approvalService.GetRequestDetailsAsync(id);
                if (approvalRequest == null)
                    return NotFound();

                await _approvalService.RejectRequestAsync(id, reason);

                return Json(new { success = true, message = "Request rejected succesfully" });
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        }
    }
}