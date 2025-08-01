﻿using ApprovalService.Application.Events;
using ApprovalService.Application.Interfaces;
using ApprovalService.Domain.Enums;
using ApprovalService.Domain.Repositories;
using MediatR;

namespace ApprovalService.Application.Features.Commands
{
    public class ApproveRequest
    {
        public record Command(int RequestId,int UserId,string UserName): IRequest<bool>;

        public class Handler : IRequestHandler<Command, bool>
        {
            private readonly IApprovalRequestRepository _repository;
            private readonly IUnitOfWork _unitOfWork;
            private readonly IActionExecutor _actionExecutor;
            private readonly IMessagePublisher _messagePublisher;

            public Handler(
                IApprovalRequestRepository repository,
                IUnitOfWork unitOfWork,
                IActionExecutor actionExecutor,
                IMessagePublisher messagePublisher
                )
            {
                _repository = repository;
                _unitOfWork = unitOfWork;
                _actionExecutor = actionExecutor;
                _messagePublisher = messagePublisher;
            }

            public async Task<bool> Handle(Command request, CancellationToken cancellationToken = default)
            {
                var approvalRequest = await _repository.GetByIdAsync(request.RequestId, cancellationToken)
                    ?? throw new InvalidOperationException($"Request {request.RequestId} not found");

                if (approvalRequest.Status != ApprovalStatus.Pending)
                {
                    throw new InvalidOperationException($"Request is no longer pending. Current status: {approvalRequest.Status}");
                }

                approvalRequest.Approve(request.UserId, request.UserName);
                await _repository.UpdateAsync(approvalRequest, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                // Execute the action
                try
                {
                    var executed = await _actionExecutor.ExecuteAsync(
                        approvalRequest.RequestType,
                        approvalRequest.ActionData,
                        cancellationToken);

                    if (executed)
                    {
                        approvalRequest.MarkAsExecuted();
                    }
                    else
                    {
                        approvalRequest.MarkAsFailed("Execution failed");
                    }
                }
                catch (Exception ex)
                {
                    approvalRequest.MarkAsFailed($"Execution error: {ex.Message}");
                }

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                // Notify requester
                var evt = new ApprovalRequestProcessedEvent
                {
                    RequestId = approvalRequest.Id,
                    RequestType = approvalRequest.RequestType,
                    Status = approvalRequest.Status == ApprovalStatus.Executed ? "Approved" : "Failed",
                    ProcessedById = request.UserId,
                    ProcessedByName = request.UserName,
                    RequestedById = approvalRequest.RequestedById,
                    RejectionReason = approvalRequest.Status == ApprovalStatus.Failed ? approvalRequest.RejectionReason : null
                };

                await _messagePublisher.PublishAsync(evt, "approval.request.processed", cancellationToken);
                return true;
            }
        }
    }
}