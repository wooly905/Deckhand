
namespace WindowsAxon.Agent.Approval;

public sealed record ApprovalDecision(ApprovalOutcome Outcome, bool AllowForConversation, string? Note);
