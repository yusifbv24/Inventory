// Admin-specific functions
function loadPendingApprovalsCount() {
    // Only try to load if user is admin
    if (!isAdmin) return;

    $.ajax({
        url: '/api/approvalrequests',
        type: 'GET',
        data: { pageNumber: 1, pageSize: 1 },
        headers: {
            'Accept': 'application/json',
            'Content-Type': 'application/json'
        },
        success: function (data) {
            const count = data.totalCount || 0;
            updatePendingApprovalsCount(count);
        },
        error: function (xhr, status, error) {
            // Silently handle the error - approval service might not be available
            console.log('Note: Approval service not available');
            // Hide the approval badges since service is not available
            $('#pendingApprovalsCount').hide();
            $('#sidebarPendingCount').hide();
        }
    });
}
function updatePendingApprovalsCount(count) {
    const $badge = $('#pendingApprovalsCount');
    const $sidebarBadge = $('#sidebarPendingCount');

    if (count > 0) {
        $badge.text(count).show();
        $sidebarBadge.text(count).show();
    } else {
        $badge.hide();
        $sidebarBadge.hide();
    }
}    