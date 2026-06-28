namespace Audit.Models;

public enum AuditStatus
{
    Unstarted,
    InReview,
    Completed,
    Remediated,
    Failed,
    Removed,
}