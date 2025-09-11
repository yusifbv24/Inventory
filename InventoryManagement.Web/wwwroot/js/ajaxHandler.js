window.AjaxHandler = (function () {
    'use strict';

    function handleFormSubmit(formSelector, options) {
        const defaults = {
            beforeSubmit: null,
            onSuccess: null,
            onError: null,
            successMessage: 'Operation completed successfully',
            successRedirect: null,
            redirectDelay: 2000,
            resetOnSuccess: false
        };

        const settings = $.extend({}, defaults, options);

        $(formSelector).off('submit').on('submit', function (e) {
            e.preventDefault();
            e.stopPropagation();

            const $form = $(this);
            const form = this;
            const formData = new FormData(form);
            const $submitBtn = $form.find('button[type="submit"]');

            // Clear previous validation errors
            $form.find('.is-invalid').removeClass('is-invalid');
            $form.find('.invalid-feedback').remove();
            $form.find('.alert-danger').remove();

            // Call beforeSubmit if provided
            if (settings.beforeSubmit) {
                if (settings.beforeSubmit(form) === false) {
                    return false;
                }
            }

            // Store original button state - IMPORTANT: Store before changing
            const originalBtnHtml = $submitBtn.html();
            const originalBtnDisabled = $submitBtn.prop('disabled');

            // Function to reset button state - defined here for closure access
            function resetButton() {
                $submitBtn.prop('disabled', originalBtnDisabled).html(originalBtnHtml);
            }

            // Set loading state
            $submitBtn.prop('disabled', true)
                .html('<span class="spinner-border spinner-border-sm me-2"></span>Processing...');

            $.ajax({
                url: form.action,
                type: form.method,
                data: formData,
                processData: false,
                contentType: false,
                headers: {
                    'X-Requested-With': 'XMLHttpRequest'
                },
                success: function (response) {
                    // Always reset button first
                    resetButton();

                    // Check if this is actually an error response
                    if (response && typeof response === 'object' && response.isSuccess === false) {
                        // This is definitively an error - handle it as such
                        handleErrorResponse(response);
                        return; // CRITICAL: Stop processing here
                    }

                    // Check for approval request (not an error, but special case)
                    if (response && response.isApprovalRequest) {
                        handleApprovalRequest(response);
                        return;
                    }

                    // Only if we get here, it's a true success
                    handleSuccessResponse(response);
                },
                error: function (xhr) {
                    // Always reset button on error
                    resetButton();

                    // Handle various error scenarios
                    if (xhr.status === 401) {
                        showToast('Session expired. Redirecting to login...', 'warning');
                        setTimeout(() => window.location.href = '/Account/Login', 2000);
                        return;
                    }

                    handleAjaxError(xhr);
                }
            });

            // Helper function to handle error responses
            function handleErrorResponse(response) {
                // Show validation errors if present
                if (response.errors) {
                    showValidationErrors($form, response.errors);
                }
                
                // Display error message
                const errorMessage = response.message || 'Operation failed';
                
                // Add alert to form
                const alertHtml = `<div class="alert alert-danger alert-dismissible fade show" role="alert">
                    <i class="fas fa-exclamation-circle me-2"></i>${errorMessage}
                    <button type="button" class="btn-close" data-bs-dismiss="alert"></button>
                </div>`;
                $form.prepend(alertHtml);
                
                // Show toast notification for the error
                showToast(errorMessage, 'error');

                // Call custom error handler if provided
                if (settings.onError) {
                    settings.onError(errorMessage, response);
                }
                
                // Scroll to top to show error
                $('html, body').animate({ scrollTop: 0 }, 'fast');
            }

            // Helper function to handle approval requests
            function handleApprovalRequest(response) {
                const message = response.message || 'Request submitted for approval';
                showToast(message, 'info');
                
                // Call success handler if provided
                if (settings.onSuccess) {
                    const continueDefault = settings.onSuccess(response);
                    if (continueDefault === false) return;
                }
                
                // Redirect if configured
                if (settings.successRedirect) {
                    setTimeout(() => window.location.href = settings.successRedirect, settings.redirectDelay);
                }
            }

            // Helper function to handle successful responses
            function handleSuccessResponse(response) {
                // Only show success message if operation was truly successful
                showToast(settings.successMessage, 'success');
                
                // Play notification sound if available
                if (typeof playNotificationSound === 'function') {
                    playNotificationSound();
                }

                // Call success handler if provided
                if (settings.onSuccess) {
                    const continueDefault = settings.onSuccess(response);
                    if (continueDefault === false) return;
                }

                // Reset form if configured
                if (settings.resetOnSuccess) {
                    form.reset();
                }

                // Redirect if configured
                if (settings.successRedirect) {
                    setTimeout(() => window.location.href = settings.successRedirect, settings.redirectDelay);
                }
            }

            // Helper function to handle AJAX errors
            function handleAjaxError(xhr) {
                let errorMessage = 'An error occurred';

                // Try to extract error message from response
                if (xhr.responseJSON) {
                    errorMessage = xhr.responseJSON.message || 
                                 xhr.responseJSON.title || 
                                 xhr.responseJSON.detail || 
                                 errorMessage;

                    // Show validation errors if present
                    if (xhr.responseJSON.errors) {
                        showValidationErrors($form, xhr.responseJSON.errors);
                    }
                } else if (xhr.responseText && xhr.responseText.length < 500) {
                    // Use response text if it's not too long and not HTML
                    if (!xhr.responseText.includes('<!DOCTYPE') && !xhr.responseText.includes('<html')) {
                        errorMessage = xhr.responseText;
                    }
                }

                // Add status-specific messages
                if (xhr.status === 400) {
                    errorMessage = errorMessage || 'Invalid request. Please check your input.';
                } else if (xhr.status === 404) {
                    errorMessage = 'The requested resource was not found.';
                } else if (xhr.status === 500) {
                    errorMessage = 'Server error occurred. Please try again later.';
                }

                // Display error alert on form
                const alertHtml = `<div class="alert alert-danger alert-dismissible fade show" role="alert">
                    <i class="fas fa-exclamation-circle me-2"></i>${errorMessage}
                    <button type="button" class="btn-close" data-bs-dismiss="alert"></button>
                </div>`;
                $form.prepend(alertHtml);

                // Show toast
                showToast(errorMessage, 'error');

                // Call custom error handler
                if (settings.onError) {
                    settings.onError(errorMessage, xhr);
                }
                
                // Scroll to top to show error
                $('html, body').animate({ scrollTop: 0 }, 'fast');
            }

            return false;
        });
    }

    function showValidationErrors($form, errors) {
        // Handle different error formats
        if (typeof errors === 'string') {
            // Single error message
            const alertHtml = `<div class="alert alert-danger alert-dismissible fade show" role="alert">
                <i class="fas fa-exclamation-circle me-2"></i>${errors}
                <button type="button" class="btn-close" data-bs-dismiss="alert"></button>
            </div>`;
            $form.prepend(alertHtml);
            return;
        }

        // Handle field-specific errors
        Object.keys(errors).forEach(function (field) {
            // Try to find the field with exact name or capitalized version
            const $field = $form.find(`[name="${field}"], [name="${field.charAt(0).toUpperCase() + field.slice(1)}"]`);
            
            if ($field.length) {
                $field.addClass('is-invalid');
                
                // Handle error messages that might be arrays or strings
                let messages = errors[field];
                if (Array.isArray(messages)) {
                    messages = messages.join(', ');
                }
                
                // Remove any existing error message for this field
                $field.siblings('.invalid-feedback').remove();
                
                // Add new error message
                $field.after(`<div class="invalid-feedback">${messages}</div>`);
            }
        });
    }

    return {
        handleForm: handleFormSubmit
    };
})();