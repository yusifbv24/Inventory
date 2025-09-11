// Admin-specific functions
function loadPendingApprovalsCount() {
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
            console.log('Note: Approval service not available');
            // Don't show error toast for this background check
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