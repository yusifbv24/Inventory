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
            e.stopPropagation(); // Prevent any other handlers

            const $form = $(this);
            const form = this;
            const formData = new FormData(form);
            const $submitBtn = $form.find('button[type="submit"]');

            // Clear previous validation errors
            $form.find('.is-invalid').removeClass('is-invalid');
            $form.find('.invalid-feedback').remove();
            $form.find('.alert-danger').remove();

            // Store original button state
            const originalBtnHtml = $submitBtn.html();
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
                    // Reset button
                    $submitBtn.prop('disabled', false).html(originalBtnHtml);

                    // Check response type
                    if (response && typeof response === 'object') {
                        if (response.isSuccess === false) {
                            // Handle validation errors
                            if (response.errors) {
                                showValidationErrors($form, response.errors);
                            }
                            showToast(response.message || 'Operation failed', 'error');

                            if (settings.onError) {
                                settings.onError(response.message, response);
                            }
                        } else if (response.isApprovalRequest) {
                            // Handle approval request
                            showToast(response.message || 'Request submitted for approval', 'info');

                            // Play notification sound
                            if (typeof playNotificationSound === 'function') {
                                playNotificationSound();
                            }

                            if (settings.successRedirect) {
                                setTimeout(() => window.location.href = settings.successRedirect, settings.redirectDelay);
                            }
                        } else {
                            // Success
                            showToast(settings.successMessage, 'success');

                            // Play notification sound
                            if (typeof playNotificationSound === 'function') {
                                playNotificationSound();
                            }

                            if (settings.onSuccess) {
                                const continueDefault = settings.onSuccess(response);
                                if (continueDefault === false) return;
                            }

                            if (settings.successRedirect) {
                                setTimeout(() => window.location.href = settings.successRedirect, settings.redirectDelay);
                            }
                        }
                    } else {
                        // Plain success
                        showToast(settings.successMessage, 'success');
                        if (typeof playNotificationSound === 'function') {
                            playNotificationSound();
                        }

                        if (settings.successRedirect) {
                            setTimeout(() => window.location.href = settings.successRedirect, settings.redirectDelay);
                        }
                    }
                },
                error: function (xhr) {
                    // Reset button
                    $submitBtn.prop('disabled', false).html(originalBtnHtml);

                    let errorMessage = 'An error occurred';

                    if (xhr.status === 401) {
                        showToast('Session expired. Redirecting to login...', 'warning');
                        setTimeout(() => window.location.href = '/Account/Login', 2000);
                        return;
                    }

                    if (xhr.responseJSON) {
                        errorMessage = xhr.responseJSON.message || xhr.responseJSON.title || errorMessage;

                        if (xhr.responseJSON.errors) {
                            showValidationErrors($form, xhr.responseJSON.errors);
                        }
                    }

                    // Show error message at top of form
                    const alertHtml = `<div class="alert alert-danger alert-dismissible fade show" role="alert">
                        <i class="fas fa-exclamation-circle me-2"></i>${errorMessage}
                        <button type="button" class="btn-close" data-bs-dismiss="alert"></button>
                    </div>`;

                    $form.prepend(alertHtml);
                    showToast(errorMessage, 'error');

                    if (settings.onError) {
                        settings.onError(errorMessage, xhr);
                    }
                }
            });

            return false; // Ensure form doesn't submit normally
        });
    }

    function showValidationErrors($form, errors) {
        Object.keys(errors).forEach(function (field) {
            const $field = $form.find(`[name="${field}"], [name="${field.charAt(0).toUpperCase() + field.slice(1)}"]`);
            if ($field.length) {
                $field.addClass('is-invalid');
                const messages = Array.isArray(errors[field]) ? errors[field].join(', ') : errors[field];
                $field.after(`<div class="invalid-feedback">${messages}</div>`);
            }
        });
    }

    return {
        handleForm: handleFormSubmit
    };
})();